using System.Text.Json.Serialization;

namespace Logistico.Modules.Shipments.Contracts;

/// <summary>What a parcel contains, recognised from its item description (<see cref="Rules.ProductCatalog"/>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProductCategory>))]
public enum ProductCategory
{
    Fashion,
    ShoesAndBags,
    Beauty,
    Electronics,
    Accessories,
    KidsAndToys,
    HomeAndKitchen,
    Books,
    Food,
    Health,
    Other,
}

/// <summary>
/// What Iraqi customers order, where, when and how it ends, for domestic orders booked in a period (compared with the
/// period before). Delivery outcomes are for the period's own orders: success = delivered ÷ (delivered + returned).
/// </summary>
public sealed record DemandInsights(
    int Days,
    DateTimeOffset From,
    DateTimeOffset To,
    Metric Orders,
    Metric SuccessRate,
    Metric AvgBasketIqd,
    CustomerLoyalty Customers,
    IReadOnlyList<GovernorateDemand> Governorates,
    IReadOnlyList<CategoryDemand> Categories,
    IReadOnlyList<LaneDemand> Lanes,
    IReadOnlyList<BasketBand> Baskets,
    IReadOnlyList<OrderTimeCell> OrderTimes,
    IReadOnlyList<DemandTerm> Terms);

/// <summary>Orders of a group and how they ended so far (orders still on their way count in neither outcome).</summary>
public abstract record DemandOutcome(int Orders, int Delivered, int Returned)
{
    public decimal? SuccessRate => DemandMath.Percent(Delivered, Delivered + Returned);

    public decimal? ReturnRate => DemandMath.Percent(Returned, Delivered + Returned);
}

/// <param name="Customers">Distinct mobile numbers that ordered in the period.</param>
/// <param name="RepeatCustomers">Of those, numbers with two or more orders in the period.</param>
/// <param name="FirstTime">Orders of customers with a single order in the period.</param>
/// <param name="Repeat">Orders of repeat customers.</param>
public sealed record CustomerLoyalty(int Customers, int RepeatCustomers, LoyaltyOutcome FirstTime, LoyaltyOutcome Repeat)
{
    public decimal? RepeatCustomerShare => DemandMath.Percent(RepeatCustomers, Customers);

    public decimal? RepeatOrderShare => DemandMath.Percent(Repeat.Orders, FirstTime.Orders + Repeat.Orders);
}

public sealed record LoyaltyOutcome(int Orders, int Delivered, int Returned) : DemandOutcome(Orders, Delivered, Returned);

public sealed record GovernorateDemand(
    string Code,
    int Orders,
    int PreviousOrders,
    int Delivered,
    int Returned,
    decimal? AvgBasketIqd,
    double? AvgDeliveryHours,
    DeliveryFailureReason? TopFailureReason,
    ProductCategory? TopCategory)
    : DemandOutcome(Orders, Delivered, Returned)
{
    /// <summary>Change in orders against the previous period, in percent (null when it had none).</summary>
    public decimal? Growth => PreviousOrders == 0 ? null : Math.Round((Orders - PreviousOrders) * 100m / PreviousOrders, 1);
}

public sealed record CategoryDemand(ProductCategory Category, int Orders, int Delivered, int Returned, decimal? AvgBasketIqd)
    : DemandOutcome(Orders, Delivered, Returned);

/// <summary>Seller's governorate → customer's governorate.</summary>
public sealed record LaneDemand(string OriginCode, string DestinationCode, int Orders, int Delivered, int Returned, double? AvgDeliveryHours)
    : DemandOutcome(Orders, Delivered, Returned);

/// <summary>Orders by cash-on-delivery amount (<see cref="Rules.BasketBands"/>); refusals are returns the customer refused.</summary>
public sealed record BasketBand(int Band, int Orders, int Delivered, int Returned, int Refused)
    : DemandOutcome(Orders, Delivered, Returned)
{
    public decimal? RefusalRate => DemandMath.Percent(Refused, Delivered + Returned);
}

/// <summary>Orders booked on a weekday (0 = Sunday) at an hour (0–23), Baghdad time.</summary>
public sealed record OrderTimeCell(int Day, int Hour, int Orders);

/// <summary>A frequent word in item descriptions ("فستان", "سماعات"…), counted once per order.</summary>
public sealed record DemandTerm(string Term, ProductCategory Category, int Orders);

public static class DemandMath
{
    /// <summary>Part of whole in percent with one decimal; null when the whole is zero.</summary>
    public static decimal? Percent(int part, int whole) => whole == 0 ? null : Math.Round(part * 100m / whole, 1);
}
