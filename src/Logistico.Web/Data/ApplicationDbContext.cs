using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Web.Data;

/// <summary>ASP.NET Core Identity store (users, roles, logins, passkeys) in the "iam" schema.</summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public const string Schema = "iam";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>(e => e.Property(u => u.DisplayName).HasMaxLength(200));
    }
}
