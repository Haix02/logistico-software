using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel.Identity;
using Logistico.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Logistico.Web.IntegrationTests;

/// <summary>
/// Team changes that must stay safe under pressure: simultaneous administrators, temporary lockouts, bad input, password
/// resets by an administrator, and the customer-support role.
/// </summary>
[Collection("Api")]
public sealed class TeamSafetyTests(LogisticoApiFactory factory)
{
    private const string Password = "Basra-Hub-2026!";

    [Fact]
    public async Task Two_administrators_locking_each_other_at_once_leave_one_of_them_active()
    {
        var company = Guid.CreateVersion7();
        var setup = factory.CreateUserClient(tenant: company);
        var first = await AddAsync(setup, StaffRoles.Admin);
        var second = await AddAsync(setup, StaffRoles.Admin);
        var asFirst = factory.CreateUserClient(first.Email, company, userId: first.Id);
        var asSecond = factory.CreateUserClient(second.Email, company, userId: second.Id);

        for (var round = 0; round < 5; round++)
        {
            var answers = await Task.WhenAll(
                asFirst.PostAsync("/" + TeamApiRoutes.Lock(second.Id), null),
                asSecond.PostAsync("/" + TeamApiRoutes.Lock(first.Id), null));

            // The per-company lock applies them one after the other: the second sees the last administrator and refuses.
            Assert.Single(answers, a => a.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(answers, a => a.StatusCode == HttpStatusCode.Conflict);
            var team = (await setup.GetFromJsonAsync<TeamList>("/" + TeamApiRoutes.Team))!;
            Assert.Single(team.Members, m => m.Role == StaffRoles.Admin && !m.IsLocked);

            await setup.PostAsync("/" + TeamApiRoutes.Unlock(first.Id), null);
            await setup.PostAsync("/" + TeamApiRoutes.Unlock(second.Id), null);
        }
    }

    [Fact]
    public async Task A_lockout_after_wrong_passwords_is_not_shown_or_counted_as_a_locked_account()
    {
        var company = Guid.CreateVersion7();
        var setup = factory.CreateUserClient(tenant: company);
        var forgetful = await AddAsync(setup, StaffRoles.Admin);
        var other = await AddAsync(setup, StaffRoles.Admin);

        // Five wrong passwords: Identity locks the account for a few minutes.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.SetLockoutEndDateAsync((await users.FindByIdAsync(forgetful.Id))!, DateTimeOffset.UtcNow.AddMinutes(5));
        }

        var listed = Assert.Single((await setup.GetFromJsonAsync<TeamList>("/" + TeamApiRoutes.Team))!.Members, m => m.Id == forgetful.Id);
        Assert.False(listed.IsLocked);
        Assert.NotNull(listed.LockedOutUntil);

        // The temporarily locked-out administrator still counts, so the other one can be locked by a colleague.
        Assert.Equal(HttpStatusCode.NoContent, (await setup.PostAsync("/" + TeamApiRoutes.Lock(other.Id), null)).StatusCode);

        // Changing the role of someone who is locked out does not turn the lockout into a permanent one.
        Assert.Equal(HttpStatusCode.Conflict, (await setup.PutAsJsonAsync("/" + TeamApiRoutes.Role(forgetful.Id), new ChangeTeamRoleRequest { Role = StaffRoles.Viewer })).StatusCode);
        await setup.PostAsync("/" + TeamApiRoutes.Unlock(other.Id), null);
        Assert.Equal(HttpStatusCode.NoContent, (await setup.PutAsJsonAsync("/" + TeamApiRoutes.Role(forgetful.Id), new ChangeTeamRoleRequest { Role = StaffRoles.Viewer })).StatusCode);
        listed = Assert.Single((await setup.GetFromJsonAsync<TeamList>("/" + TeamApiRoutes.Team))!.Members, m => m.Id == forgetful.Id);
        Assert.Equal((StaffRoles.Viewer, false), (listed.Role, listed.IsLocked));

        // Unlocking clears the temporary lockout too.
        Assert.Equal(HttpStatusCode.NoContent, (await setup.PostAsync("/" + TeamApiRoutes.Unlock(forgetful.Id), null)).StatusCode);
        Assert.Null(Assert.Single((await setup.GetFromJsonAsync<TeamList>("/" + TeamApiRoutes.Team))!.Members, m => m.Id == forgetful.Id).LockedOutUntil);
    }

