using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Modules.Shipments.Domain;

/// <summary>One company's automation rules (one row per tenant).</summary>
internal sealed class AutomationSettings
{
    private AutomationSettings()
    {
    }

    public Guid TenantId { get; private set; }
    public int MaxDeliveryAttempts { get; private set; }
    public bool ReturnOnRefusal { get; private set; }
    public bool AutoReschedule { get; private set; }
    public bool AutoPlanTransfers { get; private set; }
    public bool AutoApplyCustomerRequests { get; private set; }
    public bool NotifyCustomers { get; private set; }
    public int PickupSlaHours { get; private set; }
    public int WarehouseDwellHours { get; private set; }
    public int OutForDeliveryHours { get; private set; }
    public int LineHaulGraceHours { get; private set; }
    public int ReturnCollectionDays { get; private set; }
    public int RequestSlaHours { get; private set; }
    public OrderConfirmationMode OrderConfirmation { get; private set; } = OrderConfirmationMode.RiskyCustomers;
    public int ConfirmationHours { get; private set; } = 12;
    public int AutoCancelUnconfirmedHours { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }
    public DateTimeOffset? LastSweepAt { get; private set; }
    public uint Version { get; private set; }

    public static AutomationSettings Default(Guid tenantId, DateTimeOffset now)
    {
        var settings = new AutomationSettings { TenantId = tenantId };
        settings.Update(new AutomationPolicy(), null, now);
        return settings;
    }

    public void Update(AutomationPolicy policy, string? by, DateTimeOffset now)
    {
        MaxDeliveryAttempts = policy.MaxDeliveryAttempts;
        ReturnOnRefusal = policy.ReturnOnRefusal;
        AutoReschedule = policy.AutoReschedule;
        AutoPlanTransfers = policy.AutoPlanTransfers;
        AutoApplyCustomerRequests = policy.AutoApplyCustomerRequests;
        NotifyCustomers = policy.NotifyCustomers;
        PickupSlaHours = policy.PickupSlaHours;
        WarehouseDwellHours = policy.WarehouseDwellHours;
        OutForDeliveryHours = policy.OutForDeliveryHours;
        LineHaulGraceHours = policy.LineHaulGraceHours;
        ReturnCollectionDays = policy.ReturnCollectionDays;
        RequestSlaHours = policy.RequestSlaHours;
        OrderConfirmation = policy.OrderConfirmation;
        ConfirmationHours = policy.ConfirmationHours;
        AutoCancelUnconfirmedHours = policy.AutoCancelUnconfirmedHours;
        UpdatedAt = now;
        UpdatedBy = by;
    }

    public void MarkSwept(DateTimeOffset now) => LastSweepAt = now;

    public AutomationPolicy ToPolicy() => new()
    {
        MaxDeliveryAttempts = MaxDeliveryAttempts,
        ReturnOnRefusal = ReturnOnRefusal,
        AutoReschedule = AutoReschedule,
        AutoPlanTransfers = AutoPlanTransfers,
        AutoApplyCustomerRequests = AutoApplyCustomerRequests,
        NotifyCustomers = NotifyCustomers,
        PickupSlaHours = PickupSlaHours,
        WarehouseDwellHours = WarehouseDwellHours,
        OutForDeliveryHours = OutForDeliveryHours,
        LineHaulGraceHours = LineHaulGraceHours,
        ReturnCollectionDays = ReturnCollectionDays,
        RequestSlaHours = RequestSlaHours,
        OrderConfirmation = OrderConfirmation,
        ConfirmationHours = ConfirmationHours,
        AutoCancelUnconfirmedHours = AutoCancelUnconfirmedHours,
        Version = Version,
        UpdatedAt = UpdatedAt,
        UpdatedBy = UpdatedBy,
    };
}

/// <summary>An operational exception (late, stuck, missing, misrouted) that someone should look at.</summary>
internal sealed class OpsAlert
{
    private OpsAlert()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public AlertKind Kind { get; private set; }
    public AlertSeverity Severity { get; private set; }

