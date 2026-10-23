using System.Security.Claims;
using Logistico.Modules.Merchants.Contracts;
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

/// <summary>
/// The seller portal: an online shop signs in and books its own orders, follows them, cancels one before pickup, and
/// sees what the company owes it and what it has paid out. The account is linked to one seller (Team page) and sees
/// nothing else of the company. Booking goes through the same <see cref="OrderDesk"/> as the office's order screen, so
/// the company's prices, numbering, routing and confirmation calls apply unchanged.
/// </summary>
internal static class SellerPortalEndpoints
{
    public static IEndpointRouteBuilder MapSellerPortalEndpoints(this IEndpointRouteBuilder app)
    {
        var seller = app.MapGroup("/" + ShipmentsApiRoutes.Seller)
            .WithTags("Seller portal")
            .RequireAuthorization(StaffPolicies.Sell)
            .WithValidation();
        seller.MapGet("/", HomeAsync).WithSummary("The shop's orders in numbers, the money it is owed and its payouts.");
        seller.MapGet("/orders", OrdersAsync).WithSummary("The shop's own orders, newest first.");
        seller.MapPost("/orders", BookAsync).WithSummary("Books a delivery from the shop's governorate; the fee comes from the company's prices.");
        seller.MapGet("/quote", QuoteAsync).WithSummary("The company's delivery fee for this shop to a governorate.");
        seller.MapPost("/orders/{id:guid}/cancel", CancelAsync).WithSummary("Cancels an order that has not been picked up yet.");
        seller.MapGet("/payouts/{id:guid}/statement.csv", StatementAsync).WithSummary("Parcel-by-parcel CSV statement of one of the shop's payouts.");
        return app;
    }

