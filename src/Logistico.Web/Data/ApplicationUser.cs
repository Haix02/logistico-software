using Logistico.SharedKernel;
using Microsoft.AspNetCore.Identity;

namespace Logistico.Web.Data;

public class ApplicationUser : IdentityUser
{
    /// <summary>The logistics company this user works for.</summary>
    public Guid TenantId { get; set; } = Tenants.Default;

    [PersonalData]
    public string? DisplayName { get; set; }
}
