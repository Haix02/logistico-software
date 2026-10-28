using System.Security.Claims;
using Logistico.Modules.Shipments.Application;
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

/// <summary>
/// Shelf checks (cycle counts): count what is really on a hub shelf, or in the whole hub, against what the system says.
/// Fixing what the count finds (a parcel on the wrong shelf, one the system has elsewhere) is an ordinary receive scan.
/// </summary>
internal static class ShelfCheckEndpoints
{
    /// <summary>Lines of each kind returned with a check: enough to work through, never a whole hub at once.</summary>
    private const int LinesPerState = 200;

    public static IEndpointRouteBuilder MapShelfCheckEndpoints(this IEndpointRouteBuilder app)
    {
        var checks = app.MapGroup("/" + ShipmentsApiRoutes.ShelfChecks)
            .WithTags("Shelf checks")
            .RequireAuthorization(StaffPolicies.Staff)
            .WithValidation();

        checks.MapGet("/", ListAsync).WithSummary("Recent shelf checks, newest first, optionally at one hub.");
        checks.MapPost("/", StartAsync)
            .WithSummary("Starts counting a shelf (or the whole hub); an unfinished count of the same shelf is joined instead.")
            .RequireAuthorization(StaffPolicies.Operate);
        checks.MapGet("/{id:guid}", GetAsync).WithSummary("A shelf check with its numbers and the parcels that need attention.");
        checks.MapPost("/{id:guid}/scan", ScanAsync)
            .WithSummary("One parcel found on the shelf: on the shelf, on the wrong shelf, or not expected here.")
            .RequireAuthorization(StaffPolicies.Operate);
        checks.MapPost("/{id:guid}/finish", FinishAsync)
            .WithSummary("Ends the count: every expected parcel not scanned is missing.")
            .RequireAuthorization(StaffPolicies.Operate);
        return app;
    }

    private static async Task<Ok<PagedResult<ShelfCheckSummary>>> ListAsync(string? hub, int? page, int? pageSize, ShipmentsDbContext db, CancellationToken ct)
    {
        var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var number = Paging.Page(page);
        var query = db.ShelfChecks.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(hub))
        {
            query = query.Where(c => c.HubCode == hub);
        }

