using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Application;

/// <summary>Line-haul trucks between hubs: open, plan waiting parcels automatically, depart, receive, close.</summary>
internal sealed class TransferPlanner(ShipmentsDbContext db, NumberGenerator numbers, HubDirectory hubs, Activity activity, ITenantContext tenant, TimeProvider clock)
{
    public async Task<HubTransfer> OpenAsync(string fromHub, string toHub, string? vehiclePlate, string? driverName, string? by, CancellationToken ct)
    {
        var network = await hubs.NetworkAsync(tenant.TenantId, ct);
        var from = network.Find(fromHub) ?? throw new DomainException("transfer.unknown_hub", "This hub is not active in your network.", fromHub);
        var to = network.Find(toHub) ?? throw new DomainException("transfer.unknown_hub", "This hub is not active in your network.", toHub);
        var transfer = HubTransfer.Open(tenant.TenantId, await numbers.TransferNumberAsync(ct), from.Code, to.Code, vehiclePlate, driverName, by, clock.GetUtcNow());
        db.Transfers.Add(transfer);
        return transfer;
    }

    /// <summary>
    /// Puts a parcel that is waiting at the wrong hub onto the loading truck for its lane, opening one when needed.
    /// Returns the truck, or null when the parcel does not need to travel.
    /// </summary>
    public async Task<HubTransfer?> PlanAsync(Shipment shipment, CancellationToken ct)
    {
        if (!shipment.NeedsTransfer || shipment.CurrentHubCode is not { } from)
        {
            return null;
        }

        var to = shipment.TargetHubCode;
        if (shipment.TransferId is { } existingId)
        {
            var existing = db.Transfers.Local.FirstOrDefault(t => t.Id == existingId)
                ?? await db.Transfers.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == existingId, ct);
            if (existing is { Status: TransferStatus.Loading } && existing.FromHubCode == from && existing.ToHubCode == to)
            {
                return existing;
            }

            existing?.Unplan(shipment, clock.GetUtcNow());
        }

        var transfer = db.Transfers.Local.FirstOrDefault(t => t.Status == TransferStatus.Loading && t.FromHubCode == from && t.ToHubCode == to)
            ?? await db.Transfers.Include(t => t.Items)
                .Where(t => t.Status == TransferStatus.Loading && t.FromHubCode == from && t.ToHubCode == to)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync(ct)
            ?? await OpenAsync(from, to, null, null, Actor.System.Name, ct);

        transfer.Plan(shipment, clock.GetUtcNow());
        activity.Log(shipment.TenantId, AutomationRule.PlanTransfer, shipment, $"{transfer.Number} {from}>{to}");
        return transfer;
    }

    /// <summary>
    /// After the parcel's next stop changed (a return, a new address): takes it off a loading truck that no longer
    /// goes there and, when trucks are planned automatically, puts it on the right one.
    /// </summary>
    public async Task ReconcileAsync(Shipment shipment, bool autoPlan, CancellationToken ct)
    {
        if (shipment.TransferId is { } truckId)
        {
            var truck = db.Transfers.Local.FirstOrDefault(t => t.Id == truckId)
                ?? await db.Transfers.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == truckId, ct);
            if (truck is { Status: TransferStatus.Loading }
                && (!shipment.NeedsTransfer || truck.FromHubCode != shipment.CurrentHubCode || truck.ToHubCode != shipment.TargetHubCode))
            {
                truck.Unplan(shipment, clock.GetUtcNow());
            }
        }

        if (autoPlan)
        {
            await PlanAsync(shipment, ct);
        }
    }

    /// <summary>Plans every parcel already waiting at the truck's origin hub for its destination hub.</summary>
    public async Task<int> PlanWaitingAsync(HubTransfer transfer, CancellationToken ct)
    {
        var waiting = await WaitingFor(transfer.FromHubCode, transfer.ToHubCode)
            .Where(s => s.TransferId == null)
            .OrderBy(s => s.LastStatusAt)
            .Take(500)
            .ToListAsync(ct);
        foreach (var shipment in waiting)
        {
            transfer.Plan(shipment, clock.GetUtcNow());
        }

        return waiting.Count;
    }

    /// <summary>The truck leaves: loaded parcels go "between hubs", planned-but-not-loaded parcels stay at the hub.</summary>
    public async Task DepartAsync(HubTransfer transfer, DepartTransferRequest request, string? by, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var ids = transfer.Items.Select(i => i.ShipmentId).ToList();
        var shipments = await db.Shipments.Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);

        var transitHours = (await hubs.NetworkAsync(transfer.TenantId, ct)).TransitHours(transfer.FromHubCode, transfer.ToHubCode);
        transfer.DropStale(shipments, now); // lines left by parcels that were returned or moved meanwhile
        var leftBehind = transfer.Depart(request.VehiclePlate, request.DriverName, request.SealNumber, transitHours, by, now);
        foreach (var item in leftBehind)
        {
            if (shipments.TryGetValue(item.ShipmentId, out var parked))
            {
                parked.RemoveFromTransfer(transfer.Id, now);
            }
        }

        var actor = new Actor(by, "scan");
        foreach (var item in transfer.Items)
        {
            shipments[item.ShipmentId].DepartOnTransfer(transfer.Number, transfer.FromHubCode, transfer.ToHubCode, transitHours, actor, now);
        }
    }

    /// <summary>Closes the manifest at the destination hub; parcels not scanned off the truck are flagged missing.</summary>
    public IReadOnlyList<TransferItem> Close(HubTransfer transfer, string? notes, string? by)
    {
        var now = clock.GetUtcNow();
        var missing = transfer.Close(notes, by, now);
        foreach (var item in missing)
        {
            db.Alerts.Add(OpsAlert.ForTransfer(transfer, AlertKind.MissingInTransfer, AlertSeverity.Critical, null, transfer.Number, now, item.ShipmentId, item.TrackingNumber));
            activity.Log(transfer.TenantId, AutomationRule.AlertRaised, item.ShipmentId, item.TrackingNumber, nameof(AlertKind.MissingInTransfer));
        }

        activity.Log(transfer.TenantId, AutomationRule.TransferClosed, null, null, $"{transfer.Number} missing={missing.Count}");
        return missing;
    }

    /// <summary>Parcels sitting at <paramref name="fromHub"/> whose next stop is <paramref name="toHub"/>.</summary>
    public IQueryable<Shipment> WaitingFor(string fromHub, string toHub) =>
        db.Shipments.Where(s => s.CurrentHubCode == fromHub
            && (ShipmentLifecycle.WarehouseStatuses.Contains(s.Status) || s.Status == ShipmentStatus.ReturnInitiated)
            && (ShipmentLifecycle.ReturnStatuses.Contains(s.Status) ? (s.OriginHubCode ?? s.DestinationHubCode) : s.DestinationHubCode) == toHub);
}
