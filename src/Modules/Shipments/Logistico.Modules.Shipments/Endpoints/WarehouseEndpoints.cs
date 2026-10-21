using System.Security.Claims;
using Logistico.Modules.Shipments.Application;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Endpoints;

internal static class WarehouseEndpoints
{
    private const int RadarSize = 60;

    public static IEndpointRouteBuilder MapWarehouseEndpoints(this IEndpointRouteBuilder app)
    {
        var warehouse = app.MapGroup("/" + ShipmentsApiRoutes.Warehouse)
            .WithTags("Hub stations")
            .RequireAuthorization(StaffPolicies.Staff)
            .WithValidation();

        warehouse.MapPost("/scan", ScanAsync)
            .WithSummary("One scan at a hub: receive (also completes truck arrivals), dispatch to courier, load a truck, verify routing, deliver or fail.")
            .RequireAuthorization(StaffPolicies.Operate);
        warehouse.MapPost("/bulk", BulkAsync)
            .WithSummary("The same operation for up to 500 parcels (e.g. a courier's run sheet or a pallet).")
            .RequireAuthorization(StaffPolicies.Operate);
        warehouse.MapGet("/radar", RadarAsync)
            .WithSummary("Live hub radar: latest scans across all hubs and today's counters. Pass afterSeq to poll cheaply.");
        return app;
    }

    private static async Task<IResult> ScanAsync(ScanRequest request, ClaimsPrincipal user, WarehouseScanner scanner, CancellationToken ct)
    {
        try
        {
            var outcome = (await scanner.ScanAsync(request.Operation, [request.TrackingNumber], request.HubCode, request.WarehouseBin, request.CourierName,
                request.TransferId, request.FailureReason, user.UserName(), ct)).Single();
            if (outcome.Ok)
            {
                return TypedResults.Ok(outcome);
            }

            var status = outcome.ProblemCode == "shipment.tracking_not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status422UnprocessableEntity;
            return TypedResults.Problem(statusCode: status, title: outcome.Message,
                extensions: new Dictionary<string, object?> { ["code"] = outcome.ProblemCode, ["scan"] = outcome });
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.IsUniqueViolation())
        {
            // A parcel or truck changed at the same moment (another station); scanning again sees the new state.
            return Api.Stale();
        }
    }

    private static async Task<IResult> BulkAsync(BulkScanRequest request, ClaimsPrincipal user, WarehouseScanner scanner, CancellationToken ct)
    {
        try
        {
            var outcomes = await scanner.ScanAsync(request.Operation, request.TrackingNumbers, request.HubCode, request.WarehouseBin, request.CourierName,
                request.TransferId, request.FailureReason, user.UserName(), ct);
            return TypedResults.Ok(new BulkScanResult(outcomes.Count(o => o.Ok), outcomes.Count(o => !o.Ok), outcomes));
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.IsUniqueViolation())
        {
            // A parcel or truck changed at the same moment (another station); scanning again sees the new state.
            return Api.Stale();
        }
    }

    private static async Task<Ok<HubRadar>> RadarAsync(long? afterSeq, string? hub, ShipmentsDbContext db, HubDirectory directory, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var startOfDay = Api.StartOfBaghdadDay(clock.GetUtcNow());
        var scans = db.HubScans.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(hub))
        {
            scans = scans.Where(s => s.HubCode == hub);
        }

        var lastSeq = await scans.MaxAsync(s => (long?)s.Id, ct) ?? 0;
        var perHub = await db.HubScans.AsNoTracking()
            .Where(s => s.ScannedAt >= startOfDay)
            .GroupBy(s => s.HubCode)
            .Select(g => new HubActivity(g.Key, g.Count(), g.Count(s => s.IsMisrouted), g.Max(s => (DateTimeOffset?)s.ScannedAt)))
            .ToListAsync(ct);

        var recentQuery = scans.OrderByDescending(s => s.Id).AsQueryable();
        if (afterSeq is { } after)
        {
            recentQuery = recentQuery.Where(s => s.Id > after);
        }

        var recent = await recentQuery.Take(RadarSize).ToListAsync(ct);
        var ids = recent.Where(r => r.ShipmentId != null).Select(r => r.ShipmentId!.Value).Distinct().ToList();
        var parcels = await db.Shipments.AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.ConsigneeName, s.DestinationCode, s.CodAmount, s.CodCurrency })
            .ToDictionaryAsync(s => s.Id, ct);

        var items = recent.Select(scan =>
        {
            var p = scan.ShipmentId is { } sid && parcels.TryGetValue(sid, out var found) ? found : null;
            return new ScanOutcome(scan.Id, scan.Ok, scan.Operation, scan.TrackingNumber, scan.ShipmentId, scan.Status, scan.HubCode, scan.WarehouseBin,
                scan.IsMisrouted, scan.ExpectedHubCode, null, scan.TransferNumber, p?.ConsigneeName, p?.DestinationCode, scan.CourierName,
                p?.CodAmount, p?.CodCurrency, scan.Message, scan.ProblemCode, scan.ScannedBy, scan.ScannedAt);
        }).ToList();

        var hubs = (await directory.NetworkAsync(tenant.TenantId, ct)).All
            .Select(h => perHub.FirstOrDefault(a => a.HubCode == h.Code) ?? new HubActivity(h.Code, 0, 0, null))
            .ToList();
        return TypedResults.Ok(new HubRadar(lastSeq, hubs.Sum(h => h.ScansToday), hubs.Sum(h => h.MisroutedToday), hubs, items));
    }
}
