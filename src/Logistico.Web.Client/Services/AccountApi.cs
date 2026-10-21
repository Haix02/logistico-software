using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Logistico.Web.Client.Services;

internal sealed class AccountApi(HttpClient http, AuthenticationStateProvider authState, NavigationManager navigation)
{
    public async Task SignOutAsync()
    {
        await http.PostAsync("api/v1/account/logout", content: null);
        (authState as ServerAuthenticationStateProvider)?.SignedOut();
        navigation.NavigateTo("Account/Login", forceLoad: true);
    }
}
