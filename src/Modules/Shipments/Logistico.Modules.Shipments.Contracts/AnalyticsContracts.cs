namespace Logistico.Modules.Shipments.Contracts;

/// <summary>
/// Delivery performance for a period (domestic orders) compared with the period just before it.
/// Rates are percentages (0–100); <see cref="Metric.Previous"/> is null when the earlier period had no data.
/// </summary>
public sealed record AnalyticsOverview(
    int Days,
    DateTimeOffset From,
    DateTimeOffset To,
    Metric Booked,
    Metric Delivered,
    Metric SuccessRate,
    Metric FirstAttemptRate,
    Metric OnTimeRate,
    Metric ReturnRate,
    Metric AvgDeliveryHours,
    Metric CodCollectedIqd,
    IReadOnlyList<DailyVolume> Daily,
    IReadOnlyList<SellerPerformance> Sellers,
    IReadOnlyList<GovernoratePerformance> Governorates,
    IReadOnlyList<CourierPerformance> Couriers,
    IReadOnlyList<ReasonCount> FailureReasons,
    Metric? AverageRating = null,
    int Ratings = 0);

public sealed record Metric(decimal Value, decimal? Previous)
{
    /// <summary>Change against the previous period, in percent of the previous value (null when there is nothing to compare).</summary>
    public decimal? ChangePercent => Previous is { } p && p != 0 ? Math.Round((Value - p) * 100 / Math.Abs(p), 1) : null;

    /// <summary>Change in points, for values that are already percentages.</summary>
    public decimal? ChangePoints => Previous is { } p ? Math.Round(Value - p, 1) : null;
}

/// <summary>One day (Baghdad time) of the period.</summary>
public sealed record DailyVolume(DateOnly Day, int Booked, int Delivered, int Failed, int Returned);

public sealed record SellerPerformance(Guid MerchantId, string Name, int Orders, int Delivered, int Returned, decimal CodCollected)
{
    /// <summary>Delivered out of the orders that finished (delivered or returned), in percent.</summary>
    public decimal? SuccessRate => Delivered + Returned == 0 ? null : Math.Round(Delivered * 100m / (Delivered + Returned), 1);
}

public sealed record GovernoratePerformance(string Code, int Orders, int Delivered, int Returned, double? AvgDeliveryHours)
{
    public decimal? SuccessRate => Delivered + Returned == 0 ? null : Math.Round(Delivered * 100m / (Delivered + Returned), 1);
}

public sealed record CourierPerformance(string Name, int Delivered, int FailedAttempts, decimal CodCollected, decimal? AverageRating = null)
{
    public decimal? SuccessRate => Delivered + FailedAttempts == 0 ? null : Math.Round(Delivered * 100m / (Delivered + FailedAttempts), 1);
}

public sealed record ReasonCount(DeliveryFailureReason Reason, int Count);
