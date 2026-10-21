using System.Security.Claims;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;

namespace Logistico.Web.Infrastructure;

/// <summary>
/// Tenant for the current work: an explicit <see cref="TenantScope"/> (background jobs), otherwise the signed-in user's
/// claim, otherwise the default tenant (anonymous requests).
/// </summary>
internal sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid TenantId
    {
        get
        {
            if (TenantScope.Override is { } tenantOverride)
            {
                return tenantOverride;
            }

            var principal = accessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return Tenants.Default;
            }

            if (Guid.TryParse(principal.FindFirstValue(LogisticoClaimTypes.TenantId), out var tenantId) && tenantId != Guid.Empty)
            {
                return tenantId;
            }

            throw new InvalidOperationException("An authenticated user must have a valid tenant claim.");
        }
    }
}
