using System.Security.Claims;
using Logistico.Modules.Shipments.Application;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
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

/// <summary>The company's warehouses (hubs, branches, fulfilment stores) and the network the browser routes with.</summary>
internal static class WarehousesEndpoints
{
    public static IEndpointRouteBuilder MapWarehousesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/" + ShipmentsApiRoutes.NetworkHubs, HubsAsync)
            .WithTags("Warehouses")
            .WithSummary("Active hubs with the governorates they cover, for routing and display in the browser.")
            .RequireAuthorization(StaffPolicies.Staff);

        var group = app.MapGroup("/" + ShipmentsApiRoutes.Warehouses)
            .WithTags("Warehouses")
            .RequireAuthorization(StaffPolicies.Staff)
            .WithValidation();
        group.MapGet("/", ListAsync).WithSummary("All warehouses, active first, with today's workload and the governorates nobody covers.");
        group.MapGet("/{id:guid}", GetAsync).WithSummary("One warehouse. Returns an ETag for editing.");
        group.MapPost("/", CreateAsync).WithSummary("Adds a warehouse. Its routing tag is permanent.")
            .RequireAuthorization(StaffPolicies.Administer);
        group.MapPut("/{id:guid}", UpdateAsync).WithSummary("Edits, moves coverage, makes main or retires a warehouse. Requires If-Match.")
            .RequireAuthorization(StaffPolicies.Administer);
        return app;
    }

    private static async Task<Ok<NetworkHubs>> HubsAsync(HubDirectory directory, ShipmentsDbContext db, ITenantContext tenant, CancellationToken ct)
    {
        var network = await directory.NetworkAsync(tenant.TenantId, ct);
        var updated = await db.Warehouses.AsNoTracking().MaxAsync(w => (DateTimeOffset?)w.UpdatedAt, ct) ?? DateTimeOffset.MinValue;
        return TypedResults.Ok(new NetworkHubs(network.All, updated));
    }

    private static async Task<Ok<WarehouseList>> ListAsync(ShipmentsDbContext db, HubDirectory directory, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        await directory.NetworkAsync(tenant.TenantId, ct); // creates the standard network on first use
        var warehouses = await db.Warehouses.AsNoTracking()
            .OrderByDescending(w => w.IsActive).ThenByDescending(w => w.IsPrimary).ThenBy(w => w.CreatedAt)
            .ToListAsync(ct);
        var loads = await LoadsAsync(db, warehouses, clock.GetUtcNow(), ct);
        var covered = warehouses.Where(w => w.IsActive).SelectMany(w => w.Covers).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var uncovered = IraqGovernorates.All.Select(g => g.Code).Where(c => !covered.Contains(c)).ToList();
        return TypedResults.Ok(new WarehouseList(warehouses.Select(w => ToDetails(w, loads[w.Code])).ToList(), uncovered));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpResponse response, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var warehouse = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
        if (warehouse is null)
        {
            return Api.NotFound("warehouse.not_found");
        }

        var loads = await LoadsAsync(db, [warehouse], clock.GetUtcNow(), ct);
        response.Headers.ETag = Api.ETag(warehouse.Version);
        return TypedResults.Ok(ToDetails(warehouse, loads[warehouse.Code]));
    }

    private static async Task<IResult> CreateAsync(WarehouseRequest request, ClaimsPrincipal user, HttpResponse response, ShipmentsDbContext db, WarehouseAdmin admin, TimeProvider clock, CancellationToken ct)
    {
        try
        {
            var warehouse = await admin.CreateAsync(request, user.UserName(), ct);
            var loads = await LoadsAsync(db, [warehouse], clock.GetUtcNow(), ct);
            response.Headers.ETag = Api.ETag(warehouse.Version);
            return TypedResults.Created("/" + ShipmentsApiRoutes.WarehouseById(warehouse.Id), ToDetails(warehouse, loads[warehouse.Code]));
        }
        catch (DomainException ex) when (ex.Code == "warehouse.tag_taken")
        {
            return Api.Conflict(ex.Code, ex.Args);
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        WarehouseRequest request,
        HttpRequest http,
        HttpResponse response,
        ClaimsPrincipal user,
        ShipmentsDbContext db,
        WarehouseAdmin admin,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!Api.TryReadIfMatch(http, out var expected))
        {
            return Api.IfMatchRequired();
        }

        var warehouse = await db.Warehouses.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (warehouse is null)
        {
            return Api.NotFound("warehouse.not_found");
        }

        if (warehouse.Version != expected)
        {
            return Api.Stale("warehouse.stale");
        }

        try
        {
            db.Entry(warehouse).Property(w => w.Version).OriginalValue = expected;
            await admin.UpdateAsync(warehouse, request, user.UserName(), ct);
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Api.Stale("warehouse.stale");
        }

        var loads = await LoadsAsync(db, [warehouse], clock.GetUtcNow(), ct);
        response.Headers.ETag = Api.ETag(warehouse.Version);
        return TypedResults.Ok(ToDetails(warehouse, loads[warehouse.Code]));
    }

    /// <summary>Today's workload per warehouse in a few grouped queries (no per-warehouse round trips).</summary>
    private static async Task<Dictionary<string, WarehouseLoad>> LoadsAsync(ShipmentsDbContext db, IReadOnlyList<Warehouse> warehouses, DateTimeOffset now, CancellationToken ct)
    {
        var codes = warehouses.Select(w => w.Code).ToList();
        var startOfDay = Api.StartOfBaghdadDay(now);
        var warehouse = ShipmentLifecycle.WarehouseStatuses;
        var returning = ShipmentLifecycle.ReturnStatuses;

        var atHubs = await db.Shipments.AsNoTracking()
            .Where(s => s.CurrentHubCode != null && codes.Contains(s.CurrentHubCode) && (warehouse.Contains(s.Status) || returning.Contains(s.Status)))
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
            .ToDictionaryAsync(x => x.Hub, ct);

        var trucks = await db.Transfers.AsNoTracking()
            .Where(t => t.Status != TransferStatus.Received && (codes.Contains(t.FromHubCode) || codes.Contains(t.ToHubCode)))
            .Select(t => new { t.FromHubCode, t.ToHubCode, t.Status, Parcels = t.Items.Count(i => i.State != TransferItemState.Missing) })
            .ToListAsync(ct);
        var scans = await db.HubScans.AsNoTracking().Where(s => s.ScannedAt >= startOfDay && codes.Contains(s.HubCode))
            .GroupBy(s => s.HubCode).Select(g => new { Hub = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Hub, x => x.Count, ct);
        var alerts = await db.Alerts.AsNoTracking().Where(a => a.ResolvedAt == null && a.HubCode != null && codes.Contains(a.HubCode))
            .GroupBy(a => a.HubCode!).Select(g => new { Hub = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Hub, x => x.Count, ct);
        var delivered = await db.Shipments.AsNoTracking().Where(s => s.DeliveredAt >= startOfDay)
            .GroupBy(s => s.DestinationHubCode).Select(g => new { Hub = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Hub, x => x.Count, ct);
        var couriers = await db.Couriers.AsNoTracking().Where(c => c.IsActive)
            .GroupBy(c => c.Governorate).Select(g => new { Governorate = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Governorate, x => x.Count, ct);

        return warehouses.ToDictionary(w => w.Code, w =>
        {
            var load = atHubs.GetValueOrDefault(w.Code);
            return new WarehouseLoad(
                load?.OnShelves ?? 0,
                load?.ToDispatch ?? 0,
                load?.ToTransfer ?? 0,
                load?.Returns ?? 0,
                trucks.Where(t => t.ToHubCode == w.Code && t.Status == TransferStatus.InTransit).Sum(t => t.Parcels),
                trucks.Count(t => t.FromHubCode == w.Code || t.ToHubCode == w.Code),
                scans.GetValueOrDefault(w.Code),
                alerts.GetValueOrDefault(w.Code),
                delivered.GetValueOrDefault(w.Code),
                w.Covers.Sum(c => couriers.GetValueOrDefault(c)));
        }, StringComparer.OrdinalIgnoreCase);
    }

    private static WarehouseDetails ToDetails(Warehouse w, WarehouseLoad load) => new(
        w.Id, w.Code, w.RoutingTag, w.NameAr, w.NameEn, w.Kind, w.Governorate, w.Address, w.Latitude, w.Longitude, w.Phone, w.ManagerName,
        w.OpeningHours, w.ShelfCapacity, w.Covers, w.IsActive, w.IsPrimary, w.Notes, w.UpdatedAt, w.UpdatedBy, load, w.Version);
}
