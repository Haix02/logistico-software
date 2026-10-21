using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Endpoints;

/// <summary>Delivery performance for the dashboard: KPIs against the previous period, daily volume and league tables.</summary>
internal static class AnalyticsEndpoints
{
    private static readonly int[] AllowedDays = [1, 7, 30, 90];

    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/" + ShipmentsApiRoutes.AnalyticsOverview, OverviewAsync)
            .WithTags("Analytics")
            .WithSummary("Domestic delivery performance for the last 1, 7, 30 or 90 days, compared with the period before.")
            .RequireAuthorization(StaffPolicies.Staff);
        return app;
    }

    private static async Task<Ok<AnalyticsOverview>> OverviewAsync(int? days, ShipmentsDbContext db, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var span = AllowedDays.Contains(days ?? 7) ? days ?? 7 : 7;
        var now = clock.GetUtcNow();
        var from = Api.StartOfBaghdadDay(now).AddDays(-(span - 1));
        var to = now;
        var previousFrom = from.AddDays(-span);
        var domestic = db.Shipments.AsNoTracking().Where(s => s.ServiceType == ServiceType.Domestic);
        var returning = new[] { ShipmentStatus.ReturnInitiated, ShipmentStatus.ReturnInTransit, ShipmentStatus.ReturnAtHub, ShipmentStatus.Returned };

        var booked = await domestic.Where(s => s.CreatedAt >= previousFrom && s.CreatedAt < to)
            .GroupBy(s => s.CreatedAt >= from).Select(g => new { Current = g.Key, Count = g.Count() }).ToListAsync(ct);

        var delivered = await domestic.Where(s => s.DeliveredAt >= previousFrom && s.DeliveredAt < to)
            .GroupBy(s => s.DeliveredAt >= from)
            .Select(g => new
            {
                Current = g.Key,
                Count = g.Count(),
                FirstAttempt = g.Count(s => s.DeliveryAttempts <= 1),
                WithPromise = g.Count(s => s.PromisedAt != null),
                OnTime = g.Count(s => s.PromisedAt != null && s.DeliveredAt <= s.PromisedAt),
                Hours = g.Average(s => (double?)(s.DeliveredAt!.Value - s.CreatedAt).TotalHours),
            })
            .ToListAsync(ct);

        var returns = await domestic.Where(s => s.ReturnRequestedAt >= previousFrom && s.ReturnRequestedAt < to)
            .GroupBy(s => s.ReturnRequestedAt >= from).Select(g => new { Current = g.Key, Count = g.Count() }).ToListAsync(ct);

        var cod = await domestic.Where(s => s.CodCollectedAt >= previousFrom && s.CodCollectedAt < to && s.CodCurrency == "IQD")
            .GroupBy(s => s.CodCollectedAt >= from).Select(g => new { Current = g.Key, Amount = g.Sum(s => s.CodAmount ?? 0) }).ToListAsync(ct);

        int CountOf<T>(IEnumerable<T> rows, bool current, Func<T, bool> isCurrent, Func<T, int> value) => rows.Where(r => isCurrent(r) == current).Sum(value);

        Metric Rate(Func<bool, (int Part, int Whole)> pick)
        {
            static decimal? Percent((int Part, int Whole) x) => x.Whole == 0 ? null : Math.Round(x.Part * 100m / x.Whole, 1);
            return new Metric(Percent(pick(true)) ?? 0, Percent(pick(false)));
        }

        (int Delivered, int Returned) Finished(bool current) =>
            (CountOf(delivered, current, r => r.Current, r => r.Count), CountOf(returns, current, r => r.Current, r => r.Count));

        Metric Count<T>(IEnumerable<T> rows, Func<T, bool> isCurrent, Func<T, int> value)
        {
            var list = rows.ToList();
            var previous = CountOf(list, false, isCurrent, value);
            return new Metric(CountOf(list, true, isCurrent, value), list.Any(r => !isCurrent(r)) ? previous : null);
        }

        var hoursNow = delivered.FirstOrDefault(r => r.Current)?.Hours;
        var hoursBefore = delivered.FirstOrDefault(r => !r.Current)?.Hours;

        var daily = await DailyAsync(db, tenant.TenantId, from, to, span, ct);

        // Grouped and sorted in SQL as anonymous rows (EF cannot sort on a record constructor), mapped afterwards.
        var sellers = (await domestic.Where(s => s.CreatedAt >= from && s.MerchantId != null)
            .GroupBy(s => new { s.MerchantId, s.MerchantName })
            .Select(g => new
            {
                Id = g.Key.MerchantId!.Value,
                g.Key.MerchantName,
                Orders = g.Count(),
                Delivered = g.Count(s => s.Status == ShipmentStatus.Delivered),
                Returned = g.Count(s => returning.Contains(s.Status)),
                Cod = g.Sum(s => s.CodCollectedAt != null && s.CodCurrency == "IQD" ? s.CodAmount ?? 0 : 0),
            })
            .OrderByDescending(s => s.Orders).Take(8)
            .ToListAsync(ct))
            .Select(s => new SellerPerformance(s.Id, s.MerchantName ?? "—", s.Orders, s.Delivered, s.Returned, s.Cod))
            .ToList();

        var governorates = (await domestic.Where(s => s.CreatedAt >= from)
            .GroupBy(s => s.DestinationCode)
            .Select(g => new
            {
                Code = g.Key,
                Orders = g.Count(),
                Delivered = g.Count(s => s.Status == ShipmentStatus.Delivered),
                Returned = g.Count(s => returning.Contains(s.Status)),
                Hours = g.Where(s => s.DeliveredAt != null).Average(s => (double?)(s.DeliveredAt!.Value - s.CreatedAt).TotalHours),
            })
            .OrderByDescending(g => g.Orders)
            .ToListAsync(ct))
            .Select(g => new GovernoratePerformance(g.Code, g.Orders, g.Delivered, g.Returned, g.Hours))
            .ToList();

        var courierDeliveries = await domestic.Where(s => s.DeliveredAt >= from && s.CourierName != null)
            .GroupBy(s => s.CourierName!)
            .Select(g => new
            {
                Name = g.Key,
                Delivered = g.Count(),
                Cod = g.Sum(s => s.CodCurrency == "IQD" ? s.CodAmount ?? 0 : 0),
                Rating = g.Average(s => (double?)s.Rating),
            })
            .ToListAsync(ct);

        var ratings = await domestic.Where(s => s.RatedAt >= previousFrom && s.RatedAt < to && s.Rating != null)
            .GroupBy(s => s.RatedAt >= from)
            .Select(g => new { Current = g.Key, Count = g.Count(), Average = g.Average(s => (double)s.Rating!.Value) })
            .ToListAsync(ct);
        var failures = db.ShipmentEvents.AsNoTracking()
            .Where(e => e.Status == ShipmentStatus.DeliveryFailed && e.OccurredAt >= from && e.OccurredAt < to)
            .Where(e => domestic.Any(s => s.Id == e.ShipmentId));
        var courierFailures = await failures.Where(e => e.CourierName != null)
            .GroupBy(e => e.CourierName!).Select(g => new { Name = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Name, x => x.Count, ct);
        var couriers = courierDeliveries
            .Select(c => new CourierPerformance(c.Name, c.Delivered, courierFailures.GetValueOrDefault(c.Name), c.Cod, c.Rating is { } r ? Math.Round((decimal)r, 1) : null))
            .Concat(courierFailures.Where(f => courierDeliveries.All(c => c.Name != f.Key)).Select(f => new CourierPerformance(f.Key, 0, f.Value, 0)))
            .OrderByDescending(c => c.Delivered).ThenBy(c => c.FailedAttempts).Take(10)
            .ToList();
        var reasons = (await failures.Where(e => e.FailureReason != null)
            .GroupBy(e => e.FailureReason!.Value).Select(g => new { Reason = g.Key, Count = g.Count() })
            .ToListAsync(ct))
            .OrderByDescending(r => r.Count)
            .Select(r => new ReasonCount(r.Reason, r.Count))
            .ToList();

        return TypedResults.Ok(new AnalyticsOverview(
            span,
            from,
            to,
            Count(booked, r => r.Current, r => r.Count),
            Count(delivered, r => r.Current, r => r.Count),
            Rate(current => (Finished(current).Delivered, Finished(current).Delivered + Finished(current).Returned)),
            Rate(current => (CountOf(delivered, current, r => r.Current, r => r.FirstAttempt), CountOf(delivered, current, r => r.Current, r => r.Count))),
            Rate(current => (CountOf(delivered, current, r => r.Current, r => r.OnTime), CountOf(delivered, current, r => r.Current, r => r.WithPromise))),
            Rate(current => (Finished(current).Returned, Finished(current).Delivered + Finished(current).Returned)),
            new Metric(Math.Round((decimal)(hoursNow ?? 0), 1), hoursBefore is { } h ? Math.Round((decimal)h, 1) : null),
            new Metric(cod.Where(c => c.Current).Sum(c => c.Amount), cod.Any(c => !c.Current) ? cod.Where(c => !c.Current).Sum(c => c.Amount) : null),
            daily,
            sellers,
            governorates,
            couriers,
            reasons,
            new Metric(
                Math.Round((decimal)(ratings.FirstOrDefault(r => r.Current)?.Average ?? 0), 1),
                ratings.FirstOrDefault(r => !r.Current) is { } before ? Math.Round((decimal)before.Average, 1) : null),
            ratings.FirstOrDefault(r => r.Current)?.Count ?? 0));
    }

    /// <summary>Booked, delivered, failed attempts and returns per Baghdad day, every day of the period present.</summary>
    private static async Task<IReadOnlyList<DailyVolume>> DailyAsync(ShipmentsDbContext db, Guid tenantId, DateTimeOffset from, DateTimeOffset to, int span, CancellationToken ct)
    {
        // One pass in PostgreSQL; days are cut in Baghdad time, not UTC.
        var rows = await db.Database.SqlQuery<DayRow>($"""
            SELECT x.day AS "Day", SUM(x.booked)::int AS "Booked", SUM(x.delivered)::int AS "Delivered", SUM(x.failed)::int AS "Failed", SUM(x.returned)::int AS "Returned"
            FROM (
                SELECT (s.created_at AT TIME ZONE 'Asia/Baghdad')::date AS day, 1 AS booked, 0 AS delivered, 0 AS failed, 0 AS returned
                FROM shipment.shipments s
                WHERE s.tenant_id = {tenantId} AND s.service_type = 'Domestic' AND s.created_at >= {from} AND s.created_at < {to}
                UNION ALL
                SELECT (s.delivered_at AT TIME ZONE 'Asia/Baghdad')::date, 0, 1, 0, 0
                FROM shipment.shipments s
                WHERE s.tenant_id = {tenantId} AND s.service_type = 'Domestic' AND s.delivered_at >= {from} AND s.delivered_at < {to}
                UNION ALL
                SELECT (e.occurred_at AT TIME ZONE 'Asia/Baghdad')::date, 0, 0, 1, 0
                FROM shipment.shipment_events e
                JOIN shipment.shipments s ON s.id = e.shipment_id AND s.service_type = 'Domestic'
                WHERE e.tenant_id = {tenantId} AND e.status = 'DeliveryFailed' AND e.occurred_at >= {from} AND e.occurred_at < {to}
                UNION ALL
                SELECT (s.return_requested_at AT TIME ZONE 'Asia/Baghdad')::date, 0, 0, 0, 1
                FROM shipment.shipments s
                WHERE s.tenant_id = {tenantId} AND s.service_type = 'Domestic' AND s.return_requested_at >= {from} AND s.return_requested_at < {to}
            ) x
            GROUP BY x.day
            """).ToListAsync(ct);

        var byDay = rows.ToDictionary(r => r.Day);
        var first = DateOnly.FromDateTime(from.ToOffset(TimeSpan.FromHours(3)).DateTime);
        return Enumerable.Range(0, span)
            .Select(i => first.AddDays(i))
            .Select(day => byDay.TryGetValue(day, out var r) ? new DailyVolume(day, r.Booked, r.Delivered, r.Failed, r.Returned) : new DailyVolume(day, 0, 0, 0, 0))
            .ToList();
    }

    private sealed class DayRow
    {
        public DateOnly Day { get; set; }
        public int Booked { get; set; }
        public int Delivered { get; set; }
        public int Failed { get; set; }
        public int Returned { get; set; }
    }
}