    private static async Task<IResult> HomeAsync(ClaimsPrincipal user, IMerchantDirectory merchants, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (await ShopOfAsync(user, merchants, ct) is not { } shop)
        {
            return NotLinked();
        }

        var since = clock.GetUtcNow().AddDays(-30);
        var returning = ShipmentLifecycle.ReturnStatuses;
        var closed = ShipmentLifecycle.ClosedStatuses;
        var counts = await db.Shipments.AsNoTracking()
            .Where(s => s.MerchantId == shop.Id)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Active = g.Count(s => !closed.Contains(s.Status) && !returning.Contains(s.Status)),
                Returning = g.Count(s => returning.Contains(s.Status)),
                Delivered = g.Count(s => s.Status == ShipmentStatus.Delivered && s.DeliveredAt >= since),
                Returned = g.Count(s => s.Status == ShipmentStatus.Returned && s.LastStatusAt >= since),
            })
            .FirstOrDefaultAsync(ct);

        var money = await FinanceEndpoints.SummarizeAsync(db, shop.Id, ct);
        var balance = money.SellerBalances.FirstOrDefault();
        return TypedResults.Ok(new SellerHome(
            shop.Id, shop.Name, shop.Governorate,
            counts?.Active ?? 0, counts?.Returning ?? 0, counts?.Delivered ?? 0, counts?.Returned ?? 0,
            money.UnsettledNetPayableIqd, balance?.DeliveredUnsettledCount ?? 0,
            money.PendingCodToCollectIqd + money.CodWithCouriersIqd, (balance?.InProgressCount ?? 0) + (balance?.WithCouriersCount ?? 0),
            money.TotalSettledNetPaidIqd, money.RecentSettlements));
    }

    private static async Task<IResult> OrdersAsync([AsParameters] SellerOrderQuery query, ClaimsPrincipal user, IMerchantDirectory merchants, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (await ShopOfAsync(user, merchants, ct) is not { } shop)
        {
            return NotLinked();
        }

        var search = new ShipmentQuery
        {
            Q = query.Q,
            MerchantId = shop.Id,
            Status = query.View switch
            {
                SellerOrderView.Active => [.. Enum.GetValues<ShipmentStatus>().Where(s => ShipmentLifecycle.IsActive(s) && !ShipmentLifecycle.IsReturning(s))],
                SellerOrderView.Delivered => [ShipmentStatus.Delivered],
                SellerOrderView.Returns => [.. ShipmentLifecycle.ReturnStatuses, ShipmentStatus.Returned],
                _ => null,
            },
            Page = query.Page,
            PageSize = SellerOrderQuery.PageSize,
        };
        var filtered = ShipmentSearch.Filter(db.Shipments.AsNoTracking(), search, clock.GetUtcNow());
        var total = await filtered.CountAsync(ct);
        var items = await ShipmentSearch.Sort(filtered, search)
            .Skip((search.EffectivePage - 1) * search.EffectivePageSize)
            .Take(search.EffectivePageSize)
            .Select(s => new SellerOrder(
                s.Id, s.TrackingNumber, s.Status, s.ConsigneeName, s.ConsigneePhone, s.DestinationCode, s.DestinationCity, s.ItemsDescription,
                s.CodAmount, s.DeliveryFee, s.DeliveryAttempts, s.LastFailureReason, s.ReturnReason, s.ExternalReference, s.CreatedAt, s.LastStatusAt,
                s.CodSettlementId != null))
            .ToListAsync(ct);
        return TypedResults.Ok(new SellerOrderList(items, total, search.EffectivePage, search.EffectivePageSize));
    }

    private static async Task<IResult> BookAsync(CreateShipmentRequest request, ClaimsPrincipal user, IMerchantDirectory merchants, OrderDesk orders, CancellationToken ct)
    {
        if (await ShopOfAsync(user, merchants, ct) is not { } shop)
        {
            return NotLinked();
        }

        // A shop books domestic deliveries from its own governorate at the company's price; the rest is the office's call.
        request.ServiceType = ServiceType.Domestic;
        request.Mode = TransportMode.Road;
        request.MerchantId = shop.Id;
        request.OriginCode = shop.Governorate;
        request.CodCurrency = "IQD";
        request.DeliveryFee = null;
        request.PromisedAt = null;
        request.HouseBillNumber = null;
        request.Incoterm = null;
        if (Api.Validate(request) is { } invalid)
        {
            return invalid;
        }

        try
        {
            var shipment = await orders.BookAsync(request, user.UserName(), ct);
            return TypedResults.Created("/" + ShipmentsApiRoutes.SellerOrders, new CreateShipmentResponse(shipment.Id, shipment.TrackingNumber));
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
    }

    private static async Task<IResult> QuoteAsync(string? destination, decimal? weightKg, ClaimsPrincipal user, IMerchantDirectory merchants, Pricing pricing, ITenantContext tenant, CancellationToken ct)
    {
        if (await ShopOfAsync(user, merchants, ct) is not { } shop)
        {
            return NotLinked();
        }

        if (!IraqGovernorates.IsKnown(destination))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["destination"] = [ValidationText.Governorate] });
        }

        var weight = Math.Clamp(weightKg ?? 1m, 0.001m, 1_000_000m);
        return TypedResults.Ok((await pricing.GetAsync(tenant.TenantId, ct)).Quote(shop.Governorate, destination, weight, shop.Id));
    }

    private static async Task<IResult> CancelAsync(Guid id, ClaimsPrincipal user, IMerchantDirectory merchants, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (await ShopOfAsync(user, merchants, ct) is not { } shop)
        {
            return NotLinked();
        }

        var shipment = await db.Shipments.Include(s => s.Events).FirstOrDefaultAsync(s => s.Id == id && s.MerchantId == shop.Id, ct);
        if (shipment is null)
        {
            return Api.NotFound();
        }

        // Once a courier has it, cancelling means a return trip the office arranges; the shop asks them instead.
        if (shipment.Status is not (ShipmentStatus.Draft or ShipmentStatus.Booked))
        {
            return Api.Conflict("seller.cannot_cancel", shipment.TrackingNumber);
        }

        try
        {
            shipment.Cancel(null, new Actor(user.UserName()), clock.GetUtcNow());
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

    private static async Task<IResult> StatementAsync(Guid id, ClaimsPrincipal user, IMerchantDirectory merchants, HttpResponse response, ShipmentsDbContext db, CancellationToken ct) =>
        await ShopOfAsync(user, merchants, ct) is { } shop
            ? await FinanceEndpoints.StatementAsync(id, shop.Id, response, db, ct)
            : NotLinked();

    /// <summary>The active seller this account works for, by the id on its sign-in claim (null when not linked).</summary>
    private static async Task<MerchantLookupItem?> ShopOfAsync(ClaimsPrincipal user, IMerchantDirectory merchants, CancellationToken ct) =>
        Guid.TryParse(user.FindFirstValue(LogisticoClaimTypes.MerchantId), out var id)
            ? await merchants.FindActiveAsync(id, ct)
            : null;

    private static ProblemHttpResult NotLinked() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: ShipmentTexts.Problem("seller.not_linked"),
            type: "https://logistico.app/problems/seller.not_linked",
            extensions: new Dictionary<string, object?> { ["code"] = "seller.not_linked" });
}
