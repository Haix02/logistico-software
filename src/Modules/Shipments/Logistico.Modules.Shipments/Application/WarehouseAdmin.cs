using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Iraq;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Application;

/// <summary>
/// Adds, edits and retires warehouses while keeping the network consistent: one main warehouse, every governorate
/// covered at most once, and no warehouse retired while parcels or trucks still depend on it.
/// </summary>
internal sealed class WarehouseAdmin(ShipmentsDbContext db, HubDirectory hubs, ITenantContext tenant, TimeProvider clock)
{
    public async Task<Warehouse> CreateAsync(WarehouseRequest request, string? by, CancellationToken ct)
    {
        await hubs.EnsureStandardAsync(tenant.TenantId, ct);
        var tag = Warehouse.NormalizeTag(request.RoutingTag);
        if (await db.Warehouses.AnyAsync(w => w.RoutingTag == tag, ct))
        {
            throw new DomainException("warehouse.tag_taken", "Another warehouse already uses this routing tag.", tag);
        }

        var all = await db.Warehouses.ToListAsync(ct);
        var warehouse = Warehouse.Create(tenant.TenantId, request, by, clock.GetUtcNow());
        await SaveAsync(warehouse, request, all, isNew: true, ct);
        return warehouse;
    }

    public async Task UpdateAsync(Warehouse warehouse, WarehouseRequest request, string? by, CancellationToken ct)
    {
        var wasActive = warehouse.IsActive;
        var wasPrimary = warehouse.IsPrimary;
        if (wasPrimary && !request.IsPrimary)
        {
            throw new DomainException("warehouse.primary_required", "Make another warehouse the main one first.");
        }

        if (wasActive && !request.IsActive)
        {
            await EnsureCanRetireAsync(warehouse, ct);
        }

        var all = await db.Warehouses.ToListAsync(ct);
        warehouse.Update(request, by, clock.GetUtcNow());
        await SaveAsync(warehouse, request, all, isNew: false, ct, afterSave: wasActive && !warehouse.IsActive ? RerouteAroundAsync : null);
    }

    /// <summary>
    /// Parcels still heading for (or returning to) a retired warehouse go to the warehouse that now covers their
    /// governorate. None of them is at the retired warehouse or on a truck to it (retiring is refused otherwise).
    /// </summary>
    private async Task RerouteAroundAsync(Warehouse retired, CancellationToken ct)
    {
        hubs.Invalidate(tenant.TenantId);
        var network = await hubs.NetworkAsync(tenant.TenantId, ct);
        var now = clock.GetUtcNow();
        var affected = await db.Shipments
            .Where(s => !ShipmentLifecycle.ClosedStatuses.Contains(s.Status)
                && (s.DestinationHubCode == retired.Code || s.OriginHubCode == retired.Code))
            .ToListAsync(ct);
        foreach (var shipment in affected)
        {
            shipment.RerouteAround(retired.Code, network, now);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SaveAsync(Warehouse warehouse, WarehouseRequest request, List<Warehouse> all, bool isNew, CancellationToken ct,
        Func<Warehouse, CancellationToken, Task>? afterSave = null)
    {
        var now = clock.GetUtcNow();
        var others = all.Where(w => w.Id != warehouse.Id && w.IsActive).ToList();

        var conflicts = others
            .SelectMany(w => w.Covers.Intersect(warehouse.Covers, StringComparer.OrdinalIgnoreCase).Select(g => (Warehouse: w, Governorate: g)))
            .ToList();
        if (conflicts.Count > 0)
        {
            if (!request.TakeOverCoverage)
            {
                var names = string.Join("، ", conflicts.Select(c => $"{IraqGovernorates.NameOrCode(c.Governorate)} ({c.Warehouse.RoutingTag})"));
                throw new DomainException("warehouse.coverage_conflict", "Some governorates are covered by another warehouse.", names);
            }

            foreach (var group in conflicts.GroupBy(c => c.Warehouse))
            {
                group.Key.GiveUp(group.Select(c => c.Governorate).ToList(), now);
            }
        }

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // Only one main warehouse may exist at any moment: clear the old one before saving the new one.
            var previousPrimary = others.Where(w => w.IsPrimary).ToList();
            if (warehouse.IsPrimary && previousPrimary.Count > 0)
            {
                foreach (var previous in previousPrimary)
                {
                    previous.MakePrimary(false, now);
                }

                warehouse.MakePrimary(false, now);
                if (isNew)
                {
                    db.Warehouses.Add(warehouse);
                }

                await db.SaveChangesAsync(ct);
                warehouse.MakePrimary(true, now);
            }
            else if (isNew)
            {
                db.Warehouses.Add(warehouse);
            }

            await db.SaveChangesAsync(ct);
            if (afterSave is not null)
            {
                await afterSave(warehouse, ct);
            }

            await transaction.CommitAsync(ct);
        });

        hubs.Invalidate(tenant.TenantId);
    }

    /// <summary>Retiring is refused while parcels sit there, trucks still run to or from it, or nothing else would be left.</summary>
    private async Task EnsureCanRetireAsync(Warehouse warehouse, CancellationToken ct)
    {
        if (!await db.Warehouses.AnyAsync(w => w.Id != warehouse.Id && w.IsActive, ct))
        {
            throw new DomainException("warehouse.last_active", "At least one warehouse must stay active.");
        }

        var held = ShipmentLifecycle.WarehouseStatuses.Concat(ShipmentLifecycle.ReturnStatuses).Append(ShipmentStatus.OutForDelivery).ToArray();
        var parcels = await db.Shipments.CountAsync(s => s.CurrentHubCode == warehouse.Code && held.Contains(s.Status), ct);
        var trucks = await db.Transfers.CountAsync(t => t.Status != TransferStatus.Received && (t.FromHubCode == warehouse.Code || t.ToHubCode == warehouse.Code), ct);
        if (parcels > 0 || trucks > 0)
        {
            throw new DomainException("warehouse.not_empty", "Move its parcels and finish its trucks before retiring this warehouse.", parcels, trucks);
        }
    }
}
