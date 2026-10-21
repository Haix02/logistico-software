namespace Logistico.Modules.Shipments.Contracts.Rules;

/// <summary>Which requests make sense for an order in its current state (shared by the tracking page and the API).</summary>
public static class RequestRules
{
    /// <summary>Customers may ask for a return up to this many days after delivery.</summary>
    public const int ReturnWindowDays = 7;

    public static IReadOnlyList<RequestType> Allowed(ShipmentStatus status, DateTimeOffset? deliveredAt, DateTimeOffset now) => status switch
    {
        ShipmentStatus.Draft or ShipmentStatus.Booked or ShipmentStatus.PickedUp or ShipmentStatus.AtWarehouse
            or ShipmentStatus.ReadyForDispatch or ShipmentStatus.InTransitToHub or ShipmentStatus.DeliveryFailed
            or ShipmentStatus.AtDestinationWarehouse =>
            [RequestType.Reschedule, RequestType.ChangeAddress, RequestType.ChangePhone, RequestType.Cancel, RequestType.Complaint],
        ShipmentStatus.OutForDelivery => [RequestType.Complaint],
        ShipmentStatus.Delivered when deliveredAt is { } at && now - at <= TimeSpan.FromDays(ReturnWindowDays) =>
            [RequestType.Return, RequestType.Complaint],
        _ => [RequestType.Complaint],
    };

    public static bool IsAllowed(RequestType type, ShipmentStatus status, DateTimeOffset? deliveredAt, DateTimeOffset now) =>
        Allowed(status, deliveredAt, now).Contains(type);
}
