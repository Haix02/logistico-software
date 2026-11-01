using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel.Identity;
using Logistico.SharedKernel.Setup;

namespace Logistico.Web.IntegrationTests;

/// <summary>The getting-started guide follows the company's data; the company profile; the installation health checks.</summary>
[Collection("Api")]
public sealed class GettingStartedTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task Steps_are_done_when_the_company_data_says_so()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);

        var start = await GuideAsync(admin);
        Assert.Equal(GettingStartedSteps.All.Select(s => s.Id), start.Steps.Select(s => s.Id));
        Assert.All(start.Steps.Where(s => s.Id != GettingStartedSteps.Health), s => Assert.False(s.Done, s.Id));
        Assert.Equal(GettingStartedSteps.Company, start.Next!.Id);

        // The company names itself; a courier, a seller, an order and a scan follow.
        var saved = await admin.PutAsJsonAsync("/" + SetupApiRoutes.Company, new CompanyProfile { Name = "سريع البصرة", Phone = "0780 111 2233", Governorate = "IQ-BA" });
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        await admin.EnsureCourierAsync();
        var shop = await admin.CreateMerchantAsync("متجر الدليل", "guide.shop");
        var order = await admin.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون الدليل"));
        await admin.ScanAsync(ScanOperation.Receive, order.TrackingNumber, IraqHubs.Baghdad, "A-1");

        var later = await GuideAsync(admin);
        string[] expected = [GettingStartedSteps.Company, GettingStartedSteps.Couriers, GettingStartedSteps.Sellers, GettingStartedSteps.FirstOrder, GettingStartedSteps.FirstScan];
        Assert.Equal(expected.Order(), later.Steps.Where(s => s.Done).Select(s => s.Id).Where(id => id != GettingStartedSteps.Health).Order());

        // "Not for us": a step can be skipped and brought back; the next step moves on.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/" + SetupApiRoutes.SkipStep(GettingStartedSteps.Warehouses), null)).StatusCode);
        var skipped = await GuideAsync(admin);
        Assert.True(skipped.Steps.Single(s => s.Id == GettingStartedSteps.Warehouses).Skipped);
        Assert.Equal(GettingStartedSteps.Prices, skipped.Next!.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/" + SetupApiRoutes.SkipStep(GettingStartedSteps.Warehouses))).StatusCode);
        Assert.False((await GuideAsync(admin)).Steps.Single(s => s.Id == GettingStartedSteps.Warehouses).Skipped);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/" + SetupApiRoutes.SkipStep("no-such-step"), null)).StatusCode);

        // Other companies start from nothing.
        Assert.DoesNotContain((await GuideAsync(factory.CreateUserClient(tenant: Guid.CreateVersion7()))).Steps, s => s.Done && s.Id != GettingStartedSteps.Health);
    }

    [Fact]
    public async Task Each_role_sees_the_steps_it_can_do()
    {
        var company = Guid.CreateVersion7();
        string[] operations = [GettingStartedSteps.Couriers, GettingStartedSteps.Sellers, GettingStartedSteps.SortPlan, GettingStartedSteps.FirstOrder, GettingStartedSteps.FirstScan];
        Assert.Equal(operations, (await GuideAsync(factory.CreateUserClient("ops@guide.test", company, StaffRoles.Operations))).Steps.Select(s => s.Id));
        Assert.Equal([GettingStartedSteps.Prices], (await GuideAsync(factory.CreateUserClient("money@guide.test", company, StaffRoles.Finance))).Steps.Select(s => s.Id));
        Assert.Empty((await GuideAsync(factory.CreateUserClient("viewer@guide.test", company, StaffRoles.Viewer))).Steps);

        // Skipping changes what the company does: not for read-only roles.
        var viewer = factory.CreateUserClient("viewer@guide.test", company, StaffRoles.Viewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/" + SetupApiRoutes.SkipStep(GettingStartedSteps.Couriers), null)).StatusCode);
    }

    [Fact]
    public async Task The_company_profile_is_checked_and_never_saved_over_a_newer_change()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var bad = await admin.PutAsJsonAsync("/" + SetupApiRoutes.Company, new CompanyProfile { Name = "شركة", Phone = "123", Governorate = "IQ-ZZ" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var problems = await bad.Content.ReadAsStringAsync();
        Assert.Contains("Phone", problems, StringComparison.Ordinal);
        Assert.Contains("Governorate", problems, StringComparison.Ordinal);

        var first = await admin.PutAsJsonAsync("/" + SetupApiRoutes.Company, new CompanyProfile { Name = "شركة النهرين", Phone = "0770 555 1212" });
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        var etag = first.Headers.ETag!;

        // A second save needs the version it was based on.
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await admin.PutAsJsonAsync("/" + SetupApiRoutes.Company, new CompanyProfile { Name = "شركة النهرين" })).StatusCode);
        var renamed = await admin.PutWithETagAsync(SetupApiRoutes.Company, new CompanyProfile { Name = "النهرين للتوصيل", Phone = "0770 555 1212" }, etag);
        Assert.True(renamed.IsSuccessStatusCode, await renamed.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await admin.PutWithETagAsync(SetupApiRoutes.Company, new CompanyProfile { Name = "قديم" }, etag)).StatusCode);

        // Everyone in the company reads it; only administrators change it.
        var viewer = factory.CreateUserClient("viewer@profile.test", company, StaffRoles.Viewer);
        Assert.Equal("النهرين للتوصيل", (await viewer.GetFromJsonAsync<CompanyProfileView>("/" + SetupApiRoutes.Company))!.Profile.Name);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync("/" + SetupApiRoutes.Company, new CompanyProfile { Name = "x" })).StatusCode);
    }

    [Fact]
    public async Task Administrators_see_what_the_installation_still_needs()
    {
        var checks = (await factory.CreateUserClient().GetFromJsonAsync<SystemChecksView>("/" + SetupApiRoutes.SystemChecks))!;
        var byId = checks.Checks.ToDictionary(c => c.Id);
        Assert.Equal(SystemCheckState.Ok, byId["migrations"].State);
        Assert.Equal(SystemCheckState.Ok, byId["collation"].State);
        Assert.Equal(SystemCheckState.Ok, byId["database"].State);
        Assert.Equal(SystemCheckState.Warning, byId["email"].State); // the no-op sender cannot send password resets
        Assert.Equal(SystemCheckState.Problem, byId["https"].State); // the test server is plain HTTP outside Development
        Assert.True(checks.HasProblems);

        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateUserClient("ops@health.test", role: StaffRoles.Operations).GetAsync("/" + SetupApiRoutes.SystemChecks)).StatusCode);
    }

    private static async Task<GettingStartedView> GuideAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<GettingStartedView>("/" + SetupApiRoutes.GettingStarted))!;
}
