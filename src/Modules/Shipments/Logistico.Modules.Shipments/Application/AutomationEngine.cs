using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Logistico.Modules.Shipments.Application;

/// <summary>
/// The periodic sweep for one company: raises alerts for parcels, trucks and requests that are late or stuck (and
/// clears them when the problem is gone), plans parcels onto trucks, and moves returns that reached the seller's hub
/// to "ready for hand-over". Safe to run on several servers: a PostgreSQL advisory lock lets one sweep run per company.
/// </summary>
internal sealed class AutomationEngine(
    ShipmentsDbContext db,
    AutomationPolicies policies,
    TransferPlanner planner,
    Activity activity,
    TimeProvider clock,
    ILogger<AutomationEngine> logger)
{
    private const int MaxPerRule = 2000;

    private static readonly AlertKind[] SweptKinds =
    [
        AlertKind.PickupOverdue, AlertKind.WarehouseDwell, AlertKind.OutForDeliveryOverdue, AlertKind.PromiseMissed,
        AlertKind.ReturnNotCollected, AlertKind.TransferOverdue, AlertKind.RequestOverdue, AlertKind.ConfirmationOverdue,
    ];

    public sealed record SweepResult(bool Ran, int Raised, int Resolved, int Planned, int ReturnsReady, int AutoCancelled = 0);

    public async Task<SweepResult> SweepAsync(Guid tenantId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var locked = await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtext('logistico.automation'), hashtext({tenantId.ToString()})) AS \"Value\"")
            .SingleAsync(ct);
        if (!locked)
        {
            return new(false, 0, 0, 0, 0);
        }

        var now = clock.GetUtcNow();
        var policy = await policies.GetAsync(tenantId, ct);
        var candidates = new Dictionary<string, OpsAlert>();

        await CollectShipmentAlertsAsync(policy, now, candidates, ct);
        await CollectTransferAlertsAsync(policy, now, candidates, ct);
        await CollectRequestAlertsAsync(now, candidates, ct);
        var (raised, resolved) = await ReconcileAsync(tenantId, candidates, now, ct);
        resolved += await ResolveSettledEventAlertsAsync(now, ct);

        var planned = policy.AutoPlanTransfers ? await PlanWaitingParcelsAsync(ct) : 0;
        var returnsReady = await AdvanceReturnsAtSellerHubAsync(now, ct);
        var autoCancelled = policy.AutoCancelUnconfirmedHours > 0 ? await CancelUnconfirmedAsync(policy, now, ct) : 0;

        var settings = await policies.GetForUpdateAsync(tenantId, ct);
        settings.MarkSwept(now);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // A parcel changed while we worked on it (a scan, an edit); the next sweep picks it up again.
            logger.LogWarning(ex, "Automation sweep for tenant {TenantId} skipped conflicting changes", tenantId);
            return new(true, 0, 0, 0, 0);
        }

        return new(true, raised, resolved, planned, returnsReady, autoCancelled);
    }

    private async Task CollectShipmentAlertsAsync(AutomationPolicy policy, DateTimeOffset now, Dictionary<string, OpsAlert> into, CancellationToken ct)
    {
        var pickupBefore = now.AddHours(-policy.PickupSlaHours);
        var dwellBefore = now.AddHours(-policy.WarehouseDwellHours);
        var courierBefore = now.AddHours(-policy.OutForDeliveryHours);
        var returnBefore = now.AddDays(-policy.ReturnCollectionDays);
        var confirmBefore = now.AddHours(-policy.ConfirmationHours);

        // Orders waiting for the customer's confirmation are not late for pickup; they are late for confirmation.
        await AddAsync(AlertKind.PickupOverdue, s => s.Status == ShipmentStatus.Booked && s.PickupRequired && s.Confirmation != ConfirmationStatus.Pending && s.LastStatusAt < pickupBefore, policy.PickupSlaHours);
        await AddAsync(AlertKind.ConfirmationOverdue, s => s.Status == ShipmentStatus.Booked && s.Confirmation == ConfirmationStatus.Pending && s.LastStatusAt < confirmBefore, policy.ConfirmationHours);
        await AddAsync(AlertKind.WarehouseDwell, s => ShipmentLifecycle.WarehouseStatuses.Contains(s.Status) && s.LastStatusAt < dwellBefore, policy.WarehouseDwellHours);
        await AddAsync(AlertKind.OutForDeliveryOverdue, s => s.Status == ShipmentStatus.OutForDelivery && s.LastStatusAt < courierBefore, policy.OutForDeliveryHours);
        await AddAsync(AlertKind.ReturnNotCollected, s => s.Status == ShipmentStatus.ReturnAtHub && s.LastStatusAt < returnBefore, policy.ReturnCollectionDays * 24);

        // Promise missed: still on its way after the promised/rescheduled time (domestic deliveries only).
        var late = await db.Shipments.AsNoTracking()
            .Where(s => s.ServiceType == ServiceType.Domestic && s.EtaAt != null && s.EtaAt < now
                && !ShipmentLifecycle.ClosedStatuses.Contains(s.Status) && !ShipmentLifecycle.ReturnStatuses.Contains(s.Status))
            .OrderBy(s => s.EtaAt)
            .Take(MaxPerRule)
            .Select(s => new { s.Id, s.TenantId, s.TrackingNumber, s.CurrentHubCode, s.DestinationHubCode, s.DestinationCode, s.EtaAt })
            .ToListAsync(ct);
        foreach (var s in late)
        {
            var hours = (int)(now - s.EtaAt!.Value).TotalHours;
            Put(into, NewShipmentAlert(AlertKind.PromiseMissed, hours >= 48 ? AlertSeverity.Critical : AlertSeverity.Warning, s.Id, s.TenantId, s.TrackingNumber, s.CurrentHubCode ?? s.DestinationHubCode, s.DestinationCode, hours, now));
        }

        async Task AddAsync(AlertKind kind, System.Linq.Expressions.Expression<Func<Shipment, bool>> filter, int thresholdHours)
        {
            var rows = await db.Shipments.AsNoTracking().Where(filter)
                .OrderBy(s => s.LastStatusAt)
                .Take(MaxPerRule)
                .Select(s => new { s.Id, s.TenantId, s.TrackingNumber, s.CurrentHubCode, s.DestinationHubCode, s.DestinationCode, s.LastStatusAt })
                .ToListAsync(ct);
            foreach (var s in rows)
            {
                var hours = (int)(now - s.LastStatusAt).TotalHours;
                var severity = kind == AlertKind.OutForDeliveryOverdue || hours >= thresholdHours * 2 ? AlertSeverity.Critical : AlertSeverity.Warning;
                Put(into, NewShipmentAlert(kind, severity, s.Id, s.TenantId, s.TrackingNumber, s.CurrentHubCode ?? s.DestinationHubCode, s.DestinationCode, hours, now));
            }
        }
    }

    private async Task CollectTransferAlertsAsync(AutomationPolicy policy, DateTimeOffset now, Dictionary<string, OpsAlert> into, CancellationToken ct)
    {
        var trucks = await db.Transfers.AsNoTracking()
            .Where(t => t.Status == TransferStatus.InTransit && t.ExpectedAt != null && t.ExpectedAt < now.AddHours(-policy.LineHaulGraceHours))
            .OrderBy(t => t.ExpectedAt)
            .Take(MaxPerRule)
            .ToListAsync(ct);
        foreach (var t in trucks)
        {
            var hours = (int)(now - t.ExpectedAt!.Value).TotalHours;
            Put(into, OpsAlert.ForTransfer(t, AlertKind.TransferOverdue, hours >= 12 ? AlertSeverity.Critical : AlertSeverity.Warning, hours, null, now));
        }
    }

    private async Task CollectRequestAlertsAsync(DateTimeOffset now, Dictionary<string, OpsAlert> into, CancellationToken ct)
    {
        var overdue = await db.Requests.AsNoTracking()
            .Where(r => (r.Status == RequestStatus.Open || r.Status == RequestStatus.InProgress) && r.DueAt < now)
            .OrderBy(r => r.DueAt)
            .Take(MaxPerRule)
            .ToListAsync(ct);
        foreach (var r in overdue)
        {
            Put(into, OpsAlert.ForRequest(r, (int)(now - r.DueAt).TotalHours, now));
        }
    }

    /// <summary>Raises new alerts, refreshes ongoing ones and auto-resolves those whose condition is gone.</summary>
    private async Task<(int Raised, int Resolved)> ReconcileAsync(Guid tenantId, Dictionary<string, OpsAlert> candidates, DateTimeOffset now, CancellationToken ct)
    {
        var open = await db.Alerts.Where(a => a.ResolvedAt == null && SweptKinds.Contains(a.Kind)).ToListAsync(ct);
        var openByKey = open.ToDictionary(a => a.SubjectKey);
        var raised = 0;
        var resolved = 0;

        foreach (var (key, candidate) in candidates)
        {
            if (openByKey.TryGetValue(key, out var existing))
            {
                existing.Touch(candidate.Severity, candidate.Hours, now);
                continue;
            }

            db.Alerts.Add(candidate);
            activity.Log(tenantId, AutomationRule.AlertRaised, candidate.ShipmentId, candidate.TrackingNumber ?? candidate.TransferNumber ?? candidate.RequestNumber, candidate.Kind.ToString());
            raised++;
        }

        foreach (var alert in open.Where(a => !candidates.ContainsKey(a.SubjectKey)))
        {
            alert.Resolve(Domain.Actor.System.Name, automatic: true, now);
            activity.Log(tenantId, AutomationRule.AlertResolved, alert.ShipmentId, alert.TrackingNumber ?? alert.TransferNumber ?? alert.RequestNumber, alert.Kind.ToString());
            resolved++;
        }

        return (raised, resolved);
    }

    /// <summary>Misrouted and missing parcels are cleared once they reach the right hub again (or the journey ended).</summary>
    private async Task<int> ResolveSettledEventAlertsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var open = await db.Alerts
            .Where(a => a.ResolvedAt == null && (a.Kind == AlertKind.Misrouted || a.Kind == AlertKind.MissingInTransfer) && a.ShipmentId != null)
            .ToListAsync(ct);
        if (open.Count == 0)
        {
            return 0;
        }

        var ids = open.Select(a => a.ShipmentId!.Value).ToList();
        var parcels = await db.Shipments.AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.Status, s.CurrentHubCode, s.DestinationHubCode, s.OriginHubCode })
            .ToDictionaryAsync(s => s.Id, ct);
        var resolved = 0;
        foreach (var alert in open)
        {
            if (!parcels.TryGetValue(alert.ShipmentId!.Value, out var s))
            {
                continue;
            }

            var target = ShipmentLifecycle.IsReturning(s.Status) ? s.OriginHubCode ?? s.DestinationHubCode : s.DestinationHubCode;
            var settled = alert.Kind == AlertKind.Misrouted
                ? s.CurrentHubCode == target || ShipmentLifecycle.ClosedStatuses.Contains(s.Status) || s.Status == ShipmentStatus.OutForDelivery
                : !ShipmentLifecycle.IsOnLineHaul(s.Status);
            if (settled)
            {
                alert.Resolve(Domain.Actor.System.Name, automatic: true, now);
                activity.Log(alert.TenantId, AutomationRule.AlertResolved, alert.ShipmentId, alert.TrackingNumber, alert.Kind.ToString());
                resolved++;
            }
        }

        return resolved;
    }

    /// <summary>Safety net for parcels at the wrong hub that are not on a truck yet (e.g. rules switched on later).</summary>
    private async Task<int> PlanWaitingParcelsAsync(CancellationToken ct)
    {
        var waiting = await db.Shipments
            .Where(s => s.CurrentHubCode != null && s.TransferId == null
                && (ShipmentLifecycle.WarehouseStatuses.Contains(s.Status) || s.Status == ShipmentStatus.ReturnInitiated)
                && (ShipmentLifecycle.ReturnStatuses.Contains(s.Status) ? (s.OriginHubCode ?? s.DestinationHubCode) : s.DestinationHubCode) != s.CurrentHubCode)
            .OrderBy(s => s.LastStatusAt)
            .Take(500)
            .ToListAsync(ct);
        var planned = 0;
        foreach (var shipment in waiting)
        {
            try
            {
                planned += await planner.PlanAsync(shipment, ct) is null ? 0 : 1;
            }
            catch (DomainException ex)
            {
                // For example a hub that is no longer active: the parcel waits for a person, the sweep goes on.
                logger.LogWarning("Could not plan {TrackingNumber} onto a truck: {Code}", shipment.TrackingNumber, ex.Code);
            }
        }

        return planned;
    }

    /// <summary>Orders the customer never confirmed are cancelled after the company's limit (when it set one).</summary>
    private async Task<int> CancelUnconfirmedAsync(AutomationPolicy policy, DateTimeOffset now, CancellationToken ct)
    {
        var before = now.AddHours(-policy.AutoCancelUnconfirmedHours);
        var stale = await db.Shipments
            .Where(s => s.Status == ShipmentStatus.Booked && s.Confirmation == ConfirmationStatus.Pending && s.ConfirmationRequestedAt < before)
            .OrderBy(s => s.ConfirmationRequestedAt)
            .Take(MaxPerRule)
            .ToListAsync(ct);
        foreach (var shipment in stale)
        {
            shipment.Cancel(ShipmentTexts.Get("AutoCancelReason"), Domain.Actor.System, now);
            activity.Log(shipment.TenantId, AutomationRule.AutoCancel, shipment, $"{policy.AutoCancelUnconfirmedHours}h");
        }

        return stale.Count;
    }

    /// <summary>A return that is already at the seller's hub is ready to be handed over.</summary>
    private async Task<int> AdvanceReturnsAtSellerHubAsync(DateTimeOffset now, CancellationToken ct)
    {
        var ready = await db.Shipments
            .Where(s => s.Status == ShipmentStatus.ReturnInitiated && s.CurrentHubCode != null && s.CurrentHubCode == (s.OriginHubCode ?? s.DestinationHubCode))
            .OrderBy(s => s.LastStatusAt)
            .Take(500)
            .ToListAsync(ct);
        foreach (var shipment in ready)
        {
            shipment.AdvanceTo(ShipmentStatus.ReturnAtHub, now, new MilestoneDetails(Location: shipment.CurrentHubCode, WarehouseBin: shipment.WarehouseBin), "system", Domain.Actor.System.Name, now);
        }

        return ready.Count;
    }

    private static OpsAlert NewShipmentAlert(AlertKind kind, AlertSeverity severity, Guid id, Guid tenantId, string trackingNumber, string? hub, string governorate, int hours, DateTimeOffset now) =>
        OpsAlert.ForShipmentRow(kind, severity, id, tenantId, trackingNumber, hub, governorate, hours, now);

    private static void Put(Dictionary<string, OpsAlert> into, OpsAlert alert) => into.TryAdd(alert.SubjectKey, alert);
}
