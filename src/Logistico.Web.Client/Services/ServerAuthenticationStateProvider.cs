using System.Net.Http.Json;
using System.Security.Claims;
using Logistico.SharedKernel.Identity;
using Microsoft.AspNetCore.Components.Authorization;

namespace Logistico.Web.Client.Services;

/// <summary>
/// Asks the server who is signed in (GET /api/v1/account/me). The cookie itself is HttpOnly and never visible to the browser code.
/// </summary>
internal sealed class ServerAuthenticationStateProvider(HttpClient http) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private Task<AuthenticationState>? _state;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state ??= LoadAsync();

    public void SignedOut()
    {
        _state = Task.FromResult(Anonymous);
        NotifyAuthenticationStateChanged(_state);
    }

    private async Task<AuthenticationState> LoadAsync()
    {
        try
        {
            var me = await http.GetFromJsonAsync<UserInfo>("api/v1/account/me");
            if (me is not { IsAuthenticated: true })
            {
                return Anonymous;
            }

            List<Claim> claims = [new(ClaimTypes.Name, me.UserName ?? "")];
            if (me.DisplayName is not null)
            {
                claims.Add(new(LogisticoClaimTypes.DisplayName, me.DisplayName));
            }

            if (me.TenantId is { } tenantId)
            {
                claims.Add(new(LogisticoClaimTypes.TenantId, tenantId.ToString()));
            }

            claims.AddRange((me.Roles ?? []).Select(role => new Claim(ClaimTypes.Role, role)));
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "logistico.cookie")));
        }
        catch (HttpRequestException)
        {
            return Anonymous;
        }
    }
}
