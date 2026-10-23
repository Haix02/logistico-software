using System.Security.Claims;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Endpoints;

internal static class FleetEndpoints
{
    public static IEndpointRouteBuilder MapFleetEndpoints(this IEndpointRouteBuilder app)
    {
        var fleet = app.MapGroup("/" + ShipmentsApiRoutes.Fleet)
            .WithTags("Fleet & couriers")
            .RequireAuthorization(StaffPolicies.Staff)
            .WithValidation();

        fleet.MapGet("/", OverviewAsync).WithSummary("Couriers with live workload and COD cash on their runs (aggregated in SQL).");
        fleet.MapGet("/lookup", LookupAsync).WithSummary("Active couriers for dispatch pick-lists.");
        fleet.MapPost("/couriers", CreateAsync).WithSummary("Registers a courier and vehicle.")
            .RequireAuthorization(StaffPolicies.Operate);
        fleet.MapPut("/couriers/{id:guid}", UpdateAsync).WithSummary("Updates a courier. Requires If-Match with the current ETag.")
            .RequireAuthorization(StaffPolicies.Operate);
        fleet.MapGet("/couriers/{id:guid}/cash", CashAsync).WithSummary("The cash a courier collected on delivery and has not handed in yet, parcel by parcel.");
        fleet.MapPost("/couriers/{id:guid}/cash", ReceiveCashAsync).WithSummary("Records a courier's cash hand-in: every collected parcel not handed in yet, with the amount counted.")
            .RequireAuthorization(StaffPolicies.Operate);
        fleet.MapGet("/cash-handovers", HandoversAsync).WithSummary("Cash hand-ins, newest first, with any shortage.");
        return app;
    }

