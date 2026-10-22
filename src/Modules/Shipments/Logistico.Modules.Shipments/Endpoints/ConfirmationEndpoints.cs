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

/// <summary>Orders waiting for the customer's confirmation: the staff queue, call results and the customer's own answer.</summary>
internal static class ConfirmationEndpoints
{
    public static IEndpointRouteBuilder MapConfirmationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/" + ShipmentsApiRoutes.Confirmations)
            .WithTags("Order confirmation")
            .RequireAuthorization(StaffPolicies.Staff)
            .WithValidation();
        group.MapGet("/", QueueAsync).WithSummary("Orders waiting for confirmation, oldest first, with each customer's return risk.");

        app.MapPost("/" + ShipmentsApiRoutes.Shipments + "/{id:guid}/confirmation", RecordAsync)
            .WithTags("Order confirmation")
            .WithSummary("Records a confirmation call: confirmed, no answer or declined (declined orders are cancelled). Requires If-Match.")
            .RequireAuthorization(StaffPolicies.Care)
            .WithValidation();

        app.MapPost("/" + ShipmentsApiRoutes.PublicTracking + "/{trackingNumber}/confirm", CustomerConfirmAsync)
            .WithTags("Public tracking")
            .WithSummary("The customer confirms or declines their order. The complete mobile number on the order proves it is them.")
            .AllowAnonymous()
            .RequireRateLimiting(ShipmentsModule.PublicRequestsRateLimitPolicy)
            .WithValidation();
        return app;
    }

    private static async Task<Ok<ConfirmationQueue>> QueueAsync(
        string? q,
        Guid? merchantId,
        int? page,
        int? pageSize,
        ShipmentsDbContext db,
        ConfirmationDesk desk,
        AutomationPolicies policies,
        ITenantContext tenant,
        TimeProvider clock,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var size = Math.Clamp(pageSize ?? 50, 1, 200);
        var skip = (Paging.Page(page) - 1) * size;
        var pending = db.Shipments.AsNoTracking().Where(s => s.Confirmation == ConfirmationStatus.Pending && s.Status == ShipmentStatus.Booked);
        if (merchantId is { } seller)
        {
            pending = pending.Where(s => s.MerchantId == seller);
        }

        pending = ShipmentSearch.ByText(pending, q);

        var overdueBefore = now.AddHours(-(await policies.GetAsync(tenant.TenantId, ct)).ConfirmationHours);
        var total = await pending.CountAsync(ct);
        var overdue = await pending.CountAsync(s => s.ConfirmationRequestedAt < overdueBefore, ct);
        var rows = await pending.OrderBy(s => s.ConfirmationRequestedAt).Skip(skip).Take(size)
            .Select(s => new
            {
                s.Id,
                s.TrackingNumber,
                s.MerchantName,
                s.ConsigneeName,
                s.ConsigneePhone,
                s.DestinationCode,
                s.DestinationCity,
                s.ItemsDescription,
                s.CodAmount,
                s.CreatedAt,
                s.ConfirmationRequestedAt,
                s.ConfirmationAttempts,
                s.Version,
            })
            .ToListAsync(ct);

        // History per customer, earlier orders only (this order is still open).
        var phones = rows.Select(r => r.ConsigneePhone).OfType<string>().Distinct().ToList();
        var returning = new[] { ShipmentStatus.ReturnInitiated, ShipmentStatus.ReturnInTransit, ShipmentStatus.ReturnAtHub, ShipmentStatus.Returned };
        var history = await db.Shipments.AsNoTracking().Where(s => s.ConsigneePhone != null && phones.Contains(s.ConsigneePhone))
            .GroupBy(s => s.ConsigneePhone!)
            .Select(g => new { Phone = g.Key, Delivered = g.Count(s => s.Status == ShipmentStatus.Delivered), Returned = g.Count(s => returning.Contains(s.Status)) })
            .ToDictionaryAsync(x => x.Phone, ct);
        var risks = await desk.RisksAsync(tenant.TenantId, phones, ct);

        var startOfDay = Api.StartOfBaghdadDay(now);
        var decided = await db.Shipments.AsNoTracking().Where(s => s.ConfirmationDecidedAt >= startOfDay)
            .GroupBy(s => s.Confirmation).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var autoCancelled = await db.AutomationLog.AsNoTracking().CountAsync(e => e.Rule == AutomationRule.AutoCancel && e.At >= startOfDay, ct);

        var items = rows.Select(r => new ConfirmationQueueItem(
            r.Id, r.TrackingNumber, r.MerchantName, r.ConsigneeName, r.ConsigneePhone, r.DestinationCode, r.DestinationCity, r.ItemsDescription,
            r.CodAmount, r.CreatedAt, r.ConfirmationRequestedAt, r.ConfirmationAttempts,
            r.ConsigneePhone is { } p ? risks.GetValueOrDefault(p, CustomerRisk.New) : CustomerRisk.New,
            r.ConsigneePhone is { } d && history.TryGetValue(d, out var h) ? h.Delivered : 0,
            r.ConsigneePhone is { } e && history.TryGetValue(e, out var x) ? x.Returned : 0,
            r.Version)).ToList();

        return TypedResults.Ok(new ConfirmationQueue(
            items,
            total,
            overdue,
            decided.Where(d => d.Key == ConfirmationStatus.Confirmed).Sum(d => d.Count),
            decided.Where(d => d.Key == ConfirmationStatus.Declined).Sum(d => d.Count),
            autoCancelled));
    }

    private static async Task<IResult> RecordAsync(
        Guid id,
        RecordConfirmationRequest request,
        HttpRequest http,
        HttpResponse response,
        ClaimsPrincipal user,
        ShipmentsDbContext db,
        ConfirmationDesk desk,
        CancellationToken ct)
    {
        if (!Api.TryReadIfMatch(http, out var expected))
        {
            return Api.IfMatchRequired();
        }

        var shipment = await db.Shipments.Include(s => s.Events).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shipment is null)
        {
            return Api.NotFound();
        }

        if (shipment.Version != expected)
        {
            return Api.Stale();
        }

        try
        {
            desk.Record(shipment, request.Outcome!.Value, request.Note, new Actor(user.UserName()));
            db.Entry(shipment).Property(s => s.Version).OriginalValue = expected;
            await db.SaveChangesAsync(ct);
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Api.Stale();
        }

        response.Headers.ETag = Api.ETag(shipment.Version);
        return TypedResults.Ok(ShipmentProjections.ToDetails(shipment));
    }

    private static async Task<IResult> CustomerConfirmAsync(string trackingNumber, CustomerConfirmation request, ShipmentsDbContext db, ConfirmationDesk desk, CustomerVerification verification, CancellationToken ct)
    {
        var normalized = TrackingNumber.Normalize(trackingNumber);
        var shipment = TrackingNumber.IsValid(normalized)
            ? await db.Shipments.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TrackingNumber == normalized, ct)
            : null;
        if (shipment is null)
        {
            return Api.NotFound();
        }

        if (PublicEndpoints.RejectUnlessCustomer(verification, shipment, request.PhoneLast4) is { } rejected)
        {
            return rejected;
        }

        using (TenantScope.Begin(shipment.TenantId))
        {
            try
            {
                desk.Record(shipment, request.Confirm ? ConfirmationOutcome.Confirmed : ConfirmationOutcome.Declined, null, new Actor("customer", "system"));
                await db.SaveChangesAsync(ct);
                return TypedResults.Ok(new { shipment.TrackingNumber, Confirmation = shipment.Confirmation, shipment.Status });
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
    }
}
