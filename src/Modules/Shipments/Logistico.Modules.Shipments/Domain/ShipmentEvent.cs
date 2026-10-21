using Logistico.Modules.Shipments.Contracts;

namespace Logistico.Modules.Shipments.Domain;

/// <summary>Append-only milestone in a shipment's history.</summary>
internal sealed class ShipmentEvent
{
    private ShipmentEvent()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ShipmentId { get; private set; }
    public ShipmentStatus Status { get; private set; }

    /// <summary>When it happened in the real world (may be earlier than <see cref="RecordedAt"/> after offline sync).</summary>
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public string? Location { get; private set; }
    public string? WarehouseBin { get; private set; }
    public string? CourierName { get; private set; }
    public DeliveryFailureReason? FailureReason { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>user | scan | gps | carrier | customs | system</summary>
    public string Source { get; private set; } = "";
    public string? RecordedBy { get; private set; }

    internal static ShipmentEvent Create(
        Shipment shipment,
        ShipmentStatus status,
        DateTimeOffset occurredAt,
        MilestoneDetails details,
        string source,
        string? recordedBy,
        DateTimeOffset now) => new()
        {
            Id = Guid.CreateVersion7(now),
            TenantId = shipment.TenantId,
            ShipmentId = shipment.Id,
            Status = status,
            OccurredAt = occurredAt.ToUniversalTime(),
            RecordedAt = now,
            Location = string.IsNullOrWhiteSpace(details.Location) ? null : details.Location.Trim(),
            WarehouseBin = details.WarehouseBin?.ToUpperInvariant(),
            CourierName = string.IsNullOrWhiteSpace(details.CourierName) ? null : details.CourierName.Trim(),
            FailureReason = details.FailureReason,
            Notes = string.IsNullOrWhiteSpace(details.Notes) ? null : details.Notes.Trim(),
            Source = source,
            RecordedBy = recordedBy,
        };
}
