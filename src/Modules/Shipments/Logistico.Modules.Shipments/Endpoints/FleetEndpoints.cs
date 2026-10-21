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
                Cash = g.Sum(s => s.CodAmount != null && (s.Status == ShipmentStatus.OutForDelivery
                    || (s.Status == ShipmentStatus.Delivered && s.DeliveredAt >= startOfDay && s.CodSettledAt == null)) ? s.CodAmount.Value : 0m),
            })
            .ToListAsync(ct);

        var items = couriers.Select(c =>
        {
            var mine = stats.Where(s => Matches(s.Name, c)).ToList();
            return new CourierListItem(c.Id, c.Code, c.Name, c.Phone, c.Governorate, c.VehicleType, c.VehiclePlate, c.IsActive, c.Notes,
                mine.Sum(s => s.Out), mine.Sum(s => s.DeliveredToday), mine.Sum(s => s.Delivered), mine.Sum(s => s.Failed), mine.Sum(s => s.Cash), c.Version);
        }).ToList();

        var totals = await db.Shipments.AsNoTracking().GroupBy(_ => 1).Select(g => new
        {
            Out = g.Count(s => s.Status == ShipmentStatus.OutForDelivery),
            DeliveredToday = g.Count(s => s.Status == ShipmentStatus.Delivered && s.DeliveredAt >= startOfDay),
            Failed = g.Count(s => s.Status == ShipmentStatus.DeliveryFailed),
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
            var term = q.Trim();
            filtered = filtered.Where(c => c.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || c.Code.Contains(term, StringComparison.OrdinalIgnoreCase)
                || c.Phone.Contains(term, StringComparison.OrdinalIgnoreCase) || (c.VehiclePlate?.Contains(term, StringComparison.OrdinalIgnoreCase) == true));
        }

        return TypedResults.Ok(new FleetOverviewResult(items.Count(c => c.IsActive), totals?.Out ?? 0, totals?.DeliveredToday ?? 0, totals?.Failed ?? 0,
            items.Sum(c => c.CashOnRunIqd), filtered.ToList()));
    }

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
