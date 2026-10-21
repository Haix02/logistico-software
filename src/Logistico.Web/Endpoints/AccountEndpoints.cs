using System.Security.Claims;
using Logistico.SharedKernel.Identity;
using Logistico.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace Logistico.Web.Endpoints;

/// <summary>Session endpoints for the WebAssembly client (cookie authentication, same origin).</summary>
internal static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/api/v1/account").WithTags("Account");

        account.MapGet("/me", (ClaimsPrincipal user) =>
        {
            if (user.Identity?.IsAuthenticated != true)
            {
                return TypedResults.Ok(UserInfo.Anonymous);
            }

            return TypedResults.Ok(new UserInfo(
                IsAuthenticated: true,
                UserName: user.Identity.Name,
                DisplayName: user.FindFirstValue(LogisticoClaimTypes.DisplayName) ?? user.Identity.Name,
                TenantId: Guid.TryParse(user.FindFirstValue(LogisticoClaimTypes.TenantId), out var tenantId) ? tenantId : null,
                Roles: user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList()));
        }).AllowAnonymous();

        account.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.NoContent();
        }).RequireAuthorization();

        return app;
    }
}
