using System.Security.Claims;
using Logistico.Modules.Shipments.Application;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
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

internal static class TransferEndpoints
{
    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var transfers = app.MapGroup("/" + ShipmentsApiRoutes.Transfers)
            .WithTags("Line-haul transfers")
            .RequireAuthorization(StaffPolicies.Staff)
            .WithValidation();

        transfers.MapGet("/", ListAsync)
            .WithSummary("Trucks between hubs with manifest counts; filter by status, hub or overdue.");
        transfers.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Truck manifest with every parcel and its state (planned, loaded, received, missing).");
        transfers.MapPost("/", OpenAsync)
            .WithSummary("Opens a truck for a lane and (by default) plans every parcel already waiting for it.")
            .RequireAuthorization(StaffPolicies.Operate);
        transfers.MapPost("/{id:guid}/depart", DepartAsync)
            .WithSummary("The truck leaves: scanned parcels go between hubs; planned but unscanned parcels stay. Requires If-Match.")
            .RequireAuthorization(StaffPolicies.Operate);
        transfers.MapPost("/{id:guid}/close", CloseAsync)
            .WithSummary("Closes the manifest at the destination hub; parcels not scanned off are flagged missing. Requires If-Match.")
            .RequireAuthorization(StaffPolicies.Operate);
        return app;
    }

    private static async Task<Ok<TransferListResult>> ListAsync([AsParameters] TransferQuery query, ShipmentsDbContext db, AutomationPolicies policies, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var grace = (await policies.GetAsync(tenant.TenantId, ct)).LineHaulGraceHours;
        var overdueBefore = now.AddHours(-grace);
        var all = db.Transfers.AsNoTracking();
        var filtered = all;
        if (query.Status is { } status)
        {
            filtered = filtered.Where(t => t.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Hub))
        {
            var hub = query.Hub.Trim().ToUpperInvariant();
            filtered = filtered.Where(t => t.FromHubCode == hub || t.ToHubCode == hub);
        }

        if (query.Overdue == true)
        {
            filtered = filtered.Where(t => t.Status == TransferStatus.InTransit && t.ExpectedAt < overdueBefore);
        }

        // Every word must appear in the truck's number, plate or driver, or be part of a parcel on it.
        foreach (var word in SearchText.Words(query.Q))
        {
            var pattern = SearchText.Pattern(word);
            filtered = filtered.Where(t => EF.Functions.Like(SearchSql.Fold(" " + t.Number), pattern)
                || (t.VehiclePlate != null && EF.Functions.Like(SearchSql.Fold(" " + t.VehiclePlate), pattern))
                || (t.DriverName != null && EF.Functions.Like(SearchSql.Fold(" " + t.DriverName), pattern))
                || t.Items.Any(i => EF.Functions.Like(SearchSql.Fold(" " + i.TrackingNumber), pattern)));
        }

        var total = await filtered.CountAsync(ct);
        var rows = await filtered
            .OrderBy(t => t.Status == TransferStatus.InTransit ? 0 : t.Status == TransferStatus.Loading ? 1 : 2)
            .ThenByDescending(t => t.CreatedAt)
            .Skip((query.EffectivePage - 1) * query.EffectivePageSize)
            .Take(query.EffectivePageSize)
            .Select(t => new
            {
                Transfer = t,
                Planned = t.Items.Count(i => i.State == TransferItemState.Planned),
                Loaded = t.Items.Count(i => i.State == TransferItemState.Loaded),
                Received = t.Items.Count(i => i.State == TransferItemState.Received),
                Missing = t.Items.Count(i => i.State == TransferItemState.Missing),
                Returns = t.Items.Count(i => i.IsReturn),
            })
            .ToListAsync(ct);

        var counts = await all.GroupBy(_ => 1).Select(g => new
        {
            Loading = g.Count(t => t.Status == TransferStatus.Loading),
            InTransit = g.Count(t => t.Status == TransferStatus.InTransit),
            Overdue = g.Count(t => t.Status == TransferStatus.InTransit && t.ExpectedAt < overdueBefore),
        }).SingleOrDefaultAsync(ct);

        var waiting = await ShipmentSearch.Filter(db.Shipments.AsNoTracking(), new ShipmentQuery { NeedsTransfer = true }, now)
            .CountAsync(s => s.TransferId == null, ct);

        var items = rows.Select(r => ToListItem(r.Transfer, r.Planned, r.Loaded, r.Received, r.Missing, r.Returns, now, grace)).ToList();
        return TypedResults.Ok(new TransferListResult(items, total, query.EffectivePage, query.EffectivePageSize,
            counts?.Loading ?? 0, counts?.InTransit ?? 0, counts?.Overdue ?? 0, waiting));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpResponse response, ShipmentsDbContext db, AutomationPolicies policies, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var transfer = await db.Transfers.AsNoTracking().Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (transfer is null)
        {
            return Api.NotFound("transfer.not_found");
        }

        response.Headers.ETag = Api.ETag(transfer.Version);
        return TypedResults.Ok(await ToDetailsAsync(transfer, db, clock.GetUtcNow(), (await policies.GetAsync(tenant.TenantId, ct)).LineHaulGraceHours, ct));
    }

    private static async Task<IResult> OpenAsync(CreateTransferRequest request, ClaimsPrincipal user, ShipmentsDbContext db, TransferPlanner planner, HubDirectory hubs, ITenantContext tenant, CancellationToken ct)
    {
        try
        {
            var network = await hubs.NetworkAsync(tenant.TenantId, ct);
            var from = network.Find(request.FromHubCode)?.Code ?? throw new DomainException("transfer.unknown_hub", "This hub is not active in your network.", request.FromHubCode);
            var to = network.Find(request.ToHubCode)?.Code ?? throw new DomainException("transfer.unknown_hub", "This hub is not active in your network.", request.ToHubCode);
            var transfer = await db.Transfers.Include(t => t.Items)
                .FirstOrDefaultAsync(t => t.Status == TransferStatus.Loading && t.FromHubCode == from && t.ToHubCode == to, ct);
            if (transfer is not null)
            {
                return Api.Conflict("transfer.already_loading", transfer.Number);
            }

            transfer = await planner.OpenAsync(from, to, request.VehiclePlate, request.DriverName, user.UserName(), ct);
            if (request.PlanWaiting)
            {
                await planner.PlanWaitingAsync(transfer, ct);
            }

            await db.SaveChangesAsync(ct);
            return TypedResults.Created("/" + ShipmentsApiRoutes.TransferById(transfer.Id), new { transfer.Id, transfer.Number });
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.IsUniqueViolation())
        {
            // Another station opened a truck on this route (or planned one of these parcels) at the same moment.
            return Api.Stale("transfer.stale");
        }
    }

    private static async Task<IResult> DepartAsync(Guid id, DepartTransferRequest request, HttpRequest http, ClaimsPrincipal user, ShipmentsDbContext db, TransferPlanner planner, CancellationToken ct) =>
        await MutateAsync(id, http, db, async transfer => await planner.DepartAsync(transfer, request, user.UserName(), ct), ct);

    private static async Task<IResult> CloseAsync(Guid id, CloseTransferRequest request, HttpRequest http, ClaimsPrincipal user, ShipmentsDbContext db, TransferPlanner planner, CancellationToken ct) =>
        await MutateAsync(id, http, db, transfer =>
        {
            planner.Close(transfer, request.Notes, user.UserName());
            return Task.CompletedTask;
        }, ct);

    private static async Task<IResult> MutateAsync(Guid id, HttpRequest http, ShipmentsDbContext db, Func<HubTransfer, Task> change, CancellationToken ct)
    {
        if (!Api.TryReadIfMatch(http, out var expected))
        {
            return Api.IfMatchRequired();
        }

        var transfer = await db.Transfers.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (transfer is null)
        {
            return Api.NotFound("transfer.not_found");
        }

        if (transfer.Version != expected)
        {
            return Api.Stale("transfer.stale");
        }

        try
        {
            await change(transfer);
            db.Entry(transfer).Property(t => t.Version).OriginalValue = expected;
            await db.SaveChangesAsync(ct);
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.IsUniqueViolation())
        {
            return Api.Stale("transfer.stale");
        }

        http.HttpContext.Response.Headers.ETag = Api.ETag(transfer.Version);
        return TypedResults.Ok(new { transfer.Id, transfer.Number, transfer.Status });
    }

    internal static TransferListItem ToListItem(HubTransfer t, int planned, int loaded, int received, int missing, int returns, DateTimeOffset now, int grace) => new(
        t.Id, t.Number, t.FromHubCode, t.ToHubCode, t.Status, planned, loaded, received, missing, returns,
        t.VehiclePlate, t.DriverName, t.SealNumber, t.CreatedAt, t.DepartedAt, t.ArrivedAt, t.ExpectedAt, t.IsOverdue(now, grace));

    private static async Task<TransferDetails> ToDetailsAsync(HubTransfer t, ShipmentsDbContext db, DateTimeOffset now, int grace, CancellationToken ct)
    {
        var ids = t.Items.Select(i => i.ShipmentId).ToList();
        var parcels = await db.Shipments.AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.Status, s.ConsigneeName, s.MerchantName, s.DestinationCode, s.CodAmount, s.CodCurrency })
            .ToDictionaryAsync(s => s.Id, ct);
        var items = t.Items
            .OrderBy(i => i.State)
            .ThenBy(i => i.TrackingNumber)
            .Select(i =>
            {
                var p = parcels[i.ShipmentId];
                return new TransferItemView(i.ShipmentId, i.TrackingNumber, i.State, i.IsReturn, p.Status, p.ConsigneeName, p.MerchantName,
                    p.DestinationCode, p.CodAmount, p.CodCurrency, i.PlannedAt, i.LoadedAt, i.ReceivedAt);
            })
            .ToList();
        var summary = ToListItem(t,
            t.Items.Count(i => i.State == TransferItemState.Planned), t.Items.Count(i => i.State == TransferItemState.Loaded),
            t.Items.Count(i => i.State == TransferItemState.Received), t.Items.Count(i => i.State == TransferItemState.Missing),
            t.Items.Count(i => i.IsReturn), now, grace);
        return new TransferDetails(summary, items, t.CreatedBy, t.DepartedBy, t.ReceivedBy, t.Notes, t.Version);
    }
}
