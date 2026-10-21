using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Endpoints;

namespace Logistico.Modules.Shipments.Tests;

public sealed class OpenDataTests
{
    private static readonly DateTimeOffset From = new(2026, 7, 21, 21, 0, 0, TimeSpan.Zero); // 22 Jul, Baghdad
    private static readonly DateTimeOffset To = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Rows_carry_only_counts_rounded_averages_and_rates()
    {
        var csv = OpenData(minimum: 10);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("period_start,period_end,section,key,name,orders,delivered,returned,success_rate_pct,return_rate_pct,avg_basket_iqd,avg_delivery_hours", lines[0]);
        // Basket rounded to 1,000 IQD and hours to one decimal, so no single order can be recognised.
        Assert.Contains("2026-07-22,2026-10-19,governorate,IQ-BG,Baghdad,120,90,10,90,10,74000,31.6", lines);
        Assert.Contains("2026-07-22,2026-10-19,category,Fashion,Fashion,40,30,5,85.7,14.3,61000,", lines);
        Assert.Contains("2026-07-22,2026-10-19,lane,IQ-BG>IQ-BA,Baghdad > Basra,25,20,5,80,20,,52", lines);
        Assert.Contains("2026-07-22,2026-10-19,basket_iqd,5,200000-,12,6,6,50,50,,", lines);
        Assert.Contains("2026-07-22,2026-10-19,weekday,4,Thursday,15,,,,,,", lines);
    }

    [Fact]
    public void Groups_below_the_minimum_are_left_out()
    {
        var csv = OpenData(minimum: 10);

        Assert.DoesNotContain("IQ-MU", csv, StringComparison.Ordinal); // 3 orders in Muthanna
        Assert.DoesNotContain("Books", csv, StringComparison.Ordinal); // 2 book orders
        Assert.DoesNotContain(",weekday,5,", csv, StringComparison.Ordinal); // Friday: 4 orders

        Assert.Contains("IQ-MU", OpenData(minimum: 1), StringComparison.Ordinal);
    }

    private static string OpenData(int minimum)
    {
        IReadOnlyList<OrderTimeCell> times =
        [
            .. Enumerable.Range(0, 7).SelectMany(day => Enumerable.Range(0, 24).Select(hour => new OrderTimeCell(day, hour, 0)))
                .Where(c => !(c.Day == 4 && c.Hour == 21) && !(c.Day == 5 && c.Hour == 20)),
            new(4, 21, 15),
            new(5, 20, 4),
        ];
        var insights = new DemandInsights(
            90,
            From,
            To,
            new Metric(123, null),
            new Metric(90, null),
            new Metric(74_000, null),
            new CustomerLoyalty(100, 20, new LoyaltyOutcome(80, 70, 5), new LoyaltyOutcome(43, 20, 5)),
            [
                new GovernorateDemand("IQ-BG", 120, 100, 90, 10, 74_321m, 31.64, DeliveryFailureReason.NoAnswer, ProductCategory.Fashion),
                new GovernorateDemand("IQ-MU", 3, 0, 1, 1, 50_000m, 70, null, null),
            ],
            [
                new CategoryDemand(ProductCategory.Fashion, 40, 30, 5, 60_600m),
                new CategoryDemand(ProductCategory.Books, 2, 2, 0, 15_000m),
            ],
            [new LaneDemand("IQ-BG", "IQ-BA", 25, 20, 5, 52.04)],
            [new BasketBand(5, 12, 6, 6, 6)],
            times,
            [new DemandTerm("فستان", ProductCategory.Fashion, 40)]);
        return InsightsEndpoints.OpenDataCsv(insights, minimum);
    }
}
