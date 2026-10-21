using System.Net;
using System.Net.Http.Json;
using Logistico.SharedKernel.Identity;

namespace Logistico.Web.IntegrationTests;

/// <summary>Administrators manage their own company's staff accounts; the last administrator cannot be removed.</summary>
[Collection("Api")]
public sealed class TeamTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task Admins_add_members_change_their_role_and_lock_them()
    {
        var tenant = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: tenant);
        var email = $"hub.{Guid.NewGuid():N}@logistico.test";

        var created = await admin.PostAsJsonAsync("/" + TeamApiRoutes.Team, new CreateTeamMemberRequest
        {
            Email = email,
            DisplayName = "حيدر — مخزن بغداد",
            Role = StaffRoles.Operations,
            Password = "Hub-Station#2026",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var member = (await created.Content.ReadFromJsonAsync<TeamMember>())!;

        var team = (await admin.GetFromJsonAsync<TeamList>("/" + TeamApiRoutes.Team))!;
        Assert.Equal(StaffRoles.Operations, Assert.Single(team.Members, m => m.Email == email).Role);

        // The same address cannot be used twice, and roles must be real ones.
        var again = await admin.PostAsJsonAsync("/" + TeamApiRoutes.Team, new CreateTeamMemberRequest { Email = email, DisplayName = "x", Password = "Another#2026x" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var badRole = await admin.PutAsJsonAsync("/" + TeamApiRoutes.Role(member.Id), new ChangeTeamRoleRequest { Role = "Owner" });
        Assert.Equal(HttpStatusCode.BadRequest, badRole.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/" + TeamApiRoutes.Role(member.Id), new ChangeTeamRoleRequest { Role = StaffRoles.Viewer })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/" + TeamApiRoutes.Lock(member.Id), null)).StatusCode);
        var locked = Assert.Single((await admin.GetFromJsonAsync<TeamList>("/" + TeamApiRoutes.Team))!.Members, m => m.Id == member.Id);
        Assert.Equal((StaffRoles.Viewer, true), (locked.Role, locked.IsLocked));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/" + TeamApiRoutes.Unlock(member.Id), null)).StatusCode);
        Assert.False(Assert.Single((await admin.GetFromJsonAsync<TeamList>("/" + TeamApiRoutes.Team))!.Members, m => m.Id == member.Id).IsLocked);

        // Another company's administrator neither sees nor changes this member.
        var stranger = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        Assert.DoesNotContain((await stranger.GetFromJsonAsync<TeamList>("/" + TeamApiRoutes.Team))!.Members, m => m.Id == member.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync("/" + TeamApiRoutes.Role(member.Id), new ChangeTeamRoleRequest { Role = StaffRoles.Admin })).StatusCode);
    }

    [Fact]
    public async Task The_last_active_administrator_cannot_be_demoted_or_locked()
    {
        var admin = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var created = await admin.PostAsJsonAsync("/" + TeamApiRoutes.Team, new CreateTeamMemberRequest
        {
            Email = $"owner.{Guid.NewGuid():N}@logistico.test",
            DisplayName = "Owner",
            Role = StaffRoles.Admin,
            Password = "Owner-Account#2026",
        });
        var owner = (await created.Content.ReadFromJsonAsync<TeamMember>())!;

        var demote = await admin.PutAsJsonAsync("/" + TeamApiRoutes.Role(owner.Id), new ChangeTeamRoleRequest { Role = StaffRoles.Viewer });
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Contains("team.last_admin", await demote.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync("/" + TeamApiRoutes.Lock(owner.Id), null)).StatusCode);
    }
}
