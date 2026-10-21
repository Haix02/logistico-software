using System.Globalization;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Web.Client.Ui;
using Microsoft.Extensions.Localization;

namespace Logistico.Web.Client.Pages.Insights;

/// <summary>A sentence worth reading before the charts, computed from the period's numbers.</summary>
internal sealed record Finding(string Icon, Intent Intent, string Text);

/// <summary>
/// Turns demand numbers into plain findings: the leading category, the fastest-growing governorate, where deliveries fail
/// most, which basket sizes come back, when customers order and how loyal they are. Groups with fewer than
/// <see cref="MinimumOrders"/> orders are never called out, so one bad week in a small governorate is not "news".
/// </summary>
internal static class InsightFindings
{
    public const int MinimumOrders = 10;

    public static IReadOnlyList<Finding> From(DemandInsights d, IStringLocalizer L)
    {
        var findings = new List<Finding>();
        var total = (int)d.Orders.Value;
        if (total < MinimumOrders)
        {
            return findings;
        }

        if (d.Categories.FirstOrDefault(c => c.Category != ProductCategory.Other) is { } top)
        {
            findings.Add(new("shopping-cart", Intent.Primary,
                Format(L["Finding_TopCategory"], ShipmentTexts.Of(top.Category), Fmt.Percent(DemandMath.Percent(top.Orders, total)))));
        }

        var growing = d.Governorates.Where(g => g.Orders >= MinimumOrders && g.PreviousOrders >= MinimumOrders && g.Growth is >= 10).MaxBy(g => g.Growth);
        if (growing is not null)
        {
            findings.Add(new("trending-up", Intent.Success, Format(L["Finding_Growth"], Fmt.Place(growing.Code), Fmt.Percent(growing.Growth))));
        }

        var finished = d.Governorates.Where(g => g.Delivered + g.Returned >= MinimumOrders).ToList();
        if (finished.Count >= 2 && finished.MinBy(g => g.SuccessRate) is { SuccessRate: { } weakRate } weak && weakRate <= d.SuccessRate.Value - 3)
        {
            findings.Add(new("warning-sign", Intent.Warning, weak.TopFailureReason is { } reason
                ? Format(L["Finding_WeakGovernorate"], Fmt.Place(weak.Code), Fmt.Percent(weakRate), ShipmentTexts.Of(reason))
                : Format(L["Finding_WeakGovernorateNoReason"], Fmt.Place(weak.Code), Fmt.Percent(weakRate))));
        }

        var overallReturn = DemandMath.Percent(d.Baskets.Sum(b => b.Returned), d.Baskets.Sum(b => b.Delivered + b.Returned));
        var riskyBasket = d.Baskets.Where(b => b.Delivered + b.Returned >= MinimumOrders).MaxBy(b => b.ReturnRate);
        if (overallReturn is > 0 && riskyBasket?.ReturnRate is { } basketReturn && basketReturn >= overallReturn * 1.5m)
        {
            findings.Add(new("dollar", Intent.Danger, Format(L["Finding_BasketRisk"],
                BasketLabel(riskyBasket.Band, L), (basketReturn / overallReturn.Value).ToString("0.#", CultureInfo.CurrentCulture),
                Fmt.Percent(basketReturn), Fmt.Percent(overallReturn))));
        }

        var riskyCategory = d.Categories.Where(c => c.Category != ProductCategory.Other && c.Delivered + c.Returned >= MinimumOrders).MaxBy(c => c.ReturnRate);
        if (overallReturn is > 0 && riskyCategory?.ReturnRate is { } categoryReturn && categoryReturn >= overallReturn * 1.3m)
        {
            findings.Add(new("undo", Intent.Warning, Format(L["Finding_RiskyCategory"], ShipmentTexts.Of(riskyCategory.Category), Fmt.Percent(categoryReturn))));
        }

        if (d.OrderTimes.MaxBy(c => c.Orders) is { Orders: > 0 } peak)
        {
            findings.Add(new("time", Intent.None, Format(L["Finding_PeakTime"],
                CultureInfo.CurrentCulture.DateTimeFormat.GetDayName((DayOfWeek)peak.Day), $"{peak.Hour:00}:00")));
        }

        var loyalty = d.Customers;
        if (loyalty.Repeat.Delivered + loyalty.Repeat.Returned >= MinimumOrders && loyalty.FirstTime.Delivered + loyalty.FirstTime.Returned >= MinimumOrders)
        {
            findings.Add(new("repeat", Intent.Primary, Format(L["Finding_Loyalty"],
                Fmt.Percent(loyalty.RepeatOrderShare), Fmt.Percent(loyalty.Repeat.SuccessRate), Fmt.Percent(loyalty.FirstTime.SuccessRate))));
        }

        return findings;
    }

    /// <summary>"Prepaid", "Under 25,000 IQD", "25,000 – 50,000 IQD", "200,000 IQD or more".</summary>
    public static string BasketLabel(int band, IStringLocalizer L)
    {
        var (low, high) = BasketBands.Range(band);
        return band == BasketBands.NoCod ? L["Basket_NoCod"]
            : low is 0 ? Format(L["Basket_Under"], Fmt.Money(high, "IQD"))
            : high is null ? Format(L["Basket_Over"], Fmt.Money(low, "IQD"))
            : Format(L["Basket_Between"], Fmt.Number(low!.Value, 0), Fmt.Money(high, "IQD"));
    }

    private static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
