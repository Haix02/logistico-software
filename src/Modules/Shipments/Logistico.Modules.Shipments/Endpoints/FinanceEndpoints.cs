using System.Globalization;
using System.Security.Claims;
using System.Text;
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

internal static class FinanceEndpoints
{
    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var finance = app.MapGroup("/" + ShipmentsApiRoutes.Finance)
            .WithTags("Finance & COD")
            .RequireAuthorization(StaffPolicies.Finance)
            .WithValidation();

        finance.MapGet("/summary", SummaryAsync).WithSummary("COD KPIs, unsettled seller balances and recent settlements (aggregated in SQL).");
        finance.MapPost("/settlements", SettleAsync).WithSummary("Settles all delivered, unsettled COD orders of a seller into one payout batch.")
            .RequireAuthorization(StaffPolicies.Finance);
        finance.MapGet("/settlements/{id:guid}/export.csv", ExportAsync).WithSummary("Parcel-by-parcel CSV statement (UTF-8 with BOM) of a settlement.")
            .RequireAuthorization(StaffPolicies.Finance);
        return app;
    }

    private static async Task<Ok<FinanceSummary>> SummaryAsync(Guid? merchantId, ShipmentsDbContext db, CancellationToken ct)
    {
        var shipments = db.Shipments.AsNoTracking().Where(s => s.Status != ShipmentStatus.Cancelled && (s.CodAmount > 0 || s.DeliveryFee > 0));
        var settlements = db.CodSettlements.AsNoTracking();
        if (merchantId is { } id)
        {
            shipments = shipments.Where(s => s.MerchantId == id);
            settlements = settlements.Where(s => s.MerchantId == id);
        }

        var returning = ShipmentLifecycle.ReturnStatuses;
        var balances = await shipments
            .Where(s => s.MerchantId != null)
            .GroupBy(s => new { s.MerchantId, s.MerchantName })
            .Select(g => new
            {
                g.Key.MerchantId,
                g.Key.MerchantName,
                UnsettledCount = g.Count(s => s.CodCurrency == "IQD" && s.Status == ShipmentStatus.Delivered && s.CodCollectedAt != null && s.CodSettlementId == null),
                Gross = g.Sum(s => s.CodCurrency == "IQD" && s.Status == ShipmentStatus.Delivered && s.CodCollectedAt != null && s.CodSettlementId == null ? s.CodAmount ?? 0m : 0m),
                Fees = g.Sum(s => s.CodCurrency == "IQD" && s.Status == ShipmentStatus.Delivered && s.CodCollectedAt != null && s.CodSettlementId == null ? s.DeliveryFee ?? 0m : 0m),
                InProgressCount = g.Count(s => s.CodCurrency == "IQD" && s.CodAmount > 0 && s.CodCollectedAt == null && s.Status != ShipmentStatus.Returned && !returning.Contains(s.Status)),
                InProgressCod = g.Sum(s => s.CodCurrency == "IQD" && s.CodAmount > 0 && s.CodCollectedAt == null && s.Status != ShipmentStatus.Returned && !returning.Contains(s.Status) ? s.CodAmount ?? 0m : 0m),
            })
            .ToListAsync(ct);

        var totals = await shipments.GroupBy(_ => 1).Select(g => new
        {
            Pending = g.Sum(s => s.CodCurrency == "IQD" && s.CodAmount > 0 && s.CodCollectedAt == null && s.Status != ShipmentStatus.Returned && !returning.Contains(s.Status) ? s.CodAmount ?? 0m : 0m),
            Gross = g.Sum(s => s.CodCurrency == "IQD" && s.Status == ShipmentStatus.Delivered && s.CodCollectedAt != null && s.CodSettlementId == null ? s.CodAmount ?? 0m : 0m),
            Fees = g.Sum(s => s.CodCurrency == "IQD" && s.Status == ShipmentStatus.Delivered && s.CodCollectedAt != null && s.CodSettlementId == null ? s.DeliveryFee ?? 0m : 0m),
            Revenue = g.Sum(s => s.Status == ShipmentStatus.Delivered ? s.DeliveryFee ?? 0m : 0m),
        }).SingleOrDefaultAsync(ct);

        var recent = await settlements.OrderByDescending(s => s.SettledAt).Take(50).Select(s => ToItem(s)).ToListAsync(ct);
        var paid = await settlements.SumAsync(s => (decimal?)s.NetPayoutAmount, ct) ?? 0m;

        var sellerBalances = balances
            .Where(b => b.UnsettledCount > 0 || b.InProgressCount > 0)
            .Select(b => new SellerCodBalance(b.MerchantId!.Value, b.MerchantName ?? "—", b.UnsettledCount, b.Gross, b.Fees, b.Gross - b.Fees, b.InProgressCount, b.InProgressCod))
            .OrderByDescending(b => b.NetPayableIqd)
            .ThenByDescending(b => b.InProgressCodIqd)
            .ToList();

        var gross = totals?.Gross ?? 0m;
        var fees = totals?.Fees ?? 0m;
        return TypedResults.Ok(new FinanceSummary(totals?.Pending ?? 0m, gross, fees, gross - fees, paid, totals?.Revenue ?? 0m, sellerBalances, recent));
    }

    private static async Task<IResult> SettleAsync(CreateCodSettlementRequest request, ClaimsPrincipal user, ShipmentsDbContext db, NumberGenerator numbers, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var merchantId = request.MerchantId!.Value;
        var unsettled = await db.Shipments
            .Where(s => s.MerchantId == merchantId && s.CodCurrency == "IQD" && s.Status == ShipmentStatus.Delivered && s.CodCollectedAt != null && s.CodSettlementId == null && s.CodAmount > 0)
            .OrderBy(s => s.DeliveredAt)
            .ToListAsync(ct);
        if (unsettled.Count == 0)
        {
            return Api.Rule(new DomainException("finance.nothing_to_settle", "This seller has no unsettled delivered cash-on-delivery orders."));
        }

        try
        {
            var settlement = CodSettlement.Create(tenant.TenantId, await numbers.SettlementNumberAsync(ct), merchantId, unsettled[0].MerchantName ?? "—", unsettled,
                request.PayoutMethod, request.PayoutReference, request.Notes, user.UserName(), clock.GetUtcNow());
            db.CodSettlements.Add(settlement);
            await db.SaveChangesAsync(ct);
            return TypedResults.Created("/" + ShipmentsApiRoutes.FinanceSettlements + $"/{settlement.Id}", ToItem(settlement));
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

    private static async Task<IResult> ExportAsync(Guid id, HttpResponse response, ShipmentsDbContext db, CancellationToken ct)
    {
        var settlement = await db.CodSettlements.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (settlement is null)
        {
            return Api.NotFound("finance.settlement_not_found");
        }

        var shipments = await db.Shipments.AsNoTracking().Where(s => s.CodSettlementId == id).OrderBy(s => s.DeliveredAt).ToListAsync(ct);
        response.ContentType = "text/csv; charset=utf-8";
        response.Headers.ContentDisposition = $"attachment; filename=\"logistico-settlement-{settlement.SettlementNumber}.csv\"";

        var csv = new StringBuilder();
        csv.Append('﻿'); // UTF-8 BOM for Excel Arabic support
        csv.AppendLine("Settlement number,Seller,Tracking number,Order ref,Customer,Governorate,Delivered at,Gross COD (IQD),Delivery fee (IQD),Net seller payout (IQD),Payout method,Payout reference");
        foreach (var s in shipments)
        {
            var gross = s.CodAmount ?? 0m;
            var fee = s.DeliveryFee ?? 0m;
            csv.AppendJoin(',',
                Api.Csv(settlement.SettlementNumber), Api.Csv(settlement.MerchantName), Api.Csv(s.TrackingNumber), Api.Csv(s.ExternalReference),
                Api.Csv(s.ConsigneeName), Api.Csv(IraqGovernorates.NameOrCode(s.DestinationCode)),
                s.DeliveredAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "",
                gross.ToString(CultureInfo.InvariantCulture), fee.ToString(CultureInfo.InvariantCulture), (gross - fee).ToString(CultureInfo.InvariantCulture),
                Api.Csv(ShipmentTexts.Of(settlement.PayoutMethod)), Api.Csv(settlement.PayoutReference));
            csv.AppendLine();
        }

        await response.WriteAsync(csv.ToString(), Encoding.UTF8, ct);
        return TypedResults.Empty;
    }

    private static CodSettlementItem ToItem(CodSettlement s) => new(
        s.Id, s.SettlementNumber, s.MerchantId, s.MerchantName, s.Currency, s.ShipmentCount, s.GrossCodAmount, s.DeliveryFeesAmount,
        s.NetPayoutAmount, s.PayoutMethod, s.PayoutReference, s.Notes, s.SettledAt, s.SettledBy);
}
