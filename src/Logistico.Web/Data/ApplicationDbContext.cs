using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Web.Data;

/// <summary>ASP.NET Core Identity store (users, roles, logins, passkeys), the company profile and the mobile app settings, in the "iam" schema.</summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public const string Schema = "iam";

    public DbSet<MobileAppSettingsRecord> MobileAppSettings => Set<MobileAppSettingsRecord>();

    public DbSet<CompanyProfileRecord> CompanyProfiles => Set<CompanyProfileRecord>();

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
        builder.Entity<CompanyProfileRecord>(e =>
        {
            e.ToTable("company_profiles");
            e.HasKey(x => x.TenantId);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Governorate).HasMaxLength(5);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.SkippedSteps).HasColumnType("text[]");
            e.Property(x => x.UpdatedBy).HasMaxLength(256);
            e.Property(x => x.Version).IsRowVersion();
            e.Ignore(x => x.IsSet);
        });
    }
}
