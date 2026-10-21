using System.Globalization;
using System.Text;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Logistico.SharedKernel.Iraq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Logistico.Modules.Shipments.Endpoints;

/// <summary>
/// Demand intelligence: what Iraqi customers order (recognised from item descriptions), where, when, at what basket size,
/// and how those orders end. Plus an anonymised open-data export so companies can share what they learn.
/// </summary>
internal static class InsightsEndpoints
{
    private static readonly int[] AllowedDays = [30, 90, 365];

    private static readonly ShipmentStatus[] Returning =
        [ShipmentStatus.ReturnInitiated, ShipmentStatus.ReturnInTransit, ShipmentStatus.ReturnAtHub, ShipmentStatus.Returned];

    /// <summary>Open-data rows describing fewer orders than this are left out, so no customer or small seller can be singled out.</summary>
    public const int DefaultOpenDataMinimumOrders = 10;

    public static IEndpointRouteBuilder MapInsightsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/" + ShipmentsApiRoutes.Insights, GetAsync)
            .WithTags("Insights")
            .WithSummary("Demand intelligence for the last 30, 90 or 365 days: governorates, product categories, trade lanes, basket sizes, ordering times, repeat customers.")
            .RequireAuthorization();

        app.MapGet("/" + ShipmentsApiRoutes.InsightsOpenData, OpenDataAsync)
            .WithTags("Insights")
            .WithSummary("Anonymised aggregate CSV for publishing (no names, phones or addresses; small groups left out). See docs/16-data-and-insights.md.")
            .RequireAuthorization(StaffPolicies.Administer); // publishing company data is a management decision
        return app;
    }

    private static async Task<Ok<DemandInsights>> GetAsync(int? days, ShipmentsDbContext db, ITenantContext tenant, TimeProvider clock, CancellationToken ct) =>
        TypedResults.Ok(await BuildAsync(db, tenant.TenantId, Span(days), clock.GetUtcNow(), ct));

    private static int Span(int? days) => AllowedDays.Contains(days ?? 30) ? days ?? 30 : 30;

    internal static async Task<DemandInsights> BuildAsync(ShipmentsDbContext db, Guid tenantId, int span, DateTimeOffset now, CancellationToken ct)
    {
        var from = Api.StartOfBaghdadDay(now).AddDays(-(span - 1));
        var previousFrom = from.AddDays(-span);
        var domestic = db.Shipments.AsNoTracking().Where(s => s.ServiceType == ServiceType.Domestic && s.Status != ShipmentStatus.Cancelled);
        var current = domestic.Where(s => s.CreatedAt >= from && s.CreatedAt < now);
        var previous = domestic.Where(s => s.CreatedAt >= previousFrom && s.CreatedAt < from);

        var governorates = await current
            .GroupBy(s => s.DestinationCode)
            .Select(g => new
            {
                Code = g.Key,
                Orders = g.Count(),
                Delivered = g.Count(s => s.Status == ShipmentStatus.Delivered),
                Returned = g.Count(s => Returning.Contains(s.Status)),
                CodSum = g.Sum(s => s.CodAmount ?? 0),
                CodOrders = g.Count(s => s.CodAmount != null),
                Hours = g.Where(s => s.DeliveredAt != null).Average(s => (double?)(s.DeliveredAt!.Value - s.CreatedAt).TotalHours),
            })
            .ToListAsync(ct);

        var before = await previous
            .GroupBy(s => s.DestinationCode)
            .Select(g => new
            {
                Code = g.Key,
                Orders = g.Count(),
                Delivered = g.Count(s => s.Status == ShipmentStatus.Delivered),
                Returned = g.Count(s => Returning.Contains(s.Status)),
                CodSum = g.Sum(s => s.CodAmount ?? 0),
                CodOrders = g.Count(s => s.CodAmount != null),
            })
            .ToListAsync(ct);

        // The most common reason a delivery attempt failed, per customer governorate.
        var failures = await db.ShipmentEvents.AsNoTracking()
            .Where(e => e.Status == ShipmentStatus.DeliveryFailed && e.FailureReason != null && e.OccurredAt >= from && e.OccurredAt < now)
            .Join(domestic, e => e.ShipmentId, s => s.Id, (e, s) => new { s.DestinationCode, Reason = e.FailureReason!.Value })
            .GroupBy(x => new { x.DestinationCode, x.Reason })
            .Select(g => new { g.Key.DestinationCode, g.Key.Reason, Count = g.Count() })
            .ToListAsync(ct);
        var topFailure = failures.GroupBy(f => f.DestinationCode)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.Count).ThenBy(f => f.Reason).First().Reason);

        // Item descriptions repeat a lot (shops sell the same products), so they are grouped before being classified.
        var items = await current
            .GroupBy(s => new { s.ItemsDescription, s.DestinationCode })
            .Select(g => new
            {
                Description = g.Key.ItemsDescription,
                g.Key.DestinationCode,
                Orders = g.Count(),
                Delivered = g.Count(s => s.Status == ShipmentStatus.Delivered),
                Returned = g.Count(s => Returning.Contains(s.Status)),
                CodSum = g.Sum(s => s.CodAmount ?? 0),
                CodOrders = g.Count(s => s.CodAmount != null),
            })
            .ToListAsync(ct);
        var categoryOf = items.Select(i => i.Description ?? "").Distinct().ToDictionary(d => d, ProductCatalog.Classify);
        var categories = items
            .GroupBy(i => categoryOf[i.Description ?? ""])
            .Select(g => new CategoryDemand(g.Key, g.Sum(i => i.Orders), g.Sum(i => i.Delivered), g.Sum(i => i.Returned), Average(g.Sum(i => i.CodSum), g.Sum(i => i.CodOrders))))
            .OrderByDescending(c => c.Orders)
            .ToList();
        var topCategory = items
            .GroupBy(i => i.DestinationCode)
            .ToDictionary(g => g.Key, g => g.GroupBy(i => categoryOf[i.Description ?? ""]).Where(c => c.Key != ProductCategory.Other)
                .OrderByDescending(c => c.Sum(i => i.Orders)).Select(c => (ProductCategory?)c.Key).FirstOrDefault());

        var lanes = (await current
            .GroupBy(s => new { s.OriginCode, s.DestinationCode })
            .Select(g => new
            {
                g.Key.OriginCode,
                g.Key.DestinationCode,
                Orders = g.Count(),
                Delivered = g.Count(s => s.Status == ShipmentStatus.Delivered),
                Returned = g.Count(s => Returning.Contains(s.Status)),
                Hours = g.Where(s => s.DeliveredAt != null).Average(s => (double?)(s.DeliveredAt!.Value - s.CreatedAt).TotalHours),
            })
            .OrderByDescending(l => l.Orders)
            .Take(12)
            .ToListAsync(ct))
            .Select(l => new LaneDemand(l.OriginCode, l.DestinationCode, l.Orders, l.Delivered, l.Returned, Round(l.Hours)))
            .ToList();

        // Exact amounts are grouped in SQL and put into bands here, so the bands are defined once (BasketBands).
        var amounts = await current
            .GroupBy(s => s.CodAmount)
            .Select(g => new
            {
                Amount = g.Key,
                Orders = g.Count(),
                Delivered = g.Count(s => s.Status == ShipmentStatus.Delivered),
                Returned = g.Count(s => Returning.Contains(s.Status)),
                Refused = g.Count(s => s.ReturnReason == ReturnReason.CustomerRefused),
            })
            .ToListAsync(ct);
        var baskets = Enumerable.Range(0, BasketBands.Count)
            .Select(band => amounts.Where(a => BasketBands.Of(a.Amount) == band).ToList())
            .Select((rows, band) => new BasketBand(band, rows.Sum(r => r.Orders), rows.Sum(r => r.Delivered), rows.Sum(r => r.Returned), rows.Sum(r => r.Refused)))
            .ToList();

        var loyalty = await LoyaltyAsync(current, ct);
        var orderTimes = await OrderTimesAsync(db, tenantId, from, now, ct);

        var terms = new Dictionary<string, (int Orders, Dictionary<string, int> Spellings, Dictionary<ProductCategory, int> Categories)>(StringComparer.Ordinal);
        foreach (var row in items)
        {
            foreach (var (key, display) in ProductCatalog.Terms(row.Description))
            {
                if (!terms.TryGetValue(key, out var term))
                {
                    term = (0, new Dictionary<string, int>(StringComparer.Ordinal), []);
                }

                term.Spellings[display] = term.Spellings.GetValueOrDefault(display) + row.Orders;
                var category = categoryOf[row.Description ?? ""];
                term.Categories[category] = term.Categories.GetValueOrDefault(category) + row.Orders;
                terms[key] = (term.Orders + row.Orders, term.Spellings, term.Categories);
            }
        }

        var orders = governorates.Sum(g => g.Orders);
        var previousOrders = before.Sum(g => g.Orders);
        var previousByCode = before.ToDictionary(b => b.Code, b => b.Orders);
        return new DemandInsights(
            span,
            from,
            now,
            new Metric(orders, previousOrders == 0 ? null : previousOrders),
            new Metric(
                DemandMath.Percent(governorates.Sum(g => g.Delivered), governorates.Sum(g => g.Delivered + g.Returned)) ?? 0,
                DemandMath.Percent(before.Sum(g => g.Delivered), before.Sum(g => g.Delivered + g.Returned))),
            new Metric(
                Average(governorates.Sum(g => g.CodSum), governorates.Sum(g => g.CodOrders)) ?? 0,
                Average(before.Sum(g => g.CodSum), before.Sum(g => g.CodOrders))),
            loyalty,
            governorates
                .Select(g => new GovernorateDemand(
                    g.Code, g.Orders, previousByCode.GetValueOrDefault(g.Code), g.Delivered, g.Returned, Average(g.CodSum, g.CodOrders), Round(g.Hours),
                    topFailure.TryGetValue(g.Code, out var reason) ? reason : null, topCategory.GetValueOrDefault(g.Code)))
                .OrderByDescending(g => g.Orders)
                .ToList(),
            categories,
            lanes,
            baskets,
            orderTimes,
            terms
                .OrderByDescending(t => t.Value.Orders).ThenBy(t => t.Key, StringComparer.Ordinal)
                .Take(24)
                .Select(t => new DemandTerm(
                    t.Value.Spellings.MaxBy(s => s.Value).Key,
                    t.Value.Categories.MaxBy(c => c.Value).Key,
                    t.Value.Orders))
                .ToList());
    }

    /// <summary>Customers are mobile numbers; a repeat customer ordered at least twice in the period.</summary>
    private static async Task<CustomerLoyalty> LoyaltyAsync(IQueryable<Domain.Shipment> orders, CancellationToken ct)
    {
        var byRepeat = await orders
            .Where(s => s.ConsigneePhone != null)
            .GroupBy(s => s.ConsigneePhone)
            .Select(g => new
            {
                Repeat = g.Count() > 1,
                Orders = g.Count(),
                Delivered = g.Count(s => s.Status == ShipmentStatus.Delivered),
                Returned = g.Count(s => Returning.Contains(s.Status)),
            })
            .GroupBy(c => c.Repeat)
            .Select(g => new { Repeat = g.Key, Customers = g.Count(), Orders = g.Sum(c => c.Orders), Delivered = g.Sum(c => c.Delivered), Returned = g.Sum(c => c.Returned) })
            .ToListAsync(ct);

        var once = byRepeat.FirstOrDefault(r => !r.Repeat);
        var repeat = byRepeat.FirstOrDefault(r => r.Repeat);
        return new CustomerLoyalty(
            (once?.Customers ?? 0) + (repeat?.Customers ?? 0),
            repeat?.Customers ?? 0,
            new LoyaltyOutcome(once?.Orders ?? 0, once?.Delivered ?? 0, once?.Returned ?? 0),
            new LoyaltyOutcome(repeat?.Orders ?? 0, repeat?.Delivered ?? 0, repeat?.Returned ?? 0));
    }

    /// <summary>Orders per weekday and hour in Baghdad time, every cell present (empty ones as 0).</summary>
    private static async Task<IReadOnlyList<OrderTimeCell>> OrderTimesAsync(ShipmentsDbContext db, Guid tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<TimeRow>($"""
            SELECT EXTRACT(DOW FROM s.created_at AT TIME ZONE 'Asia/Baghdad')::int AS "Day",
                   EXTRACT(HOUR FROM s.created_at AT TIME ZONE 'Asia/Baghdad')::int AS "Hour",
                   COUNT(*)::int AS "Orders"
            FROM shipment.shipments s
            WHERE s.tenant_id = {tenantId} AND s.service_type = 'Domestic' AND s.status <> 'Cancelled'
              AND s.created_at >= {from} AND s.created_at < {to}
            GROUP BY 1, 2
            """).ToListAsync(ct);

        var byCell = rows.ToDictionary(r => (r.Day, r.Hour), r => r.Orders);
        return Enumerable.Range(0, 7)
            .SelectMany(day => Enumerable.Range(0, 24).Select(hour => new OrderTimeCell(day, hour, byCell.GetValueOrDefault((day, hour)))))
            .ToList();
    }

    private static async Task OpenDataAsync(int? days, HttpResponse response, ShipmentsDbContext db, ITenantContext tenant, IConfiguration configuration, TimeProvider clock, CancellationToken ct)
    {
        var insights = await BuildAsync(db, tenant.TenantId, Span(days), clock.GetUtcNow(), ct);
        var minimum = Math.Max(1, configuration.GetValue("Insights:OpenDataMinimumOrders", DefaultOpenDataMinimumOrders));

        response.ContentType = "text/csv; charset=utf-8";
        response.Headers.ContentDisposition =
            $"attachment; filename=\"logistico-open-data-{insights.From.ToOffset(TimeSpan.FromHours(3)):yyyyMMdd}-{insights.To.ToOffset(TimeSpan.FromHours(3)):yyyyMMdd}.csv\"";
        await response.WriteAsync(OpenDataCsv(insights, minimum), Encoding.UTF8, ct);
    }

    /// <summary>
    /// The published dataset: one row per governorate, product category, trade lane, basket band and weekday, with
    /// counts, rates and averages only. Rows describing fewer than <paramref name="minimumOrders"/> orders are left out.
    /// Averages are rounded (basket to 1,000 IQD, hours to 1 decimal) so they cannot be traced back to a single order.
    /// </summary>
    internal static string OpenDataCsv(DemandInsights insights, int minimumOrders)
    {
        var start = insights.From.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = insights.To.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var csv = new StringBuilder();
        csv.AppendLine("period_start,period_end,section,key,name,orders,delivered,returned,success_rate_pct,return_rate_pct,avg_basket_iqd,avg_delivery_hours");

        void Row(string section, string key, string name, DemandOutcome outcome, decimal? basket = null, double? hours = null, bool ordersOnly = false)
        {
            if (outcome.Orders < minimumOrders)
            {
                return;
            }

            csv.AppendJoin(',',
                start, end, section, Api.Csv(key), Api.Csv(name),
                Num(outcome.Orders), ordersOnly ? "" : Num(outcome.Delivered), ordersOnly ? "" : Num(outcome.Returned),
                Num(outcome.SuccessRate), Num(outcome.ReturnRate),
                Num(basket is { } b ? Math.Round(b / 1000m, MidpointRounding.AwayFromZero) * 1000m : null),
                Num(hours is { } h ? Math.Round((decimal)h, 1) : null));
            csv.AppendLine();
        }

        foreach (var g in insights.Governorates)
        {
            Row("governorate", g.Code, IraqGovernorates.Find(g.Code)?.NameEn ?? g.Code, g, g.AvgBasketIqd, g.AvgDeliveryHours);
        }

        foreach (var c in insights.Categories)
        {
            Row("category", c.Category.ToString(), c.Category.ToString(), c, c.AvgBasketIqd);
        }

        foreach (var l in insights.Lanes)
        {
            Row("lane", $"{l.OriginCode}>{l.DestinationCode}", $"{IraqGovernorates.Find(l.OriginCode)?.NameEn ?? l.OriginCode} > {IraqGovernorates.Find(l.DestinationCode)?.NameEn ?? l.DestinationCode}", l, hours: l.AvgDeliveryHours);
        }

        foreach (var b in insights.Baskets)
        {
            var (low, high) = BasketBands.Range(b.Band);
            var name = b.Band == BasketBands.NoCod ? "prepaid (no cash on delivery)" : $"{Num(low)}-{(high is null ? "" : Num(high))}";
            Row("basket_iqd", b.Band.ToString(CultureInfo.InvariantCulture), name, b);
        }

        foreach (var day in insights.OrderTimes.GroupBy(c => c.Day))
        {
            var count = day.Sum(c => c.Orders);
            Row("weekday", day.Key.ToString(CultureInfo.InvariantCulture), ((DayOfWeek)day.Key).ToString(), new LoyaltyOutcome(count, 0, 0), ordersOnly: true);
        }

        return csv.ToString();
    }

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Num(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";

    private static decimal? Average(decimal sum, int count) => count == 0 ? null : Math.Round(sum / count, 0);

    private static double? Round(double? hours) => hours is { } h ? Math.Round(h, 1) : null;

    private sealed class TimeRow
    {
        public int Day { get; set; }
        public int Hour { get; set; }
        public int Orders { get; set; }
    }
}
