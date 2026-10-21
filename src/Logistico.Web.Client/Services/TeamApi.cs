using Logistico.SharedKernel.Identity;

namespace Logistico.Web.Client.Services;

/// <summary>Typed client for the company's staff accounts (administrators only).</summary>
internal sealed class TeamApi(HttpClient http) : ApiClient(http)
{
    public Task<TeamList> ListAsync(CancellationToken ct = default) => GetAsync<TeamList>(TeamApiRoutes.Team, ct);

    public Task<TeamMember> CreateAsync(CreateTeamMemberRequest request, CancellationToken ct = default) =>
        SendAsync<TeamMember>(HttpMethod.Post, TeamApiRoutes.Team, request, null, ct);

    /// <summary>Gives the member another role (a courier also needs <paramref name="courierCode"/>); null takes their access away.</summary>
    public Task ChangeRoleAsync(string id, string? role, string? courierCode = null, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, TeamApiRoutes.Role(id), new ChangeTeamRoleRequest { Role = role, CourierCode = courierCode }, null, ct);

    public Task LockAsync(string id, CancellationToken ct = default) => SendAsync(HttpMethod.Post, TeamApiRoutes.Lock(id), null, null, ct);

    public Task UnlockAsync(string id, CancellationToken ct = default) => SendAsync(HttpMethod.Post, TeamApiRoutes.Unlock(id), null, null, ct);

    public Task ResetPasswordAsync(string id, string password, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, TeamApiRoutes.Password(id), new ResetTeamPasswordRequest { Password = password }, null, ct);
}
