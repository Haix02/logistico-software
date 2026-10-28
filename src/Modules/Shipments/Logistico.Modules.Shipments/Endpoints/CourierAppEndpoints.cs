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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Endpoints;

/// <summary>
/// The courier app: a courier signs in on their phone and sees only the parcels out for delivery with them, marks each
/// one delivered or failed, and sees their day in numbers. The account is linked to a courier by code (Team page);
/// nothing else of the company is visible to it. Deliveries go through the same scan logic as the hub station, so
/// cash collection, customer messages and automation rules apply exactly as when staff record them.
/// </summary>
internal static class CourierAppEndpoints
{
    public static IEndpointRouteBuilder MapCourierAppEndpoints(this IEndpointRouteBuilder app)
    {
        var run = app.MapGroup("/" + ShipmentsApiRoutes.CourierRun)
            .WithTags("Courier app")
            .RequireAuthorization(StaffPolicies.Deliver)
            .WithValidation();
        run.MapGet("/", RunAsync).WithSummary("The signed-in courier's parcels out for delivery and today's totals.");
        run.MapPost("/{id:guid}/delivered", DeliveredAsync)
            .WithSummary("Marks one of the courier's parcels delivered (cash on delivery collected), with proof: who took it, a signature, where.");
        run.MapPost("/{id:guid}/failed", FailedAsync)
            .WithSummary("Records a failed attempt with its reason and where the courier was; the company's rules decide what happens next.");
        return app;
    }

    private static async Task<IResult> RunAsync(ClaimsPrincipal user, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (await CourierOfAsync(user, db, ct) is not { } courier)
        {
            return NotLinked();
        }

        var label = courier.DisplayLabel;
        var startOfDay = Api.StartOfBaghdadDay(clock.GetUtcNow());
        var stops = await db.Shipments.AsNoTracking()
            .Where(s => s.CourierName == label && s.Status == ShipmentStatus.OutForDelivery)
            .OrderBy(s => s.DestinationCode).ThenBy(s => s.DestinationCity).ThenBy(s => s.LastStatusAt)
            .Select(s => new CourierStop(
                s.Id, s.TrackingNumber, s.ConsigneeName, s.ConsigneePhone, s.DestinationCode, s.DestinationCity, s.DeliveryAddress,
                s.Landmark, s.ItemsDescription, s.CodAmount, s.CodCurrency, s.DeliveryAttempts, s.MerchantName, s.Notes))
            .ToListAsync(ct);
        var delivered = await db.Shipments.AsNoTracking()
            .Where(s => s.CourierName == label && s.Status == ShipmentStatus.Delivered && s.DeliveredAt >= startOfDay)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Cash = g.Sum(s => s.CodCurrency == "IQD" ? s.CodAmount ?? 0 : 0) })
            .FirstOrDefaultAsync(ct);
        var failed = await db.ShipmentEvents.AsNoTracking()
            .CountAsync(e => e.CourierName == label && e.Status == ShipmentStatus.DeliveryFailed && e.OccurredAt >= startOfDay, ct);

        // Everything collected and not handed in at the office yet, whichever day it was collected.
        var toHandIn = await db.Shipments.AsNoTracking()
            .Where(s => s.CourierName == label && s.CodCollectedAt != null && s.CodHandedOverAt == null)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Cash = g.Sum(s => s.CodAmount ?? 0) })
            .FirstOrDefaultAsync(ct);

        return TypedResults.Ok(new CourierRun(courier.Code, courier.Name, delivered?.Count ?? 0, failed, delivered?.Cash ?? 0, stops,
            toHandIn?.Cash ?? 0, toHandIn?.Count ?? 0));
    }

    // An app opened before proof of delivery existed sends no body; the delivery still counts, without proof.
    private static Task<IResult> DeliveredAsync(
        Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CourierDeliveryRequest? request, ClaimsPrincipal user, ShipmentsDbContext db,
        WarehouseScanner scanner, TimeProvider clock, CancellationToken ct) =>
        CompleteAsync(id, ScanOperation.Deliver, null, user, db, scanner, ct,
            (tenant, courier) => DeliveryProof.ForDelivery(tenant, id, courier, request ?? new CourierDeliveryRequest(), clock.GetUtcNow()));

    private static Task<IResult> FailedAsync(
        Guid id, CourierFailureRequest request, ClaimsPrincipal user, ShipmentsDbContext db, WarehouseScanner scanner, TimeProvider clock, CancellationToken ct) =>
        CompleteAsync(id, ScanOperation.Fail, request.Reason, user, db, scanner, ct,
            (tenant, courier) => DeliveryProof.ForFailure(tenant, id, courier, request.Reason!.Value, request.Location, clock.GetUtcNow()));

    /// <summary>Records the attempt through the hub scan logic and keeps its proof, both in one transaction.</summary>
    private static async Task<IResult> CompleteAsync(
        Guid id,
        ScanOperation operation,
        DeliveryFailureReason? reason,
        ClaimsPrincipal user,
        ShipmentsDbContext db,
        WarehouseScanner scanner,
        CancellationToken ct,
        Func<Guid, string, DeliveryProof> proof)
    {
        if (await CourierOfAsync(user, db, ct) is not { } courier)
        {
            return NotLinked();
        }

        // Only parcels out for delivery with this courier; anything else looks like it does not exist.
        var parcel = await db.Shipments.AsNoTracking()
            .Where(s => s.Id == id && s.CourierName == courier.DisplayLabel && s.Status == ShipmentStatus.OutForDelivery)
            .Select(s => new { s.TenantId, s.TrackingNumber, Hub = s.CurrentHubCode ?? s.DestinationHubCode })
            .FirstOrDefaultAsync(ct);
        if (parcel is null)
        {
            return Api.NotFound("courier.not_on_run");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var outcome = (await scanner.ScanAsync(operation, [parcel.TrackingNumber], parcel.Hub, null, courier.DisplayLabel, null, reason, user.UserName(), ct)).Single();
        if (outcome.Ok)
        {
            db.DeliveryProofs.Add(proof(parcel.TenantId, courier.DisplayLabel));
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct); // a refused scan stays in the hub's scan log, as before
        return outcome.Ok
            ? TypedResults.Ok(outcome)
            : Api.Conflict(outcome.ProblemCode ?? "courier.not_on_run", parcel.TrackingNumber);
    }

    /// <summary>The active courier this account delivers for, by the code on its sign-in claim (null when not linked).</summary>
    private static async Task<Courier?> CourierOfAsync(ClaimsPrincipal user, ShipmentsDbContext db, CancellationToken ct) =>
        user.FindFirstValue(LogisticoClaimTypes.CourierCode) is { Length: > 0 } code
            ? await db.Couriers.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code && c.IsActive, ct)
            : null;

    private static ProblemHttpResult NotLinked() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: ShipmentTexts.Problem("courier.not_linked"),
            type: "https://logistico.app/problems/courier.not_linked",
            extensions: new Dictionary<string, object?> { ["code"] = "courier.not_linked" });
}