    /// <summary>"shipment:{id}", "transfer:{id}" or "request:{id}" — one open alert per kind and subject.</summary>
    public string SubjectKey { get; private set; } = "";
    public Guid? ShipmentId { get; private set; }
    public string? TrackingNumber { get; private set; }
    public Guid? TransferId { get; private set; }
    public string? TransferNumber { get; private set; }
    public Guid? RequestId { get; private set; }
    public string? RequestNumber { get; private set; }
    public string? HubCode { get; private set; }
    public string? Governorate { get; private set; }
    public int? Hours { get; private set; }
    public string? Detail { get; private set; }
    public DateTimeOffset RaisedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolvedBy { get; private set; }
    public bool AutoResolved { get; private set; }

    public bool IsOpen => ResolvedAt is null;

    public static OpsAlert ForShipment(Shipment s, AlertKind kind, AlertSeverity severity, int? hours, string? detail, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        TenantId = s.TenantId,
        Kind = kind,
        Severity = severity,
        SubjectKey = Subject(kind, "shipment", s.Id),
        ShipmentId = s.Id,
        TrackingNumber = s.TrackingNumber,
        HubCode = s.CurrentHubCode ?? s.DestinationHubCode,
        Governorate = s.DestinationCode,
        Hours = hours,
        Detail = detail,
        RaisedAt = now,
        LastSeenAt = now,
    };

    /// <summary>Shipment alert built from a projected row (sweeps do not load whole shipments).</summary>
    public static OpsAlert ForShipmentRow(AlertKind kind, AlertSeverity severity, Guid shipmentId, Guid tenantId, string trackingNumber, string? hub, string governorate, int? hours, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        TenantId = tenantId,
        Kind = kind,
        Severity = severity,
        SubjectKey = Subject(kind, "shipment", shipmentId),
        ShipmentId = shipmentId,
        TrackingNumber = trackingNumber,
        HubCode = hub,
        Governorate = governorate,
        Hours = hours,
        RaisedAt = now,
        LastSeenAt = now,
    };

    public static OpsAlert ForTransfer(HubTransfer t, AlertKind kind, AlertSeverity severity, int? hours, string? detail, DateTimeOffset now, Guid? shipmentId = null, string? trackingNumber = null) => new()
    {
        Id = Guid.CreateVersion7(now),
        TenantId = t.TenantId,
        Kind = kind,
        Severity = severity,
        SubjectKey = shipmentId is { } id ? Subject(kind, "shipment", id) : Subject(kind, "transfer", t.Id),
        ShipmentId = shipmentId,
        TrackingNumber = trackingNumber,
        TransferId = t.Id,
        TransferNumber = t.Number,
        HubCode = t.ToHubCode,
        Hours = hours,
        Detail = detail ?? $"{t.FromHubCode}>{t.ToHubCode}",
        RaisedAt = now,
        LastSeenAt = now,
    };

    public static OpsAlert ForRequest(ServiceRequest r, int hours, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        TenantId = r.TenantId,
        Kind = AlertKind.RequestOverdue,
        Severity = AlertSeverity.Warning,
        SubjectKey = Subject(AlertKind.RequestOverdue, "request", r.Id),
        ShipmentId = r.ShipmentId,
        TrackingNumber = r.TrackingNumber,
        RequestId = r.Id,
        RequestNumber = r.Number,
        Hours = hours,
        Detail = r.Type.ToString(),
        RaisedAt = now,
        LastSeenAt = now,
    };

    public static string Subject(AlertKind kind, string type, Guid id) => $"{kind}:{type}:{id:N}";

    public void Touch(AlertSeverity severity, int? hours, DateTimeOffset now)
    {
        Severity = severity;
        Hours = hours;
        LastSeenAt = now;
    }

    public void Resolve(string? by, bool automatic, DateTimeOffset now)
    {
        if (ResolvedAt is not null)
        {
            return;
        }

        ResolvedAt = now;
        ResolvedBy = by;
        AutoResolved = automatic;
    }
}

