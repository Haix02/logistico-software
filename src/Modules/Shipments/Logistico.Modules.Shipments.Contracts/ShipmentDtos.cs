namespace Logistico.Modules.Shipments.Contracts;

public sealed record MoneyTotal(string Currency, decimal Amount);

public sealed record ShipmentListItem(
    Guid Id,
    string TrackingNumber,
    ServiceType ServiceType,
    TransportMode Mode,
    ShipmentStatus Status,
    string? MerchantName,
    string ShipperName,
    string ConsigneeName,
    string? ConsigneePhone,
    string OriginCode,
    string DestinationCode,
    string? DestinationCity,
    int Pieces,
    decimal ChargeableWeightKg,
    decimal? CodAmount,
    string? CodCurrency,
    string? WarehouseBin,
    string? CourierName,
    int DeliveryAttempts,
    string? ExternalReference,
    DateTimeOffset? EtaAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset CreatedAt,
    string? OriginHubCode,
    string DestinationHubCode,
    string? CurrentHubCode,
    DateTimeOffset? RescheduledTo,
    ReturnReason? ReturnReason,
    DateTimeOffset LastStatusAt,
    ConfirmationStatus Confirmation = ConfirmationStatus.NotRequired);

public sealed record ShipmentListResult(
    IReadOnlyList<ShipmentListItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<MoneyTotal> CodTotals);

public sealed record ShipmentEventItem(
    Guid Id,
    ShipmentStatus Status,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    string? Location,
    string? WarehouseBin,
    string? CourierName,
    DeliveryFailureReason? FailureReason,
    string? Notes,
    string Source,
    string? RecordedBy);

public sealed record ShipmentDetails(
    Guid Id,
    string TrackingNumber,
    ServiceType ServiceType,
    string? HouseBillNumber,
    TransportMode Mode,
    ShipmentStatus Status,
    string? Incoterm,
    Guid? MerchantId,
    string? MerchantName,
    string? ExternalReference,
    string ShipperName,
    string ConsigneeName,
    string? ConsigneePhone,
    string OriginCode,
    string DestinationCode,
    string? DestinationCity,
    string? DeliveryAddress,
    string? Landmark,
    string? ItemsDescription,
    bool PickupRequired,
    int Pieces,
    decimal GrossWeightKg,
    decimal VolumeCbm,
    decimal ChargeableWeightKg,
    decimal? DeclaredValue,
    string? DeclaredCurrency,
    decimal? CodAmount,
    string? CodCurrency,
    DateTimeOffset? CodCollectedAt,
    decimal? DeliveryFee,
    string? WarehouseBin,
    string? CourierName,
    int DeliveryAttempts,
    DeliveryFailureReason? LastFailureReason,
    DateTimeOffset? PromisedAt,
    DateTimeOffset? EtaAt,
    DateTimeOffset? DeliveredAt,
    string? Notes,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    uint Version,
    IReadOnlyList<ShipmentEventItem> Events,
    string? OriginHubCode,
    string DestinationHubCode,
    string? CurrentHubCode,
    Guid? TransferId,
    ReturnReason? ReturnReason,
    DateTimeOffset? ReturnRequestedAt,
    DateTimeOffset? ReturnedAt,
    string? ReturnManifestNumber,
    DateTimeOffset? RescheduledTo,
    DateTimeOffset LastStatusAt,
    ConfirmationStatus Confirmation = ConfirmationStatus.NotRequired,
    DateTimeOffset? ConfirmationRequestedAt = null,
    int ConfirmationAttempts = 0,
    string? ConfirmationBy = null,
    int? Rating = null,
    string? RatingComment = null,
    DateTimeOffset? CodSettledAt = null,
    string? CodSettlementNumber = null,
    decimal? ReturnFee = null)
{
    /// <summary>Where the parcel must go next: the destination hub, or the seller's hub when it is being returned.</summary>
    public string TargetHubCode => Rules.ShipmentLifecycle.IsReturning(Status) || Status == ShipmentStatus.Returned
        ? OriginHubCode ?? DestinationHubCode
        : DestinationHubCode;

    /// <summary>True when the parcel crosses Iraq between two different hubs.</summary>
    public bool CrossesHubs => OriginHubCode is not null && OriginHubCode != DestinationHubCode;
}

public sealed record CreateShipmentResponse(Guid Id, string TrackingNumber);

public sealed record BulkBookOrdersResponse(int BookedCount, IReadOnlyList<CreateShipmentResponse> Orders);

public sealed record ShipmentStatusCount(ServiceType ServiceType, ShipmentStatus Status, int Count);

/// <summary>Operational summary for dashboards and seller pages.</summary>
public sealed record ShipmentsSummary(
    IReadOnlyList<ShipmentStatusCount> Counts,
    int Total,
    int InWarehouse,
    int OutForDelivery,
    int FailedAttempts,
    int DeliveredToday,
    int BookedToday,
    IReadOnlyList<MoneyTotal> CodToCollect,
    IReadOnlyList<MoneyTotal> CodCollected);

public sealed record PublicTrackingEvent(ShipmentStatus Status, DateTimeOffset OccurredAt, string? Location, DeliveryFailureReason? FailureReason, string? HubCode = null);

/// <summary>
/// What anyone with the tracking number may see. Seller identity, customer data, COD value, delivery area and the
/// parcel's exact hub route are omitted because a label or tracking link can be shared beyond the recipient.
/// </summary>
public sealed record PublicTrackingView(
    string TrackingNumber,
    ServiceType ServiceType,
    TransportMode Mode,
    ShipmentStatus Status,
    string? MerchantName,
    string OriginCode,
    string DestinationCode,
    string? DestinationCity,
    decimal? AmountToPay,
    string? AmountCurrency,
    int DeliveryAttempts,
    DateTimeOffset? PromisedAt,
    DateTimeOffset? EtaAt,
    DateTimeOffset? DeliveredAt,
    IReadOnlyList<PublicTrackingEvent> Events,
    string? OriginHubCode,
    string? DestinationHubCode,
    string? CurrentHubCode,
    DateTimeOffset? RescheduledTo,
    IReadOnlyList<RequestType> AllowedRequests,
    int OpenRequests,
    IReadOnlyList<Rules.HubInfo> RouteHubs,
    bool AwaitingConfirmation = false,
    bool CanRate = false,
    int? Rating = null)
{
    public bool CrossesHubs => OriginHubCode is not null && DestinationHubCode is not null && OriginHubCode != DestinationHubCode;

    /// <summary>A hub on this parcel's route (the page has no access to the company's full network).</summary>
    public Rules.HubInfo? Hub(string? code) => RouteHubs.FirstOrDefault(h => string.Equals(h.Code, code, StringComparison.OrdinalIgnoreCase));
}
