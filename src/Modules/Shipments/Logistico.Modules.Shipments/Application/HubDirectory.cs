using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Application;

/// <summary>
/// A company's warehouse network, loaded once per request from its active warehouses. A company that has never set up
/// warehouses starts with the standard Iraqi network (<see cref="IraqHubs.Standard"/>), created on first use.
/// </summary>
internal sealed class HubDirectory(ShipmentsDbContext db, TimeProvider clock)
{
    private readonly Dictionary<Guid, HubNetwork> cache = [];

    public async Task<HubNetwork> NetworkAsync(Guid tenantId, CancellationToken ct)
    {
        if (cache.TryGetValue(tenantId, out var cached))
        {
            return cached;
        }

        var hubs = await LoadAsync(tenantId, ct);
        if (hubs is null)
        {
            await EnsureStandardAsync(tenantId, ct);
            hubs = await LoadAsync(tenantId, ct);
        }

        var network = hubs is { Count: > 0 } ? new HubNetwork(hubs) : IraqHubs.Default;
        cache[tenantId] = network;
        return network;
    }

    /// <summary>Forget the loaded network after warehouses change, so the rest of the request routes with the new one.</summary>
    public void Invalidate(Guid tenantId) => cache.Remove(tenantId);

    /// <summary>Creates the standard hubs for a company that has no warehouses yet (safe when two requests race).</summary>
    public async Task EnsureStandardAsync(Guid tenantId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await CreateStandardIfMissingAsync(tenantId, ct);
            return;
        }

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await CreateStandardIfMissingAsync(tenantId, ct);
            await transaction.CommitAsync(ct);
        });
    }

    private async Task CreateStandardIfMissingAsync(Guid tenantId, CancellationToken ct)
    {
        // Serialises the check-then-insert per company until the transaction ends.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext('logistico.warehouses'), hashtext({tenantId.ToString()}))", ct);
        if (!await db.Warehouses.IgnoreQueryFilters().AnyAsync(w => w.TenantId == tenantId, ct))
        {
            var now = clock.GetUtcNow();
            db.Warehouses.AddRange(IraqHubs.Standard.Select((hub, index) => Warehouse.FromStandard(tenantId, hub, index == 0, now)));
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Active hubs, main warehouse first; null when the company has no warehouse rows at all.</summary>
    private async Task<List<HubInfo>?> LoadAsync(Guid tenantId, CancellationToken ct)
    {
        var rows = await db.Warehouses.IgnoreQueryFilters().AsNoTracking()
            .Where(w => w.TenantId == tenantId)
            .OrderByDescending(w => w.IsPrimary).ThenBy(w => w.CreatedAt).ThenBy(w => w.Code)
            .ToListAsync(ct);
        return rows.Count == 0 ? null : rows.Where(w => w.IsActive).Select(w => w.ToHubInfo()).ToList();
    }
}
