using System.Text.Json.Serialization;

namespace Logistico.Modules.Shipments.Contracts;

/// <summary>
/// The seller portal's home: the shop's orders in numbers and its money, counted exactly as the company's Finance page
/// counts it (owed = delivered and collected but not yet paid out, minus delivery fees).
/// </summary>
public sealed record SellerHome(
    Guid MerchantId,
    string MerchantName,
    string Governorate,
    int ActiveOrders,
    int ReturningOrders,
    int DeliveredLast30Days,
    int ReturnedLast30Days,
    decimal OwedIqd,
    int OwedOrders,
    decimal OnTheWayIqd,
    int OnTheWayOrders,
    decimal PaidIqd,
    IReadOnlyList<CodSettlementItem> Payouts);

/// <summary>One of the shop's orders, as the shop sees it.</summary>
public sealed record SellerOrder(
    Guid Id,
    string TrackingNumber,
    ShipmentStatus Status,
    string ConsigneeName,
    string? ConsigneePhone,
    string DestinationCode,
    string? DestinationCity,
    string? ItemsDescription,
    decimal? CodAmount,
    decimal? DeliveryFee,
    int DeliveryAttempts,
    DeliveryFailureReason? LastFailureReason,
    ReturnReason? ReturnReason,
    string? ExternalReference,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastStatusAt,
    bool PaidOut,
    decimal? ReturnFee = null);

public sealed record SellerOrderList(IReadOnlyList<SellerOrder> Items, int Total, int Page, int PageSize);

/// <summary>The groups a seller filters their orders by.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SellerOrderView>))]
public enum SellerOrderView
{
    All,
    Active,
    Delivered,
    Returns,
}

/// <summary>Search and paging for the seller's order list (always only the shop's own orders).</summary>
public sealed class SellerOrderQuery
{
    public string? Q { get; set; }
    public SellerOrderView? View { get; set; }
    public int? Page { get; set; }

    public const int PageSize = 25;
}
