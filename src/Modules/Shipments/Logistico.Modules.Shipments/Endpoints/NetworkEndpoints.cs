using Logistico.Modules.Shipments.Application;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Logistico.SharedKernel.Iraq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Endpoints;

/// <summary>Live picture of the whole country for the Iraq network map (one round of grouped SQL queries).</summary>
internal static class NetworkEndpoints
{
    public static IEndpointRouteBuilder MapNetworkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/" + ShipmentsApiRoutes.Network, OverviewAsync)
            .WithTags("Iraq network")
            .WithSummary("Active parcels per governorate, workload per hub and trucks per lane.")
            .RequireAuthorization(StaffPolicies.Staff);
        return app;
    }

    private static async Task<Ok<NetworkOverview>> OverviewAsync(ShipmentsDbContext db, AutomationPolicies policies, HubDirectory directory, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var startOfDay = Api.StartOfBaghdadDay(now);
        var closed = ShipmentLifecycle.ClosedStatuses;
        var returning = ShipmentLifecycle.ReturnStatuses;
        var warehouse = ShipmentLifecycle.WarehouseStatuses;
        var domestic = db.Shipments.AsNoTracking().Where(s => s.ServiceType == ServiceType.Domestic);

        var governorates = await domestic
            .Where(s => !closed.Contains(s.Status) || s.DeliveredAt >= startOfDay)
            .GroupBy(s => s.DestinationCode)
            .Select(g => new GovernorateLoad(
                g.Key,
                g.Count(s => !closed.Contains(s.Status)),
                g.Count(s => warehouse.Contains(s.Status)),
                g.Count(s => s.Status == ShipmentStatus.OutForDelivery),
                g.Count(s => s.Status == ShipmentStatus.DeliveryFailed),
                g.Count(s => s.EtaAt != null && s.EtaAt < now && !closed.Contains(s.Status) && !returning.Contains(s.Status)),
                g.Count(s => returning.Contains(s.Status)),
                g.Count(s => s.DeliveredAt >= startOfDay)))
            .ToListAsync(ct);

        var atHubs = await db.Shipments.AsNoTracking()
            .Where(s => s.CurrentHubCode != null && (warehouse.Contains(s.Status) || returning.Contains(s.Status)))
            .GroupBy(s => s.CurrentHubCode!)
            .Select(g => new
            {
                Hub = g.Key,
                OnShelves = g.Count(),
                ToDispatch = g.Count(s => warehouse.Contains(s.Status) && s.DestinationHubCode == s.CurrentHubCode),
                ToTransfer = g.Count(s => (warehouse.Contains(s.Status) || s.Status == ShipmentStatus.ReturnInitiated)
                    && (returning.Contains(s.Status) ? (s.OriginHubCode ?? s.DestinationHubCode) : s.DestinationHubCode) != s.CurrentHubCode),
                Returns = g.Count(s => s.Status == ShipmentStatus.ReturnAtHub),
            })
            .ToListAsync(ct);

        var trucks = await db.Transfers.AsNoTracking()
            .Where(t => t.Status != TransferStatus.Received)
            .Select(t => new { t.FromHubCode, t.ToHubCode, t.Status, t.ExpectedAt, Parcels = t.Items.Count(i => i.State != TransferItemState.Missing) })
            .ToListAsync(ct);
        var grace = (await policies.GetAsync(tenant.TenantId, ct)).LineHaulGraceHours;

        var scans = await db.HubScans.AsNoTracking().Where(s => s.ScannedAt >= startOfDay)
            .GroupBy(s => s.HubCode).Select(g => new { Hub = g.Key, Count = g.Count() }).ToListAsync(ct);
        var alerts = await db.Alerts.AsNoTracking().Where(a => a.ResolvedAt == null && a.HubCode != null)
            .GroupBy(a => a.HubCode!).Select(g => new { Hub = g.Key, Count = g.Count() }).ToListAsync(ct);
        var openAlerts = await db.Alerts.AsNoTracking().CountAsync(a => a.ResolvedAt == null, ct);

        var hubs = (await directory.NetworkAsync(tenant.TenantId, ct)).All.Select(h =>
        {
            var load = atHubs.FirstOrDefault(a => a.Hub == h.Code);
            return new HubLoad(
                h.Code,
                load?.OnShelves ?? 0,
                load?.ToDispatch ?? 0,
                load?.ToTransfer ?? 0,
                load?.Returns ?? 0,
                trucks.Where(t => t.ToHubCode == h.Code && t.Status == TransferStatus.InTransit).Sum(t => t.Parcels),
                scans.FirstOrDefault(s => s.Hub == h.Code)?.Count ?? 0,
                alerts.FirstOrDefault(a => a.Hub == h.Code)?.Count ?? 0);
        }).ToList();

        var lanes = trucks
            .GroupBy(t => (t.FromHubCode, t.ToHubCode))
            .Select(g => new LaneLoad(g.Key.FromHubCode, g.Key.ToHubCode,
                g.Count(t => t.Status == TransferStatus.InTransit),
                g.Where(t => t.Status == TransferStatus.InTransit).Sum(t => t.Parcels),
                g.Where(t => t.Status == TransferStatus.Loading).Sum(t => t.Parcels),
                g.Any(t => t.Status == TransferStatus.InTransit && t.ExpectedAt < now.AddHours(-grace))))
            .ToList();

        var known = governorates.Where(g => IraqGovernorates.IsKnown(g.Code)).ToList();
        return TypedResults.Ok(new NetworkOverview(
            known,
            hubs,
            lanes,
            known.Sum(g => g.Active),
            lanes.Sum(l => l.Parcels),
            known.Sum(g => g.Late),
            openAlerts,
            now));
    }
}
