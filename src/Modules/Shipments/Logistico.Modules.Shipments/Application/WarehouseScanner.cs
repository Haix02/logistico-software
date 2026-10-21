using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Application;

/// <summary>
/// Executes hub station scans (single or bulk) and writes each one to the scan log behind the live hub radar.
/// Receive scans also complete truck arrivals, detect misroutes and plan the next truck automatically.
/// </summary>
internal sealed class WarehouseScanner(
    ShipmentsDbContext db,
    AutomationPolicies policies,
    TransferPlanner planner,
    HubDirectory hubs,
    Activity activity,
    ITenantContext tenant,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<ScanOutcome>> ScanAsync(
        ScanOperation operation,
        IEnumerable<string> trackingNumbers,
        string hubCode,
        string? bin,
        string? courierName,
        Guid? transferId,
        DeliveryFailureReason? failureReason,
        string? user,
        CancellationToken ct)
    {
        var hub = (await hubs.NetworkAsync(tenant.TenantId, ct)).Find(hubCode) ?? throw new DomainException("scan.unknown_hub", "Unknown hub.", hubCode);
        var numbers = trackingNumbers.Select(TrackingNumber.Normalize).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var shipments = await db.Shipments.Where(s => numbers.Contains(s.TrackingNumber)).ToDictionaryAsync(s => s.TrackingNumber, StringComparer.OrdinalIgnoreCase, ct);
        var transfer = transferId is { } id
            ? await db.Transfers.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new DomainException("transfer.not_found", "Truck manifest not found.")
            : null;
        var policy = await policies.GetAsync(tenant.TenantId, ct);
        var now = clock.GetUtcNow();
        var actor = Actor.Scan(user);
        var scans = new List<HubScan>(numbers.Count);

        foreach (var number in numbers)
        {
            if (!shipments.TryGetValue(number, out var shipment))
            {
                scans.Add(HubScan.Record(tenant.TenantId, hub.Code, operation, number, null, false, false, null, null,
                    ShipmentTexts.Problem("shipment.tracking_not_found", number), "shipment.tracking_not_found", user, now));
                continue;
            }

            var misrouted = IsMisrouted(hub, shipment, operation);
            try
            {
                var (message, transferNumber) = await ApplyAsync(operation, shipment, hub, bin, courierName, transfer, failureReason, policy, actor, now, ct);
                if (misrouted)
                {
                    RaiseMisrouted(shipment, hub, now);
                }

                scans.Add(HubScan.Record(tenant.TenantId, hub.Code, operation, number, shipment, true, misrouted, misrouted ? shipment.TargetHubCode : null, transferNumber, message, null, user, now));
            }
            catch (DomainException ex)
            {
                scans.Add(HubScan.Record(tenant.TenantId, hub.Code, operation, number, shipment, false, misrouted, misrouted ? shipment.TargetHubCode : null, null,
                    ShipmentTexts.Problem(ex.Code, ex.Args), ex.Code, user, now));
            }
        }

        db.HubScans.AddRange(scans);
        await db.SaveChangesAsync(ct);
        return scans.Select(scan => ToOutcome(scan, scan.ShipmentId is { } sid ? shipments.Values.FirstOrDefault(s => s.Id == sid) : null)).ToList();
    }

    private async Task<(string Message, string? TransferNumber)> ApplyAsync(
        ScanOperation operation,
        Shipment shipment,
        HubInfo hub,
        string? bin,
        string? courierName,
        HubTransfer? transfer,
        DeliveryFailureReason? failureReason,
        AutomationPolicy policy,
        Actor actor,
        DateTimeOffset now,
        CancellationToken ct)
    {
        switch (operation)
        {
            case ScanOperation.Receive or ScanOperation.Unload:
                {
                    var arriving = ShipmentLifecycle.IsOnLineHaul(shipment.Status) && shipment.TransferId is { } truckId
                        ? transfer?.Id == truckId ? transfer : await db.Transfers.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == truckId, ct)
                        : null;
                    if (arriving is { Status: TransferStatus.InTransit } && arriving.ToHubCode != hub.Code)
                    {
                        throw new DomainException("transfer.wrong_destination", "This truck must be received at its manifest destination.", arriving.Number, arriving.ToHubCode);
                    }

                    if (arriving is { Status: TransferStatus.InTransit })
                    {
                        arriving.Receive(shipment, actor.Name, now);
                    }
                    else if (arriving is null && shipment.TransferId is { } plannedId && shipment.CurrentHubCode != hub.Code)
                    {
                        // Planned on a truck at another hub but physically here now: take it off that truck.
                        var otherTruck = transfer?.Id == plannedId ? transfer : await db.Transfers.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == plannedId, ct);
                        if (otherTruck is { Status: TransferStatus.Loading })
                        {
                            otherTruck.Unplan(shipment, now);
                        }
                    }

                    shipment.ReceiveAtHub(hub, bin, actor, now);
                    var planned = policy.AutoPlanTransfers ? await planner.PlanAsync(shipment, ct) : null;
                    var text = $"{hub.RoutingTag} · {ShipmentTexts.Of(shipment.Status)}" + (shipment.WarehouseBin is { } shelf ? $" · {shelf}" : "");
                    return planned is null
                        ? (text, arriving?.Number)
                        : ($"{text} → {IraqHubs.TagOf(planned.ToHubCode)} ({planned.Number})", planned.Number);
                }

            case ScanOperation.Dispatch:
                shipment.DispatchToCourier(hub, courierName, actor, now);
                activity.Notify(shipment, NotificationTemplate.OutForDelivery, policy);
                return ($"{hub.RoutingTag} → {shipment.CourierName}", null);

            case ScanOperation.Load:
                {
                    // No truck chosen: use the truck the parcel is planned on, or plan it onto the right lane now.
                    var truck = transfer;
                    if (truck is null && shipment.TransferId is { } plannedId)
                    {
                        truck = await db.Transfers.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == plannedId, ct);
                    }

                    if (truck is not { Status: TransferStatus.Loading } || truck.FromHubCode != hub.Code)
                    {
                        truck = transfer ?? (shipment.CurrentHubCode == hub.Code ? await planner.PlanAsync(shipment, ct) : null);
                    }

                    if (truck is null)
                    {
                        throw new DomainException("transfer.wrong_lane", "This parcel is not waiting for this truck.", shipment.TrackingNumber);
                    }

                    if (truck.FromHubCode != hub.Code)
                    {
                        throw new DomainException("transfer.wrong_lane", "This parcel is not waiting for this truck.", shipment.TrackingNumber);
                    }

                    // Check before touching another truck, so a wrong scan leaves the parcel on the truck it was on.
                    truck.EnsureCanTake(shipment);
                    if (shipment.TransferId is { } other && other != truck.Id)
                    {
                        var previous = await db.Transfers.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == other, ct);
                        if (previous is { Status: TransferStatus.Loading })
                        {
                            previous.Unplan(shipment, now);
                        }
                    }

                    truck.Load(shipment, now);
                    return ($"{truck.Number} · {IraqHubs.TagOf(truck.FromHubCode)} → {IraqHubs.TagOf(truck.ToHubCode)}", truck.Number);
                }

            case ScanOperation.Deliver:
                shipment.AdvanceTo(ShipmentStatus.Delivered, now, new MilestoneDetails(Location: hub.Code, CourierName: courierName ?? shipment.CourierName), actor.Source, actor.Name, now);
                activity.Notify(shipment, NotificationTemplate.Delivered, policy);
                return (ShipmentTexts.Of(shipment.Status), null);

            case ScanOperation.Fail:
                {
                    var decision = shipment.RecordFailedAttempt(failureReason ?? DeliveryFailureReason.NoAnswer, courierName, null, policy, actor, now);
                    activity.AfterFailure(shipment, decision, policy);
                    return (ShipmentTexts.Of(shipment.Status), null);
                }

            case ScanOperation.Verify:
                {
                    return (hub.Code == shipment.TargetHubCode
                        ? $"{hub.RoutingTag} ✓ · {ShipmentTexts.Of(shipment.Status)}"
                        : $"{hub.RoutingTag} → {IraqHubs.TagOf(shipment.TargetHubCode)} · {ShipmentTexts.Of(shipment.Status)}", null);
                }

            default:
                throw new DomainException("scan.unsupported", "This scan is done from the returns screen.");
        }
    }

    /// <summary>A parcel has no business at a hub that is neither its first-mile hub nor its destination/return hub.</summary>
    private static bool IsMisrouted(HubInfo hub, Shipment shipment, ScanOperation operation) =>
        operation is ScanOperation.Receive or ScanOperation.Unload or ScanOperation.Verify
        && hub.Code != shipment.DestinationHubCode
        && hub.Code != shipment.OriginHubCode
        && hub.Code != shipment.TargetHubCode;

    private void RaiseMisrouted(Shipment shipment, HubInfo hub, DateTimeOffset now)
    {
        var key = OpsAlert.Subject(AlertKind.Misrouted, "shipment", shipment.Id);
        if (db.Alerts.Local.Any(a => a.SubjectKey == key && a.IsOpen) || db.Alerts.Any(a => a.SubjectKey == key && a.ResolvedAt == null))
        {
            return;
        }

        db.Alerts.Add(OpsAlert.ForShipment(shipment, AlertKind.Misrouted, AlertSeverity.Warning, null, $"{hub.Code}>{shipment.TargetHubCode}", now));
        activity.Log(shipment.TenantId, AutomationRule.AlertRaised, shipment, nameof(AlertKind.Misrouted));
    }

    public static ScanOutcome ToOutcome(HubScan scan, Shipment? shipment) => new(
        scan.Id,
        scan.Ok,
        scan.Operation,
        scan.TrackingNumber,
        scan.ShipmentId,
        shipment?.Status ?? scan.Status,
        scan.HubCode,
        shipment?.WarehouseBin ?? scan.WarehouseBin,
        scan.IsMisrouted,
        scan.ExpectedHubCode,
        shipment is { NeedsTransfer: true } ? shipment.TargetHubCode : null,
        scan.TransferNumber,
        shipment?.ConsigneeName,
        shipment?.DestinationCode,
        shipment?.CourierName ?? scan.CourierName,
        shipment?.CodAmount,
        shipment?.CodCurrency,
        scan.Message,
        scan.ProblemCode,
        scan.ScannedBy,
        scan.ScannedAt);
}
