namespace Logistico.Web.Client.Ui;

/// <summary>One row in a tracking timeline, built from either the internal or the public event model.</summary>
public sealed record TimelineEntry(
    Logistico.Modules.Shipments.Contracts.ShipmentStatus Status,
    DateTimeOffset OccurredAt,
    string? Location = null,
    string? WarehouseBin = null,
    string? CourierName = null,
    Logistico.Modules.Shipments.Contracts.DeliveryFailureReason? FailureReason = null,
    string? Notes = null,
    string? Source = null,
    string? RecordedBy = null);
