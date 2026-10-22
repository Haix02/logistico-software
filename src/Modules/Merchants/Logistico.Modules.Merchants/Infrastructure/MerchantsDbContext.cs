using Logistico.Modules.Merchants.Domain;
using Logistico.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Merchants.Infrastructure;

internal sealed class MerchantsDbContext(DbContextOptions<MerchantsDbContext> options, ITenantContext tenant) : DbContext(options)
{
    public const string Schema = "merchant";

    /// <summary>The folded search column (see <see cref="SearchText"/>), kept by PostgreSQL.</summary>
    public const string SearchColumn = "SearchText";

    public DbSet<Merchant> Merchants => Set<Merchant>();

    private Guid CurrentTenantId => tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Merchant>(e =>
        {
            e.ToTable("merchants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.InstagramHandle).HasMaxLength(30);
            e.Property(x => x.ContactName).HasMaxLength(120);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Governorate).HasMaxLength(5);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.Version).IsRowVersion();

            e.HasIndex(x => new { x.TenantId, x.InstagramHandle }).IsUnique().HasFilter("instagram_handle IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.Name });
            e.HasIndex(x => new { x.TenantId, x.Phone });

            // Free-text search reads one folded column (SearchText): name, Instagram handle, contact and phone.
            e.Property<string>(SearchColumn)
                .HasColumnName("search_text")
                .HasComputedColumnSql(SearchText.Sql("name || ' ' || coalesce(instagram_handle, '') || ' ' || contact_name || ' ' || phone"), stored: true);

            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
    }
}
