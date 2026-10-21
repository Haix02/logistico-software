using System.Security.Claims;
using Logistico.SharedKernel.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Logistico.Web.Data;

/// <summary>Adds the tenant and display name to the sign-in cookie so every request knows them without a database call.</summary>
internal sealed class LogisticoClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(LogisticoClaimTypes.TenantId, user.TenantId.ToString()));
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            identity.AddClaim(new Claim(LogisticoClaimTypes.DisplayName, user.DisplayName));
        }

        return identity;
    }
}
