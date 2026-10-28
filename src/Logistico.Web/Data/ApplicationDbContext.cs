using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Web.Data;

/// <summary>ASP.NET Core Identity store (users, roles, logins, passkeys) and the mobile app settings, in the "iam" schema.</summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public const string Schema = "iam";

    public DbSet<MobileAppSettingsRecord> MobileAppSettings => Set<MobileAppSettingsRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>(e => e.Property(u => u.DisplayName).HasMaxLength(200));
        builder.Entity<MobileAppSettingsRecord>(e =>
        {
            e.ToTable("mobile_app_settings");
            e.HasKey(x => x.TenantId);
            e.Property(x => x.Settings).HasColumnType("jsonb");
            e.Property(x => x.UpdatedBy).HasMaxLength(256);
            e.Property(x => x.Version).IsRowVersion();
        });
    }
}
