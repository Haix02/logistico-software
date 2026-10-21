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

internal static class RequestEndpoints
{
    public static IEndpointRouteBuilder MapRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var requests = app.MapGroup("/" + ShipmentsApiRoutes.Requests)
            .WithTags("Customer & seller requests")
            .RequireAuthorization(StaffPolicies.Staff)
            .WithValidation();

        requests.MapGet("/", ListAsync)
            .WithSummary("Requests inbox: filter by status, type, source, order or overdue.");
        requests.MapPost("/", CreateAsync)
            .WithSummary("Records a request taken by phone or from a seller; simple seller requests are applied automatically.")
            .RequireAuthorization(StaffPolicies.Care);
        requests.MapPost("/{id:guid}/start", StartAsync)
            .WithSummary("Marks a request as being handled by the current user.")
            .RequireAuthorization(StaffPolicies.Care);
        requests.MapPost("/{id:guid}/resolve", ResolveAsync)
            .WithSummary("Resolves (optionally applying the change to the order) or rejects a request.")
            .RequireAuthorization(StaffPolicies.Care);
        return app;
    }

    private static async Task<Ok<ServiceRequestList>> ListAsync([AsParameters] ServiceRequestQuery query, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var all = db.Requests.AsNoTracking();
        var filtered = all;
        if (query.Status is { Length: > 0 } statuses)
        {
            filtered = filtered.Where(r => statuses.Contains(r.Status));
        }

        if (query.Type is { } type)
        {
            filtered = filtered.Where(r => r.Type == type);
        }

        if (query.Source is { } source)
        {
            filtered = filtered.Where(r => r.Source == source);
        }

        if (query.ShipmentId is { } shipmentId)
        {
            filtered = filtered.Where(r => r.ShipmentId == shipmentId);
        }

        if (query.Overdue == true)
        {
            filtered = filtered.Where(r => (r.Status == RequestStatus.Open || r.Status == RequestStatus.InProgress) && r.DueAt < now);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = $"%{query.Q.Trim()}%";
            filtered = filtered.Where(r => EF.Functions.ILike(r.Number, pattern) || EF.Functions.ILike(r.TrackingNumber, pattern)
                || (r.Details != null && EF.Functions.ILike(r.Details, pattern)));
        }

        var total = await filtered.CountAsync(ct);
        var rows = await (
                from r in filtered
                join s in db.Shipments.AsNoTracking() on r.ShipmentId equals s.Id
                orderby (r.Status == RequestStatus.Open || r.Status == RequestStatus.InProgress) ? 0 : 1, r.DueAt
                select new { Request = r, s.ConsigneeName, s.MerchantName, s.Status })
            .Skip((query.EffectivePage - 1) * query.EffectivePageSize)
            .Take(query.EffectivePageSize)
            .ToListAsync(ct);

        var open = await all.Where(r => r.Status == RequestStatus.Open || r.Status == RequestStatus.InProgress)
            .GroupBy(r => r.Type)
            .Select(g => new { Type = g.Key, Count = g.Count(), Overdue = g.Count(r => r.DueAt < now) })
            .ToListAsync(ct);

        var items = rows.Select(r => ToItem(r.Request, r.ConsigneeName, r.MerchantName, r.Status, now)).ToList();
        return TypedResults.Ok(new ServiceRequestList(items, total, query.EffectivePage, query.EffectivePageSize,
            open.Sum(o => o.Count), open.Sum(o => o.Overdue), open.Select(o => new RequestTypeCount(o.Type, o.Count)).ToList()));
    }

    private static async Task<IResult> CreateAsync(CreateServiceRequest request, ClaimsPrincipal user, ShipmentsDbContext db, RequestDesk desk, TimeProvider clock, CancellationToken ct)
    {
        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == request.ShipmentId, ct);
        if (shipment is null)
        {
            return Api.NotFound();
        }

        try
        {
            var (created, _) = await desk.CreateAsync(shipment, request, request.Source == RequestSource.Customer ? RequestSource.Staff : request.Source, user.UserName(), ct);
            await db.SaveChangesAsync(ct);
            return TypedResults.Created("/" + ShipmentsApiRoutes.RequestById(created.Id), ToItem(created, shipment.ConsigneeName, shipment.MerchantName, shipment.Status, clock.GetUtcNow()));
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
    }

    private static async Task<IResult> StartAsync(Guid id, ClaimsPrincipal user, ShipmentsDbContext db, CancellationToken ct)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
        {
            return Api.NotFound("request.not_found");
        }

        try
        {
            request.Start(user.UserName());
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
    }

    private static async Task<IResult> ResolveAsync(Guid id, ResolveServiceRequest resolution, ClaimsPrincipal user, ShipmentsDbContext db, RequestDesk desk, CancellationToken ct)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
        {
            return Api.NotFound("request.not_found");
        }

        try
        {
            await desk.ResolveAsync(request, resolution, user.UserName(), ct);
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Api.Stale();
        }
    }

    internal static ServiceRequestItem ToItem(ServiceRequest r, string consignee, string? merchant, ShipmentStatus status, DateTimeOffset now) => new(
        r.Id, r.Number, r.ShipmentId, r.TrackingNumber, r.Type, r.Status, r.Source, r.Details, r.PreferredDate, r.NewGovernorate, r.NewCity,
        r.NewAddress, r.NewLandmark, r.NewPhone, r.CreatedAt, r.DueAt, r.ResolvedAt, r.ResolvedBy, r.Resolution, r.CreatedBy,
        consignee, merchant, status, r.IsOpen && r.DueAt < now, r.Version);
}
