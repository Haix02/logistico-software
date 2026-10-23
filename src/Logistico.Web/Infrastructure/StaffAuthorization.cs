using Logistico.SharedKernel.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Logistico.Web.Infrastructure;

/// <summary>Registers the staff policies and gives every module endpoint a role requirement by default.</summary>
internal static class StaffAuthorization
{
    private static readonly HashSet<string> ExplicitPolicies =
        [StaffPolicies.Operate, StaffPolicies.Care, StaffPolicies.Finance, StaffPolicies.Administer, StaffPolicies.Deliver, StaffPolicies.Sell, StaffPolicies.Personal];

    public static AuthorizationBuilder AddStaffPolicies(this AuthorizationBuilder builder)
    {
        foreach (var policy in StaffPolicies.All)
        {
            // A staff role is only meaningful inside a company: an account without a valid tenant claim gets nothing.
            builder.AddPolicy(policy, p => p
                .RequireAuthenticatedUser()
                .RequireRole(StaffPolicies.RolesFor(policy))
                .RequireAssertion(context =>
                    Guid.TryParse(context.User.FindFirst(LogisticoClaimTypes.TenantId)?.Value, out var tenantId) && tenantId != Guid.Empty));
        }

        return builder;
    }

    /// <summary>
    /// Every endpoint mapped in this group needs a staff role: <see cref="StaffPolicies.Staff"/> to read and
    /// <see cref="StaffPolicies.Operate"/> to change anything, unless it is anonymous (public tracking) or asks for a
    /// stricter policy itself (<c>.RequireAuthorization(StaffPolicies.Finance)</c>). Runs last, after the modules'
    /// own conventions, so a module cannot forget a role requirement.
    /// </summary>
    public static RouteGroupBuilder RequireStaffRoles(this RouteGroupBuilder group)
    {
        ((IEndpointConventionBuilder)group).Finally(endpoint =>
        {
            if (endpoint.Metadata.OfType<IAllowAnonymous>().Any()
                || endpoint.Metadata.OfType<IAuthorizeData>().Any(a => a.Policy is { } p && ExplicitPolicies.Contains(p)))
            {
                return;
            }

            var changes = endpoint.Metadata.OfType<HttpMethodMetadata>()
                .SelectMany(m => m.HttpMethods)
                .Any(method => !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method));
            endpoint.Metadata.Add(new AuthorizeAttribute(changes ? StaffPolicies.Operate : StaffPolicies.Staff));
        });
        return group;
    }
}
