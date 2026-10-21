using System.Text.Json.Serialization;

namespace Logistico.Modules.Shipments.Contracts;

/// <summary>International freight into Iraq, or a domestic delivery (e.g. an Instagram shop sending to its customer).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ServiceType>))]
public enum ServiceType
{
    International,
    Domestic,
}

[JsonConverter(typeof(JsonStringEnumConverter<TransportMode>))]
public enum TransportMode
{
    SeaFcl,
    SeaLcl,
    Air,
    Road,
    Courier,
}

[JsonConverter(typeof(JsonStringEnumConverter<ShipmentStatus>))]
public enum ShipmentStatus
{
    Draft,
    Booked,
    PickedUp,
    ReceivedAtOrigin,
    Consolidated,
    DepartedOrigin,
    InTransit,
    ArrivedPortOfEntry,
    CustomsPending,
    CustomsHold,
    Released,
    AtDestinationWarehouse,
    OutForDelivery,
    Delivered,
    DeliveryFailed,
    ReturnInitiated,
    Returned,
    Cancelled,
    AtWarehouse,
    ReadyForDispatch,

    /// <summary>On a line-haul truck between two hubs (e.g. Baghdad → Basra).</summary>
    InTransitToHub,

    /// <summary>Going back to the seller's hub on a line-haul truck.</summary>
    ReturnInTransit,

    /// <summary>Back at the seller's hub, waiting to be handed over to the seller.</summary>
    ReturnAtHub,
}

[JsonConverter(typeof(JsonStringEnumConverter<DeliveryFailureReason>))]
public enum DeliveryFailureReason
{
    NoAnswer,
    CustomerRefused,
    WrongAddress,
    CustomerRescheduled,
    Unreachable,
    Other,
}

/// <summary>Customer-facing progress stages shown on tracking pages.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TrackingStage>))]
public enum TrackingStage
{
    OrderPlaced,
    PickedUp,
    AtOrigin,
    InTransit,
    Customs,
    AtWarehouse,
    OutForDelivery,
    Delivered,
}

/// <summary>ASYCUDA World selectivity lane assigned by Iraqi customs risk engine.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CustomsLane>))]
public enum CustomsLane
{
    Unassigned,
    Green,
    Yellow,
    Red,
    Blue,
}

/// <summary>Action taken when saving a customs declaration in the workbench.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CustomsAction>))]
public enum CustomsAction
{
    Save,
    Register,
    Hold,
    Release,
}

/// <summary>Courier vehicle category used for last-mile and linehaul operations.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VehicleType>))]
public enum VehicleType
{
    Motorcycle,
    Van,
    LightTruck,
    HeavyTruck,
}

/// <summary>Method used to remit collected COD balances to a seller.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayoutMethod>))]
public enum PayoutMethod
{
    Cash,
    ZainCash,
    QiCard,
    BankTransfer,
}

/// <summary>Why a parcel is going back to the seller.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReturnReason>))]
public enum ReturnReason
{
    MaxAttemptsReached,
    CustomerRefused,
    CustomerReturn,
    SellerCancelled,
    WrongAddress,
    Damaged,
    Other,
}

/// <summary>Line-haul transfer between two hubs (a truck manifest).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TransferStatus>))]
public enum TransferStatus
{
    Loading,
    InTransit,
    Received,
}

[JsonConverter(typeof(JsonStringEnumConverter<TransferItemState>))]
public enum TransferItemState
{
    /// <summary>Added automatically: the parcel is at the hub and must go on this truck.</summary>
    Planned,
    Loaded,
    Received,
    Missing,
}

/// <summary>What a customer, seller or staff member asks for on an order.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RequestType>))]
public enum RequestType
{
    Reschedule,
    ChangeAddress,
    ChangePhone,
    Cancel,
    Return,
    Complaint,
}

[JsonConverter(typeof(JsonStringEnumConverter<RequestStatus>))]
public enum RequestStatus
{
    Open,
    InProgress,
    Resolved,
    Rejected,
}

[JsonConverter(typeof(JsonStringEnumConverter<RequestSource>))]
public enum RequestSource
{
    Staff,
    Customer,
    Seller,
    System,
}

/// <summary>Operational exceptions raised by the automation engine.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AlertKind>))]
public enum AlertKind
{
    PickupOverdue,
    WarehouseDwell,
    OutForDeliveryOverdue,
    PromiseMissed,
    TransferOverdue,
    MissingInTransfer,
    Misrouted,
    ReturnNotCollected,
    RequestOverdue,
    ConfirmationOverdue,
}

[JsonConverter(typeof(JsonStringEnumConverter<AlertSeverity>))]
public enum AlertSeverity
{
    Info,
    Warning,
    Critical,
}

/// <summary>Customer messages queued automatically at key moments of a delivery.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotificationTemplate>))]
public enum NotificationTemplate
{
    OutForDelivery,
    DeliveryFailed,
    Rescheduled,
    Delivered,
    ReturnStarted,
    RequestReceived,
    ConfirmOrder,
}

[JsonConverter(typeof(JsonStringEnumConverter<NotificationStatus>))]
public enum NotificationStatus
{
    Pending,
    Sent,
    Skipped,
}

/// <summary>What a warehouse scan did.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScanOperation>))]
public enum ScanOperation
{
    Receive,
    Dispatch,
    Verify,
    Load,
    Unload,
    HandOver,
    Deliver,
    Fail,
}

/// <summary>What the automation engine did (activity log).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationRule>))]
public enum AutomationRule
{
    AutoReturn,
    AutoReschedule,
    PlanTransfer,
    AlertRaised,
    AlertResolved,
    RequestApplied,
    NotificationQueued,
    TransferClosed,
    ConfirmationRequested,
    AutoCancel,
}

/// <summary>Whether the customer still has to confirm a cash-on-delivery order before we collect it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConfirmationStatus>))]
public enum ConfirmationStatus
{
    NotRequired,
    Pending,
    Confirmed,
    Declined,
}

/// <summary>Result of a confirmation call or message.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConfirmationOutcome>))]
public enum ConfirmationOutcome
{
    Confirmed,
    NoAnswer,
    Declined,
}

/// <summary>Which new orders need the customer's confirmation before pickup.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OrderConfirmationMode>))]
public enum OrderConfirmationMode
{
    Off,

    /// <summary>Customers whose earlier orders came back (medium or high risk).</summary>
    RiskyCustomers,

    AllOrders,
}
