using Logistico.Modules.Shipments.Application;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Iraq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Endpoints;

/// <summary>Anonymous endpoints for customers: tracking and self-service requests (new date, address, cancel, return).</summary>
internal static class PublicEndpoints
{
    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/" + ShipmentsApiRoutes.PublicTracking + "/{trackingNumber}", TrackAsync)
            .WithTags("Public tracking")
            .WithSummary("Coarse public tracking by tracking number. Seller, customer, COD and exact route data are omitted.")
            .AllowAnonymous()
            .RequireRateLimiting(ShipmentsModule.PublicTrackingRateLimitPolicy);

        app.MapPost("/" + ShipmentsApiRoutes.PublicTracking + "/{trackingNumber}/requests", CreateRequestAsync)
            .WithTags("Public tracking")
            .WithSummary("Customer self-service request verified with the complete mobile number on the order.")
            .AllowAnonymous()
            .RequireRateLimiting(ShipmentsModule.PublicRequestsRateLimitPolicy)
            .WithValidation();

        app.MapPost("/" + ShipmentsApiRoutes.PublicTracking + "/{trackingNumber}/rating", RateAsync)
            .WithTags("Public tracking")
            .WithSummary("The customer rates a delivered order (1–5). Two stars or fewer open a complaint for customer care.")
            .AllowAnonymous()
            .RequireRateLimiting(ShipmentsModule.PublicRequestsRateLimitPolicy)
            .WithValidation();
        return app;
    }

    private static async Task<IResult> RateAsync(string trackingNumber, CustomerRating request, ShipmentsDbContext db, RequestDesk desk, CustomerVerification verification, TimeProvider clock, CancellationToken ct)
    {
        var normalized = TrackingNumber.Normalize(trackingNumber);
        var shipment = TrackingNumber.IsValid(normalized)
            ? await db.Shipments.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TrackingNumber == normalized, ct)
            : null;
        if (shipment is null)
        {
            return Api.NotFound();
        }

        if (RejectUnlessCustomer(verification, shipment, request.PhoneLast4) is { } rejected)
        {
            return rejected;
        }

        using (TenantScope.Begin(shipment.TenantId))
        {
            try
            {
                shipment.Rate(request.Stars, request.Comment, clock.GetUtcNow());
                if (request.Stars <= RatingRules.ComplaintAtOrBelow)
                {
                    // An unhappy customer gets a call back: the low rating becomes a complaint in the requests inbox.
                    var details = $"{new string('★', request.Stars)}{new string('☆', 5 - request.Stars)} {shipment.RatingComment}".Trim();
                    await desk.CreateAsync(shipment, new CreateServiceRequest { ShipmentId = shipment.Id, Type = RequestType.Complaint, Details = details }, RequestSource.Customer, "customer", ct);
                }

                await db.SaveChangesAsync(ct);
                return TypedResults.Ok(new { shipment.TrackingNumber, shipment.Rating });
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

    private static async Task<IResult> TrackAsync(string trackingNumber, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var normalized = TrackingNumber.Normalize(trackingNumber);
        if (!TrackingNumber.IsValid(normalized))
        {
            return Api.NotFound();
        }

        // Public lookup spans tenants: tracking numbers come from one global sequence.
        var shipment = await db.Shipments.IgnoreQueryFilters().AsNoTracking().Include(s => s.Events)
            .FirstOrDefaultAsync(s => s.TrackingNumber == normalized, ct);
        if (shipment is null)
        {
            return Api.NotFound();
        }

        var openRequests = await db.Requests.IgnoreQueryFilters().CountAsync(r => r.TenantId == shipment.TenantId && r.ShipmentId == shipment.Id
            && (r.Status == RequestStatus.Open || r.Status == RequestStatus.InProgress), ct);
        return TypedResults.Ok(ShipmentProjections.ToPublicView(shipment, clock.GetUtcNow(), openRequests));
    }

    private static async Task<IResult> CreateRequestAsync(string trackingNumber, CustomerRequest request, ShipmentsDbContext db, RequestDesk desk, CustomerVerification verification, CancellationToken ct)
    {
        var normalized = TrackingNumber.Normalize(trackingNumber);
        var shipment = TrackingNumber.IsValid(normalized)
            ? await db.Shipments.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TrackingNumber == normalized, ct)
            : null;
        if (shipment is null)
        {
            return Api.NotFound();
        }

        if (RejectUnlessCustomer(verification, shipment, request.PhoneLast4) is { } rejected)
        {
            return rejected;
        }

        using (TenantScope.Begin(shipment.TenantId))
        {
            try
            {
                var (created, applied) = await desk.CreateAsync(shipment, request, RequestSource.Customer, "customer", ct);
                await db.SaveChangesAsync(ct);
                return TypedResults.Ok(new CustomerRequestReceipt(created.Number, created.Type, created.Status, applied, created.CreatedAt));
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

    /// <summary>
    /// Null when the caller gave the last four digits of the order's mobile number. "No phone" and "wrong digits" get the
    /// same answer so the endpoint reveals nothing; too many wrong answers lock the order's self-service for a day.
    /// </summary>
    internal static ProblemHttpResult? RejectUnlessCustomer(CustomerVerification verification, Shipment shipment, string phoneLast4) =>
        verification.Check(shipment, phoneLast4) switch
        {
            VerificationResult.Verified => null,
            VerificationResult.Locked => TypedResults.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: ShipmentTexts.Problem("request.verification_locked"),
                extensions: new Dictionary<string, object?> { ["code"] = "request.verification_locked" }),
            _ => TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: ShipmentTexts.Problem("request.verification_failed"),
                extensions: new Dictionary<string, object?> { ["code"] = "request.verification_failed" }),
        };
}