    [Fact]
    public async Task Missing_or_unknown_roles_are_answered_with_a_validation_problem()
    {
        var admin = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var nullRole = await admin.PostAsync("/" + TeamApiRoutes.Team, JsonContent.Create(new
        {
            email = $"x.{Guid.NewGuid():N}@logistico.test",
            displayName = "X",
            role = (string?)null,
            password = Password,
        }));
        Assert.Equal(HttpStatusCode.BadRequest, nullRole.StatusCode);
        Assert.Contains("\"Role\"", await nullRole.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var noBody = await admin.PutAsync("/" + TeamApiRoutes.Role("anyone"), JsonContent.Create<object?>(null));
        Assert.Equal(HttpStatusCode.BadRequest, noBody.StatusCode);
    }

    [Fact]
    public async Task Administrators_set_a_new_password_for_a_colleague_but_not_for_themselves()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var colleague = await AddAsync(admin, StaffRoles.Operations);

        var weak = await admin.PostAsJsonAsync("/" + TeamApiRoutes.Password(colleague.Id), new ResetTeamPasswordRequest { Password = "onlylowercaseletters" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("يجب أن تحتوي كلمة المرور على رقم", await weak.Content.ReadAsStringAsync(), StringComparison.Ordinal); // Arabic by default

        const string fresh = "Najaf-Branch-77?";
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/" + TeamApiRoutes.Password(colleague.Id), new ResetTeamPasswordRequest { Password = fresh })).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var account = (await users.FindByIdAsync(colleague.Id))!;
            Assert.True(await users.CheckPasswordAsync(account, fresh));
            Assert.False(await users.CheckPasswordAsync(account, Password));
        }

        var self = await AddAsync(admin, StaffRoles.Admin);
        var asSelf = factory.CreateUserClient(self.Email, company, userId: self.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await asSelf.PostAsJsonAsync("/" + TeamApiRoutes.Password(self.Id), new ResetTeamPasswordRequest { Password = fresh })).StatusCode);

        var otherCompany = factory.CreateUserClient("boss@other.test", LogisticoApiFactory.OtherTenant);
        Assert.Equal(HttpStatusCode.NotFound, (await otherCompany.PostAsJsonAsync("/" + TeamApiRoutes.Password(colleague.Id), new ResetTeamPasswordRequest { Password = fresh })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateUserClient(role: StaffRoles.Operations).PostAsJsonAsync("/" + TeamApiRoutes.Password(colleague.Id), new ResetTeamPasswordRequest { Password = fresh })).StatusCode);
    }

    [Fact]
    public async Task Customer_support_answers_customers_but_does_not_run_the_operation()
    {
        var admin = factory.CreateUserClient();
        var merchant = await admin.CreateMerchantAsync("Support Desk Shop", "support.desk.shop");
        var order = await admin.BookAsync(ApiTestHelpers.DomesticOrder(merchant, "Support Desk Customer"));

        var support = factory.CreateUserClient("support@logistico.test", role: StaffRoles.CustomerSupport);
        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync("/" + ShipmentsApiRoutes.ById(order.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync("/" + ShipmentsApiRoutes.Confirmations)).StatusCode);

        var request = await support.PostAsJsonAsync("/" + ShipmentsApiRoutes.Requests, new CreateServiceRequest { ShipmentId = order.Id, Type = RequestType.Complaint, Details = "اتصل الزبون مرتين" });
        Assert.Equal(HttpStatusCode.Created, request.StatusCode);
        var created = (await request.Content.ReadFromJsonAsync<ServiceRequestItem>())!;
        var resolved = await support.PostAsJsonAsync("/" + ShipmentsApiRoutes.RequestResolve(created.Id), new ResolveServiceRequest { Outcome = RequestStatus.Resolved, Note = "تم الاتصال" });
        Assert.True(resolved.IsSuccessStatusCode, await resolved.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Forbidden, (await support.PostAsJsonAsync("/" + ShipmentsApiRoutes.Shipments, ApiTestHelpers.DomesticOrder(merchant, "Not allowed"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.TryScanAsync(ScanOperation.Receive, order.TrackingNumber, IraqHubs.Baghdad)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.GetAsync("/" + ShipmentsApiRoutes.FinanceSummary)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.GetAsync("/" + TeamApiRoutes.Team)).StatusCode);

        // Viewers read the same inbox but cannot write to it.
        var viewer = factory.CreateUserClient("viewer@logistico.test", role: StaffRoles.Viewer);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/" + ShipmentsApiRoutes.Requests)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/" + ShipmentsApiRoutes.Requests, new CreateServiceRequest { ShipmentId = order.Id, Type = RequestType.Complaint, Details = "x" })).StatusCode);
    }

    private static async Task<TeamMember> AddAsync(HttpClient admin, string role)
    {
        var response = await admin.PostAsJsonAsync("/" + TeamApiRoutes.Team, new CreateTeamMemberRequest
        {
            Email = $"{role.ToLowerInvariant()}.{Guid.NewGuid():N}@logistico.test",
            DisplayName = role,
            Role = role,
            Password = Password,
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TeamMember>())!;
    }
}
