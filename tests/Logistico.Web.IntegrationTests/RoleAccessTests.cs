using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Logistico.Web.IntegrationTests;

/// <summary>What each staff role may do, enforced by the API (the browser only hides what a role cannot use).</summary>
[Collection("Api")]
public sealed class RoleAccessTests(LogisticoApiFactory factory)
{
    // Anonymous on purpose: customers' tracking page, its self-service requests, and "who am I" for the sign-in check.
    private static readonly string[] PublicRoutes =
    [
        ShipmentsApiRoutes.PublicTracking + "/{trackingNumber}",
        ShipmentsApiRoutes.PublicTracking + "/{trackingNumber}/requests",
        ShipmentsApiRoutes.PublicTracking + "/{trackingNumber}/rating",
        ShipmentsApiRoutes.PublicTracking + "/{trackingNumber}/confirm",
        "api/v1/account/me",
    ];

    [Fact]
    public void Every_api_endpoint_needs_a_staff_role_except_the_public_ones()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.TrimStart('/').StartsWith("api/", StringComparison.Ordinal) == true)
            .ToList();
        Assert.NotEmpty(endpoints);

        var problems = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var route = endpoint.RoutePattern.RawText!.TrimStart('/');
            var methods = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? [];
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).OfType<string>().ToHashSet();
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                if (!PublicRoutes.Contains(route))
                {
                    problems.Add($"{string.Join(',', methods)} {route} is anonymous");
                }

                continue;
            }

            var writes = methods.Any(m => m is not ("GET" or "HEAD"));
            var allowed = writes ? [StaffPolicies.Operate, StaffPolicies.Care, StaffPolicies.Finance, StaffPolicies.Administer, StaffPolicies.Deliver, StaffPolicies.Sell] : StaffPolicies.All;
            if (!policies.Overlaps(allowed) && route != "api/v1/account/logout")
            {
                problems.Add($"{string.Join(',', methods)} {route} has no staff policy ({string.Join(',', policies)})");
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public async Task An_account_without_a_role_sees_no_company_data()
    {
        var response = await factory.CreateUserClient(role: "").GetAsync("/" + ShipmentsApiRoutes.Shipments);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("auth.forbidden", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Signed_in_accounts_get_the_app_shell_for_pages_their_role_cannot_open()
    {
        // The shell holds no data (no prerendering); the app shows "not available for your role" or "waiting for access".
        var viewerPage = await factory.CreateUserClient(role: StaffRoles.Viewer).GetAsync("/orders/new");
        Assert.Equal(HttpStatusCode.OK, viewerPage.StatusCode);
        Assert.Contains("blazor.web", await viewerPage.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await factory.CreateUserClient(role: "").GetAsync("/team")).StatusCode);

        // The API behind those pages still refuses.
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateUserClient(role: StaffRoles.Viewer).GetAsync("/" + TeamApiRoutes.Team)).StatusCode);
    }

    [Fact]
    public async Task Viewers_read_but_change_nothing_and_cannot_export_personal_data()
    {
        var viewer = factory.CreateUserClient(role: StaffRoles.Viewer);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/" + ShipmentsApiRoutes.Shipments)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/" + ShipmentsApiRoutes.Insights)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/" + ShipmentsApiRoutes.Shipments, ApiTestHelpers.InternationalBooking("Viewer Co"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.TryScanAsync(ScanOperation.Receive, "LX260000000017", IraqHubs.Baghdad)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/" + ShipmentsApiRoutes.Export)).StatusCode);
    }

    [Fact]
    public async Task Operations_run_the_day_but_not_prices_rules_warehouses_payouts_or_publishing()
    {
        var operations = factory.CreateUserClient(role: StaffRoles.Operations);

        Assert.Equal(HttpStatusCode.Created, (await operations.PostAsJsonAsync("/" + ShipmentsApiRoutes.Shipments, ApiTestHelpers.InternationalBooking("Ops Co"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await operations.GetAsync("/" + ShipmentsApiRoutes.Export)).StatusCode);

        var pricing = await operations.GetAsync("/" + ShipmentsApiRoutes.Pricing);
        var card = (await pricing.Content.ReadFromJsonAsync<RateCard>())!;
        Assert.Equal(HttpStatusCode.Forbidden, (await operations.PutWithETagAsync(ShipmentsApiRoutes.Pricing, card, pricing.Headers.ETag!)).StatusCode);

        var policyResponse = await operations.GetAsync("/" + ShipmentsApiRoutes.AutomationPolicy);
        var policy = (await policyResponse.Content.ReadFromJsonAsync<AutomationPolicy>())!;
        Assert.Equal(HttpStatusCode.Forbidden, (await operations.PutWithETagAsync(ShipmentsApiRoutes.AutomationPolicy, policy, policyResponse.Headers.ETag!)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await operations.PostAsJsonAsync("/" + ShipmentsApiRoutes.Warehouses, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operations.PostAsJsonAsync("/" + ShipmentsApiRoutes.FinanceSettlements, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operations.GetAsync("/" + ShipmentsApiRoutes.InsightsOpenData)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operations.GetAsync("/" + TeamApiRoutes.Team)).StatusCode);
    }

    [Fact]
    public async Task Finance_sets_prices_and_payouts_but_does_not_book_orders()
    {
        // A company of its own, so the price change does not affect other tests.
        var finance = factory.CreateUserClient(tenant: Guid.CreateVersion7(), role: StaffRoles.Finance);

        var pricing = await finance.GetAsync("/" + ShipmentsApiRoutes.Pricing);
        var card = (await pricing.Content.ReadFromJsonAsync<RateCard>())!;
        card.SameGovernorateFee += 250;
        var saved = await finance.PutWithETagAsync(ShipmentsApiRoutes.Pricing, card, pricing.Headers.ETag!);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());

        Assert.NotEqual(HttpStatusCode.Forbidden, (await finance.PostAsJsonAsync("/" + ShipmentsApiRoutes.FinanceSettlements, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.PostAsJsonAsync("/" + ShipmentsApiRoutes.Shipments, ApiTestHelpers.InternationalBooking("Finance Co"))).StatusCode);
    }
}
