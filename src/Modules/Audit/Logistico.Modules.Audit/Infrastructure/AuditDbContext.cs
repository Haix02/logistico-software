using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Audit.Infrastructure;

internal sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string Schema = "audit";

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.ToTable("entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Actor).HasMaxLength(256);
            e.Property(x => x.ActorId).HasMaxLength(64);
            e.Property(x => x.Action).HasMaxLength(16);
            e.Property(x => x.Entity).HasMaxLength(64);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.Label).HasMaxLength(256);
            e.Property(x => x.Changes).HasColumnType("jsonb");

            // The log page reads a company's newest entries; a record's history reads one entity.
            e.HasIndex(x => new { x.TenantId, x.OccurredAt }).IsDescending(false, true).HasDatabaseName("ix_entries_tenant_time");
            e.HasIndex(x => new { x.TenantId, x.Entity, x.EntityId, x.OccurredAt }).IsDescending(false, false, false, true).HasDatabaseName("ix_entries_tenant_record");
        });
    }
}
