using System.ComponentModel.DataAnnotations;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Contracts;

// ---------- Warehouse scans ----------

/// <summary>One scan at a hub station: receive, dispatch to courier, load/unload a truck, hand a return to the seller, verify.</summary>
public sealed class ScanRequest
{
    public ScanOperation Operation { get; set; } = ScanOperation.Receive;

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(40, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string TrackingNumber { get; set; } = "";

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(20, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string HubCode { get; set; } = IraqHubs.Baghdad;

    [StringLength(20, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? WarehouseBin { get; set; }

    [StringLength(100, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? CourierName { get; set; }

    /// <summary>Truck being loaded or unloaded (Load/Unload operations).</summary>
    public Guid? TransferId { get; set; }

    public DeliveryFailureReason? FailureReason { get; set; }
}

public sealed class BulkScanRequest
{
    public ScanOperation Operation { get; set; } = ScanOperation.Receive;

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [MinLength(1, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [MaxLength(500, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public List<string> TrackingNumbers { get; set; } = [];

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public string HubCode { get; set; } = IraqHubs.Baghdad;

    [StringLength(20, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? WarehouseBin { get; set; }

    [StringLength(100, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? CourierName { get; set; }

    public Guid? TransferId { get; set; }

    public DeliveryFailureReason? FailureReason { get; set; }
}

/// <summary>Result of one scan, shown at the station and on the live hub radar.</summary>
public sealed record ScanOutcome(
    long Seq,
    bool Ok,
    ScanOperation Operation,
    string TrackingNumber,
    Guid? ShipmentId,
    ShipmentStatus? Status,
    string HubCode,
    string? WarehouseBin,
    bool IsMisrouted,
    string? ExpectedHubCode,
    string? NextHubCode,
    string? TransferNumber,
    string? ConsigneeName,
    string? DestinationCode,
    string? CourierName,
    decimal? CodAmount,
    string? CodCurrency,
    string Message,
    string? ProblemCode,
    string? ScannedBy,
    DateTimeOffset ScannedAt);

public sealed record BulkScanResult(int Succeeded, int Failed, IReadOnlyList<ScanOutcome> Items);

public sealed record HubActivity(string HubCode, int ScansToday, int MisroutedToday, DateTimeOffset? LastScanAt);

/// <summary>Live hub radar: latest scans across all hubs (from the persistent scan log).</summary>
public sealed record HubRadar(long LastSeq, int ScansToday, int MisroutedToday, IReadOnlyList<HubActivity> Hubs, IReadOnlyList<ScanOutcome> Recent);

// ---------- Line-haul transfers ----------

public sealed record TransferListItem(
    Guid Id,
    string Number,
    string FromHubCode,
    string ToHubCode,
    TransferStatus Status,
    int Planned,
    int Loaded,
    int Received,
    int Missing,
    int Returns,
    string? VehiclePlate,
    string? DriverName,
    string? SealNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DepartedAt,
    DateTimeOffset? ArrivedAt,
    DateTimeOffset? ExpectedAt,
    bool IsOverdue)
{
    public int OnBoard => Loaded + Received + Missing;
}

public sealed record TransferItemView(
    Guid ShipmentId,
    string TrackingNumber,
    TransferItemState State,
    bool IsReturn,
    ShipmentStatus ShipmentStatus,
    string ConsigneeName,
    string? MerchantName,
    string DestinationCode,
    decimal? CodAmount,
    string? CodCurrency,
    DateTimeOffset PlannedAt,
    DateTimeOffset? LoadedAt,
    DateTimeOffset? ReceivedAt);

public sealed record TransferDetails(
    TransferListItem Summary,
    IReadOnlyList<TransferItemView> Items,
    string? CreatedBy,
    string? DepartedBy,
    string? ReceivedBy,
    string? Notes,
    uint Version);

public sealed record TransferListResult(
    IReadOnlyList<TransferListItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int Loading,
    int InTransit,
    int Overdue,
    int ParcelsWaiting);

public sealed class TransferQuery
{
    public TransferStatus? Status { get; set; }
    public string? Hub { get; set; }
    public bool? Overdue { get; set; }
    public string? Q { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    public int EffectivePage => Paging.Page(Page);
    public int EffectivePageSize => Math.Clamp(PageSize ?? 25, 1, 200);
}

public sealed class CreateTransferRequest : IValidatableObject
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public string FromHubCode { get; set; } = IraqHubs.Baghdad;

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public string ToHubCode { get; set; } = IraqHubs.Basra;

    [StringLength(30, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? VehiclePlate { get; set; }

    [StringLength(100, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? DriverName { get; set; }

    /// <summary>Also plan every parcel waiting at the origin hub for this destination.</summary>
    public bool PlanWaiting { get; set; } = true;

    /// <summary>Shape only; the server checks both hubs against the company's network.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(FromHubCode))
        {
            yield return new(ValidationText.Required, [nameof(FromHubCode)]);
        }

        if (string.IsNullOrWhiteSpace(ToHubCode))
        {
            yield return new(ValidationText.Required, [nameof(ToHubCode)]);
        }
        else if (string.Equals(FromHubCode, ToHubCode, StringComparison.OrdinalIgnoreCase))
        {
            yield return new(ValidationText.SamePlace, [nameof(ToHubCode)]);
        }
    }
}

public sealed class DepartTransferRequest
{
    [StringLength(30, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? VehiclePlate { get; set; }

    [StringLength(100, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? DriverName { get; set; }

    [StringLength(30, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? SealNumber { get; set; }
}

public sealed class CloseTransferRequest
{
    [StringLength(500, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Notes { get; set; }
}

// ---------- Iraq network map ----------

public sealed record GovernorateLoad(
    string Code,
    int Active,
    int AtHub,
    int OutForDelivery,
    int Failed,
    int Late,
    int Returning,
    int DeliveredToday);

public sealed record HubLoad(
    string HubCode,
    int OnShelves,
    int ToDispatch,
    int ToTransfer,
    int ReturnsWaiting,
    int Inbound,
    int ScansToday,
    int OpenAlerts);

public sealed record LaneLoad(string FromHubCode, string ToHubCode, int Trucks, int Parcels, int Waiting, bool Overdue);

public sealed record NetworkOverview(
    IReadOnlyList<GovernorateLoad> Governorates,
    IReadOnlyList<HubLoad> Hubs,
    IReadOnlyList<LaneLoad> Lanes,
    int ActiveTotal,
    int OnTrucks,
    int Late,
    int OpenAlerts,
    DateTimeOffset GeneratedAt);