/// <summary>What the automation engine did, for the activity log and audits.</summary>
internal sealed class AutomationLogEntry
{
    private AutomationLogEntry()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public DateTimeOffset At { get; private set; }
    public AutomationRule Rule { get; private set; }
    public Guid? ShipmentId { get; private set; }
    public string? TrackingNumber { get; private set; }
    public string? Detail { get; private set; }

    public static AutomationLogEntry Create(Guid tenantId, AutomationRule rule, Guid? shipmentId, string? trackingNumber, string? detail, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        TenantId = tenantId,
        At = now,
        Rule = rule,
        ShipmentId = shipmentId,
        TrackingNumber = trackingNumber,
        Detail = detail is { Length: > 300 } ? detail[..300] : detail,
    };
}

/// <summary>A message to the customer (WhatsApp first), queued automatically and sent by staff or a provider.</summary>
internal sealed class CustomerNotification
{
    private CustomerNotification()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ShipmentId { get; private set; }
    public string TrackingNumber { get; private set; } = "";
    public NotificationTemplate Template { get; private set; }
    public string Channel { get; private set; } = "whatsapp";
    public string Phone { get; private set; } = "";
    public string Body { get; private set; } = "";
    public NotificationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? SentBy { get; private set; }

    public static CustomerNotification Queue(Shipment s, NotificationTemplate template, string body, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        TenantId = s.TenantId,
        ShipmentId = s.Id,
        TrackingNumber = s.TrackingNumber,
        Template = template,
        Phone = s.ConsigneePhone ?? "",
        Body = body,
        Status = NotificationStatus.Pending,
        CreatedAt = now,
    };

    public void Mark(NotificationStatus status, string? by, DateTimeOffset now)
    {
        Status = status;
        SentAt = status == NotificationStatus.Sent ? now : null;
        SentBy = by;
    }
}

/// <summary>Every scan at every hub — the audit trail behind the live hub radar (works across servers and restarts).</summary>
internal sealed class HubScan
{
    private HubScan()
    {
    }

    public long Id { get; private set; }
    public Guid TenantId { get; private set; }
    public DateTimeOffset ScannedAt { get; private set; }
    public string HubCode { get; private set; } = "";
    public ScanOperation Operation { get; private set; }
    public string TrackingNumber { get; private set; } = "";
    public Guid? ShipmentId { get; private set; }
    public ShipmentStatus? Status { get; private set; }
    public bool Ok { get; private set; }
    public bool IsMisrouted { get; private set; }
    public string? ExpectedHubCode { get; private set; }
    public string? WarehouseBin { get; private set; }
    public string? TransferNumber { get; private set; }
    public string? CourierName { get; private set; }
    public string Message { get; private set; } = "";
    public string? ProblemCode { get; private set; }
    public string? ScannedBy { get; private set; }

    public static HubScan Record(
        Guid tenantId,
        string hubCode,
        ScanOperation operation,
        string trackingNumber,
        Shipment? shipment,
        bool ok,
        bool misrouted,
        string? expectedHub,
        string? transferNumber,
        string message,
        string? problemCode,
        string? scannedBy,
        DateTimeOffset now) => new()
        {
            TenantId = tenantId,
            ScannedAt = now,
            HubCode = hubCode,
            Operation = operation,
            TrackingNumber = trackingNumber.Length > 40 ? trackingNumber[..40] : trackingNumber,
            ShipmentId = shipment?.Id,
            Status = shipment?.Status,
            Ok = ok,
            IsMisrouted = misrouted,
            ExpectedHubCode = expectedHub,
            WarehouseBin = shipment?.WarehouseBin,
            TransferNumber = transferNumber,
            CourierName = operation == ScanOperation.Dispatch ? shipment?.CourierName : null,
            Message = message.Length > 300 ? message[..300] : message,
            ProblemCode = problemCode,
            ScannedBy = scannedBy,
        };
}
