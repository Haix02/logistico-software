using System.ComponentModel.DataAnnotations;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Contracts;

public sealed class StartShelfCheckRequest
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(16, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string HubCode { get; set; } = "";

    /// <summary>The shelf to count, e.g. "A-01-1"; empty for the whole hub.</summary>
    [StringLength(20, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Bin { get; set; }
}

public sealed class ShelfCheckScanRequest
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(40, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string TrackingNumber { get; set; } = "";
}

/// <summary>How a shelf check stands: what was expected and what the count found.</summary>
public sealed record ShelfCheckSummary(
    Guid Id,
    string Number,
    string HubCode,
    string? Bin,
    bool Finished,
    DateTimeOffset StartedAt,
    string? StartedBy,
    DateTimeOffset? FinishedAt,
    int Expected,
    int Found,
    int Missing,
    int WrongShelf,
    int NotExpected)
{
    /// <summary>Expected parcels not scanned yet (missing once the check is finished).</summary>
    public int Pending => Finished ? 0 : Expected - Found - Missing;
}

/// <summary>A shelf check with the parcels that need attention (missing, on the wrong shelf, not expected) and the latest found.</summary>
public sealed record ShelfCheckDetails(ShelfCheckSummary Summary, IReadOnlyList<ShelfCheckLineItem> Lines);

public sealed record ShelfCheckLineItem(
    Guid? ShipmentId,
    string TrackingNumber,
    ShelfCheckState State,
    string? SystemHubCode,
    string? SystemBin,
    ShipmentStatus? SystemStatus,
    DateTimeOffset? ScannedAt);

/// <summary>One scan in a shelf check and the check's numbers after it.</summary>
public sealed record ShelfCheckScanResult(ShelfCheckLineItem Line, ShelfCheckSummary Summary);
