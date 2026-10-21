using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Application;

/// <summary>Starts returns and hands returned parcels back to sellers against a signed manifest.</summary>
internal sealed class ReturnDesk(ShipmentsDbContext db, NumberGenerator numbers, AutomationPolicies policies, TransferPlanner planner, Activity activity, TimeProvider clock)
{
    public async Task StartAsync(Shipment shipment, ReturnReason reason, string? notes, string? by, CancellationToken ct)
    {
        var policy = await policies.GetAsync(shipment.TenantId, ct);
        shipment.StartReturn(reason, notes, new Actor(by), clock.GetUtcNow());
        activity.Notify(shipment, NotificationTemplate.ReturnStarted, policy);
        await planner.ReconcileAsync(shipment, policy.AutoPlanTransfers, ct);
    }

    /// <summary>All parcels must belong to one seller and wait at the same hub; returns the manifest number.</summary>
    public async Task<(string Number, IReadOnlyList<Shipment> Shipments)> HandOverAsync(HandOverReturnsRequest request, string? by, CancellationToken ct)
    {
        var ids = request.ShipmentIds.Distinct().ToList();
        var shipments = await db.Shipments.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        if (shipments.Count != ids.Count)
        {
            throw new DomainException("shipment.not_found", "Shipment not found.");
        }

        if (shipments.Select(s => s.CurrentHubCode).Distinct().Count() > 1)
        {
            throw new DomainException("return.mixed_hubs", "Hand over parcels from one hub at a time.");
        }

        // The person signing the manifest collects for one seller only.
        if (shipments.Select(s => (s.MerchantId, Shipper: s.MerchantId is null ? s.ShipperName : null)).Distinct().Count() > 1)
        {
            throw new DomainException("return.mixed_sellers", "Hand over one seller's parcels at a time.");
        }

        var number = await numbers.ReturnManifestNumberAsync(ct);
        var now = clock.GetUtcNow();
        var actor = new Actor(by);
        foreach (var shipment in shipments)
        {
            var hub = shipment.CurrentHubCode;
            shipment.HandOverToSeller(number, request.ReceivedBy, actor, now);
            db.HubScans.Add(HubScan.Record(shipment.TenantId, hub ?? shipment.TargetHubCode, ScanOperation.HandOver, shipment.TrackingNumber, shipment,
                true, false, null, null, $"{number} · {ShipmentTexts.Of(shipment.Status)}", null, by, now));
        }

        return (number, shipments);
    }
}
