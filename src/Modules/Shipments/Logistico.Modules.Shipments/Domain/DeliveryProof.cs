using Logistico.Modules.Shipments.Contracts;

namespace Logistico.Modules.Shipments.Domain;

/// <summary>
/// What the courier's phone recorded at one delivery attempt: for a delivery, who took the parcel and an optional
/// signature; for a failed attempt, the reason; for both, where the phone was. Kept as evidence for disputes ("the
/// courier never came", "nobody signed"), so it is never changed.
/// </summary>
internal sealed class DeliveryProof
{
    private DeliveryProof()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ShipmentId { get; private set; }
    public bool Delivered { get; private set; }
    public DeliveryFailureReason? FailureReason { get; private set; }
    public string CourierLabel { get; private set; } = "";
    public DeliveryReceiver? ReceivedBy { get; private set; }
    public string? ReceiverName { get; private set; }

    /// <summary>SVG path data in a 300 × 150 box (see <see cref="CourierDeliveryRequest.IsPathData"/>).</summary>
    public string? Signature { get; private set; }

    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public double? AccuracyMeters { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public static DeliveryProof ForDelivery(Guid tenantId, Guid shipmentId, string courier, CourierDeliveryRequest request, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        TenantId = tenantId,
        ShipmentId = shipmentId,
        Delivered = true,
        CourierLabel = courier,
        ReceivedBy = request.ReceivedBy,
        ReceiverName = request.ReceivedBy == DeliveryReceiver.Customer ? null : request.ReceiverName?.Trim(),
        Signature = string.IsNullOrEmpty(request.Signature) ? null : request.Signature,
        Latitude = request.Location?.Latitude,
        Longitude = request.Location?.Longitude,
        AccuracyMeters = request.Location?.AccuracyMeters,
        RecordedAt = now,
    };

    public static DeliveryProof ForFailure(Guid tenantId, Guid shipmentId, string courier, DeliveryFailureReason reason, GeoPoint? location, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        TenantId = tenantId,
        ShipmentId = shipmentId,
        Delivered = false,
        FailureReason = reason,
        CourierLabel = courier,
        Latitude = location?.Latitude,
        Longitude = location?.Longitude,
        AccuracyMeters = location?.AccuracyMeters,
        RecordedAt = now,
    };
}