        var total = await query.CountAsync(ct);
        var checks = await query.OrderByDescending(c => c.StartedAt).Skip((number - 1) * size).Take(size).ToListAsync(ct);
        var ids = checks.Select(c => c.Id).ToList();
        var counts = await db.ShelfCheckLines.AsNoTracking()
            .Where(l => ids.Contains(l.ShelfCheckId))
            .GroupBy(l => new { l.ShelfCheckId, l.State })
            .Select(g => new { g.Key.ShelfCheckId, g.Key.State, Count = g.Count() })
            .ToListAsync(ct);
        var items = checks.Select(c => Summary(c, counts.Where(x => x.ShelfCheckId == c.Id).ToDictionary(x => x.State, x => x.Count))).ToList();
        return TypedResults.Ok(new PagedResult<ShelfCheckSummary>(items, total, number, size));
    }

    private static async Task<IResult> StartAsync(
        StartShelfCheckRequest request, ClaimsPrincipal user, ShipmentsDbContext db, HubDirectory hubs, NumberGenerator numbers, ITenantContext tenant,
        TimeProvider clock, CancellationToken ct)
    {
        if ((await hubs.NetworkAsync(tenant.TenantId, ct)).Find(request.HubCode) is not { } hub)
        {
            return Api.Rule(new DomainException("scan.unknown_hub", "Unknown hub.", request.HubCode));
        }

        var bin = string.IsNullOrWhiteSpace(request.Bin) ? null : request.Bin.Trim().ToUpperInvariant();
        if (await db.ShelfChecks.AsNoTracking().FirstOrDefaultAsync(c => c.HubCode == hub.Code && c.Bin == bin && !c.Finished, ct) is { } open)
        {
            return TypedResults.Ok(await DetailsAsync(db, open, ct)); // someone is counting this shelf already: count together
        }

        var expected = await db.Shipments.AsNoTracking()
            .Where(s => s.CurrentHubCode == hub.Code && ShelfCheck.OnShelf.Contains(s.Status) && (bin == null || s.WarehouseBin == bin))
            .ToListAsync(ct);
        var check = ShelfCheck.Start(tenant.TenantId, await numbers.ShelfCheckNumberAsync(ct), hub.Code, bin, expected, user.UserName(), clock.GetUtcNow());
        db.ShelfChecks.Add(check);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created("/" + ShipmentsApiRoutes.ShelfCheck(check.Id), await DetailsAsync(db, check, ct));
    }

    private static async Task<IResult> GetAsync(Guid id, ShipmentsDbContext db, CancellationToken ct) =>
        await db.ShelfChecks.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) is { } check
            ? TypedResults.Ok(await DetailsAsync(db, check, ct))
            : Api.NotFound("shelf_check.not_found");

    private static async Task<IResult> ScanAsync(Guid id, ShelfCheckScanRequest request, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (await db.ShelfChecks.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) is not { } check)
        {
            return Api.NotFound("shelf_check.not_found");
        }

        var number = request.TrackingNumber.Trim().ToUpperInvariant();
        var line = await db.ShelfCheckLines.FirstOrDefaultAsync(l => l.ShelfCheckId == id && l.TrackingNumber == number, ct);
        var shipment = line is null ? await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.TrackingNumber == number, ct) : null;
        try
        {
            var scanned = check.Scan(line, number, shipment, clock.GetUtcNow());
            if (line is null)
            {
                db.ShelfCheckLines.Add(scanned);
            }

            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(new ShelfCheckScanResult(ToItem(scanned), await SummaryAsync(db, check, ct)));
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateException) when (line is null)
        {
            // The same stray parcel scanned twice at the same moment: the other scan recorded it.
            db.ChangeTracker.Clear();
            var recorded = await db.ShelfCheckLines.AsNoTracking().FirstAsync(l => l.ShelfCheckId == id && l.TrackingNumber == number, ct);
            return TypedResults.Ok(new ShelfCheckScanResult(ToItem(recorded), await SummaryAsync(db, check, ct)));
        }
    }

    private static async Task<IResult> FinishAsync(Guid id, ClaimsPrincipal user, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (await db.ShelfChecks.FirstOrDefaultAsync(c => c.Id == id, ct) is not { } check)
        {
            return Api.NotFound("shelf_check.not_found");
        }

        var pending = await db.ShelfCheckLines.Where(l => l.ShelfCheckId == id && l.State == ShelfCheckState.Expected).ToListAsync(ct);
        check.Finish(pending, user.UserName(), clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Api.Stale(); // finished by someone else at the same moment
        }

        return TypedResults.Ok(await DetailsAsync(db, check, ct));
    }

    private static async Task<ShelfCheckDetails> DetailsAsync(ShipmentsDbContext db, ShelfCheck check, CancellationToken ct)
    {
        var lines = db.ShelfCheckLines.AsNoTracking().Where(l => l.ShelfCheckId == check.Id);
        var attention = await lines
            .Where(l => l.State == ShelfCheckState.Missing || l.State == ShelfCheckState.WrongShelf || l.State == ShelfCheckState.NotExpected)
            .OrderBy(l => l.State).ThenBy(l => l.SystemBin).ThenBy(l => l.TrackingNumber)
            .Take(LinesPerState * 3)
            .ToListAsync(ct);
        var found = await lines.Where(l => l.State == ShelfCheckState.Found).OrderByDescending(l => l.ScannedAt).Take(LinesPerState).ToListAsync(ct);
        var pending = await lines.Where(l => l.State == ShelfCheckState.Expected).OrderBy(l => l.SystemBin).ThenBy(l => l.TrackingNumber).Take(LinesPerState).ToListAsync(ct);
        return new ShelfCheckDetails(await SummaryAsync(db, check, ct), [.. attention.Concat(found).Concat(pending).Select(ToItem)]);
    }

    private static async Task<ShelfCheckSummary> SummaryAsync(ShipmentsDbContext db, ShelfCheck check, CancellationToken ct)
    {
        var counts = await db.ShelfCheckLines.AsNoTracking()
            .Where(l => l.ShelfCheckId == check.Id)
            .GroupBy(l => l.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.State, x => x.Count, ct);
        return Summary(check, counts);
    }

    private static ShelfCheckSummary Summary(ShelfCheck c, Dictionary<ShelfCheckState, int> counts)
    {
        int N(ShelfCheckState state) => counts.GetValueOrDefault(state);
        return new ShelfCheckSummary(
            c.Id, c.Number, c.HubCode, c.Bin, c.Finished, c.StartedAt, c.StartedBy, c.FinishedAt,
            N(ShelfCheckState.Expected) + N(ShelfCheckState.Found) + N(ShelfCheckState.Missing),
            N(ShelfCheckState.Found), N(ShelfCheckState.Missing), N(ShelfCheckState.WrongShelf), N(ShelfCheckState.NotExpected));
    }

    private static ShelfCheckLineItem ToItem(ShelfCheckLine l) =>
        new(l.ShipmentId, l.TrackingNumber, l.State, l.SystemHubCode, l.SystemBin, l.SystemStatus, l.ScannedAt);
}
