using System.ComponentModel.DataAnnotations;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Contracts;

public sealed record OpsAlertItem(
    Guid Id,
    AlertKind Kind,
    AlertSeverity Severity,
    Guid? ShipmentId,
    string? TrackingNumber,
    Guid? TransferId,
    string? TransferNumber,
    Guid? RequestId,
    string? RequestNumber,
    string? HubCode,
    string? Governorate,
    int? Hours,
    string? Detail,
    DateTimeOffset RaisedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolvedBy,
    bool AutoResolved);

public sealed record AlertKindCount(AlertKind Kind, int Count);

public sealed record AlertList(
    IReadOnlyList<OpsAlertItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int Critical,
    int Warning,
    IReadOnlyList<AlertKindCount> OpenByKind);

public sealed class AlertQuery
{
    /// <summary>"open" (default), "closed" or "all".</summary>
    public string? State { get; set; }
    public AlertKind? Kind { get; set; }
    public AlertSeverity? Severity { get; set; }
    public string? Hub { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    public int EffectivePage => Paging.Page(Page);
    public int EffectivePageSize => Math.Clamp(PageSize ?? 25, 1, 200);
}

public sealed record AutomationLogItem(Guid Id, DateTimeOffset At, AutomationRule Rule, Guid? ShipmentId, string? TrackingNumber, string? Detail);

public sealed record AutomationLogPage(IReadOnlyList<AutomationLogItem> Items, int TotalCount, int Page, int PageSize);

public sealed record CustomerNotificationItem(
    Guid Id,
    Guid ShipmentId,
    string TrackingNumber,
    NotificationTemplate Template,
    string Phone,
    string Body,
    NotificationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    string? SentBy);

public sealed record NotificationPage(IReadOnlyList<CustomerNotificationItem> Items, int TotalCount, int Page, int PageSize, int Pending);

public sealed class MarkNotificationRequest
{
    public NotificationStatus Status { get; set; } = NotificationStatus.Sent;
}

/// <summary>Today's automation in numbers (Automation page header).</summary>
public sealed record AutomationOverview(
    int OpenAlerts,
    int CriticalAlerts,
    int PendingMessages,
    int ActionsToday,
    int AutoReturnsToday,
    int AutoReschedulesToday,
    int RequestsAppliedToday,
    int TransfersPlannedToday,
    DateTimeOffset? LastSweepAt);

/// <summary>Small counters for the side menu badges, polled by the app shell.</summary>
public sealed record OpsCounters(
    int OpenAlerts,
    int CriticalAlerts,
    int OpenRequests,
    int OverdueRequests,
    int ReturnsAtHub,
    int ParcelsWaitingForTruck,
    int TrucksOnRoad,
    int PendingMessages,
    int PendingConfirmations = 0);

// ---------- Returns ----------

public sealed record ReturnSellerGroup(Guid? MerchantId, string MerchantName, string HubCode, int Count, DateTimeOffset OldestAt, decimal CodValueIqd);

public sealed record ReturnsOverview(
    int Initiated,
    int OnTrucks,
    int AtHub,
    int ReturnedThisMonth,
    int OverdueCollection,
    IReadOnlyList<ReturnSellerGroup> ReadyBySeller,
    IReadOnlyList<ReturnReasonCount> ByReason);

public sealed record ReturnReasonCount(ReturnReason Reason, int Count);

public sealed class StartReturnRequest
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public ReturnReason? Reason { get; set; }

    [StringLength(500, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Notes { get; set; }
}

public sealed class HandOverReturnsRequest
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [MinLength(1, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [MaxLength(500, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public List<Guid> ShipmentIds { get; set; } = [];

    /// <summary>Name of the seller's representative who signs for the parcels.</summary>
    [StringLength(120, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? ReceivedBy { get; set; }

    [StringLength(500, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Notes { get; set; }
}

public sealed record ReturnManifest(
    string Number,
    string HubCode,
    string? MerchantName,
    string? ReceivedBy,
    string? HandedOverBy,
    DateTimeOffset At,
    IReadOnlyList<ShipmentListItem> Items);