    private static async Task<Ok<FleetOverviewResult>> OverviewAsync(string? q, string? governorate, bool? activeOnly, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var couriers = await db.Couriers.AsNoTracking().OrderByDescending(c => c.IsActive).ThenBy(c => c.Name).ToListAsync(ct);
        var startOfDay = Api.StartOfBaghdadDay(clock.GetUtcNow());

        // One grouped query per courier name instead of loading every delivery into memory.
        var stats = await db.Shipments.AsNoTracking()
            .Where(s => s.CourierName != null && s.Status != ShipmentStatus.Cancelled)
            .GroupBy(s => s.CourierName!)
            .Select(g => new
            {
                Name = g.Key,
                Out = g.Count(s => s.Status == ShipmentStatus.OutForDelivery),
                DeliveredToday = g.Count(s => s.Status == ShipmentStatus.Delivered && s.DeliveredAt >= startOfDay),
                Delivered = g.Count(s => s.Status == ShipmentStatus.Delivered),
                Failed = g.Count(s => s.Status == ShipmentStatus.DeliveryFailed),
                // Cash still to collect on today's run, and cash collected but not handed in yet (whatever the day).
                Cash = g.Sum(s => s.CodAmount != null && s.Status == ShipmentStatus.OutForDelivery ? s.CodAmount.Value : 0m),
                ToHandIn = g.Sum(s => s.CodCollectedAt != null && s.CodHandedOverAt == null ? s.CodAmount ?? 0m : 0m),
                ParcelsToHandIn = g.Count(s => s.CodCollectedAt != null && s.CodHandedOverAt == null),
            })
            .ToListAsync(ct);

        var items = couriers.Select(c =>
        {
            var mine = stats.Where(s => Matches(s.Name, c)).ToList();
            return new CourierListItem(c.Id, c.Code, c.Name, c.Phone, c.Governorate, c.VehicleType, c.VehiclePlate, c.IsActive, c.Notes,
                mine.Sum(s => s.Out), mine.Sum(s => s.DeliveredToday), mine.Sum(s => s.Delivered), mine.Sum(s => s.Failed), mine.Sum(s => s.Cash), c.Version,
                mine.Sum(s => s.ToHandIn), mine.Sum(s => s.ParcelsToHandIn));
        }).ToList();

        var totals = await db.Shipments.AsNoTracking().GroupBy(_ => 1).Select(g => new
        {
            Out = g.Count(s => s.Status == ShipmentStatus.OutForDelivery),
            DeliveredToday = g.Count(s => s.Status == ShipmentStatus.Delivered && s.DeliveredAt >= startOfDay),
            Failed = g.Count(s => s.Status == ShipmentStatus.DeliveryFailed),
            WithCouriers = g.Sum(s => s.CodCollectedAt != null && s.CodHandedOverAt == null ? s.CodAmount ?? 0m : 0m),
        }).SingleOrDefaultAsync(ct);

        IEnumerable<CourierListItem> filtered = items;
        if (activeOnly == true)
        {
            filtered = filtered.Where(c => c.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(governorate))
        {
            filtered = filtered.Where(c => string.Equals(c.Governorate, governorate.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            filtered = filtered.Where(c => SearchText.Matches(q, c.Name, c.Code, c.Phone, c.VehiclePlate));
        }

        return TypedResults.Ok(new FleetOverviewResult(items.Count(c => c.IsActive), totals?.Out ?? 0, totals?.DeliveredToday ?? 0, totals?.Failed ?? 0,
            totals?.WithCouriers ?? 0m, filtered.ToList()));
    }

    private static async Task<IResult> CashAsync(Guid id, ShipmentsDbContext db, CancellationToken ct)
    {
        if (await db.Couriers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) is not { } courier)
        {
            return Api.NotFound("courier.not_found");
        }

        var label = courier.DisplayLabel;
        var parcels = await WithCourier(db.Shipments.AsNoTracking(), label)
            .OrderBy(s => s.DeliveredAt)
            .Select(s => new CourierCashParcel(s.Id, s.TrackingNumber, s.ConsigneeName, s.MerchantName, s.CodAmount ?? 0m, s.DeliveredAt))
            .ToListAsync(ct);
        return TypedResults.Ok(new CourierCash(courier.Id, courier.Code, courier.Name, parcels.Sum(p => p.CodAmount), parcels));
    }

    private static async Task<IResult> ReceiveCashAsync(
        Guid id, ReceiveCashRequest request, ClaimsPrincipal user, ShipmentsDbContext db, NumberGenerator numbers, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        if (await db.Couriers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) is not { } courier)
        {
            return Api.NotFound("courier.not_found");
        }

        var parcels = await WithCourier(db.Shipments, courier.DisplayLabel).OrderBy(s => s.DeliveredAt).ToListAsync(ct);
        try
        {
            var handover = CashHandover.Create(tenant.TenantId, await numbers.CashHandoverNumberAsync(ct), courier, parcels, request.ReceivedIqd!.Value, request.Notes,
                user.UserName(), clock.GetUtcNow());
            db.CashHandovers.Add(handover);
            await db.SaveChangesAsync(ct);
            return TypedResults.Created("/" + ShipmentsApiRoutes.CashHandovers, ToItem(handover));
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else received this courier's cash at the same moment: nothing is counted twice.
            return Api.Stale();
        }
    }

    private static async Task<Ok<PagedResult<CashHandoverItem>>> HandoversAsync(Guid? courierId, int? page, int? pageSize, ShipmentsDbContext db, CancellationToken ct)
    {
        var size = Math.Clamp(pageSize ?? 25, 1, 200);
        var number = Paging.Page(page);
        var query = db.CashHandovers.AsNoTracking();
        if (courierId is { } courier)
        {
            query = query.Where(h => h.CourierId == courier);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(h => h.ReceivedAt).Skip((number - 1) * size).Take(size).ToListAsync(ct);
        return TypedResults.Ok(new PagedResult<CashHandoverItem>(items.Select(ToItem).ToList(), total, number, size));
    }

    /// <summary>Parcels whose cash on delivery this courier collected and has not handed in yet.</summary>
    private static IQueryable<Shipment> WithCourier(IQueryable<Shipment> shipments, string label) =>
        shipments.Where(s => s.CourierName == label && s.CodCollectedAt != null && s.CodHandedOverAt == null && s.CodAmount > 0);

    private static CashHandoverItem ToItem(CashHandover h) =>
        new(h.Id, h.Number, h.CourierId, h.CourierLabel, h.ParcelCount, h.ExpectedIqd, h.ReceivedIqd, h.DifferenceIqd, h.ReceivedAt, h.ReceivedBy, h.Notes);

    /// <summary>Courier names on deliveries are stored as "Name (CODE)"; older rows may hold the name or code alone.</summary>
    private static bool Matches(string recorded, Courier c)
    {
        var text = recorded.Trim();
        return string.Equals(text, c.DisplayLabel, StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, c.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, c.Code, StringComparison.OrdinalIgnoreCase)
            || text.StartsWith(c.Name + " (", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Ok<List<CourierLookupItem>>> LookupAsync(ShipmentsDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Couriers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new CourierLookupItem(c.Id, c.Code, c.Name, c.Governorate, c.VehicleType))
            .ToListAsync(ct));

    private static async Task<IResult> CreateAsync(CourierRequest request, ShipmentsDbContext db, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var code = Courier.NormalizeCode(request.Code);
        if (await db.Couriers.AnyAsync(c => c.Code.ToLower() == code.ToLower(), ct))
        {
            return Api.Conflict("courier.code_taken", code);
        }

        try
        {
            var courier = Courier.Create(tenant.TenantId, request, clock.GetUtcNow());
            db.Couriers.Add(courier);
            await db.SaveChangesAsync(ct);
            return TypedResults.Created("/" + ShipmentsApiRoutes.CourierById(courier.Id), ToItem(courier));
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
    }

    private static async Task<IResult> UpdateAsync(Guid id, CourierRequest request, HttpRequest http, HttpResponse response, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Api.TryReadIfMatch(http, out var expected))
        {
            return Api.IfMatchRequired();
        }

        var courier = await db.Couriers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (courier is null)
        {
            return Api.NotFound("courier.not_found");
        }

        if (courier.Version != expected)
        {
            return Api.Stale("courier.stale");
        }

        var code = Courier.NormalizeCode(request.Code);
        if (await db.Couriers.AnyAsync(c => c.Id != id && c.Code.ToLower() == code.ToLower(), ct))
        {
            return Api.Conflict("courier.code_taken", code);
        }

        try
        {
            courier.Update(request, clock.GetUtcNow());
            db.Entry(courier).Property(c => c.Version).OriginalValue = expected;
            await db.SaveChangesAsync(ct);
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Api.Stale("courier.stale");
        }

        response.Headers.ETag = Api.ETag(courier.Version);
        return TypedResults.Ok(ToItem(courier));
    }

    private static CourierListItem ToItem(Courier c) =>
        new(c.Id, c.Code, c.Name, c.Phone, c.Governorate, c.VehicleType, c.VehiclePlate, c.IsActive, c.Notes, 0, 0, 0, 0, 0m, c.Version);
}
