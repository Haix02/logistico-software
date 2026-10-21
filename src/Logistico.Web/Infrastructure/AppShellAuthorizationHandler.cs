using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Components.Endpoints;

namespace Logistico.Web.Infrastructure;

/// <summary>
/// The app's pages are an empty WebAssembly shell (no prerendering) whose data comes from the API, which checks the role
/// itself. A signed-in user whose role does not open a page (or who has no role yet) therefore gets the shell, and the
/// app explains what is missing, instead of the bare account "access denied" page. Everything else (API calls, account
/// pages, anonymous visitors) keeps the default behaviour.
/// </summary>
internal sealed class AppShellAuthorizationHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && context.User.Identity?.IsAuthenticated == true
            && HttpMethods.IsGet(context.Request.Method)
            && !context.Request.Path.StartsWithSegments("/api")
            && context.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>()?.Type.Assembly == typeof(Logistico.Web.Client._Imports).Assembly)
        {
            return next(context);
        }

        return _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
