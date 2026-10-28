using System.Globalization;
using System.Resources;

namespace Logistico.Modules.Shipments.Contracts;

/// <summary>Localised names and messages of the Shipments module (Resources/ShipmentTexts[.ar].resx).</summary>
public static class ShipmentTexts
{
    private static readonly ResourceManager Resources = new("Logistico.Modules.Shipments.Contracts.Resources.ShipmentTexts", typeof(ShipmentTexts).Assembly);

    public static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Of(ShipmentStatus status) => Get("Status_" + status);
    public static string Of(TransportMode mode) => Get("Mode_" + mode);
    public static string Of(ServiceType service) => Get("Service_" + service);
    public static string Of(DeliveryFailureReason reason) => Get("Failure_" + reason);
    public static string Of(DeliveryReceiver receiver) => Get("Receiver_" + receiver);
    public static string Of(TrackingStage stage) => Get("Stage_" + stage);
    public static string Of(CustomsLane lane) => Get("Lane_" + lane);
    public static string Of(VehicleType vehicle) => Get("Vehicle_" + vehicle);
    public static string Of(PayoutMethod method) => Get("Payout_" + method);
    public static string Of(ReturnReason reason) => Get("Return_" + reason);
    public static string Of(TransferStatus status) => Get("Transfer_" + status);
    public static string Of(TransferItemState state) => Get("TransferItem_" + state);
    public static string Of(RequestType type) => Get("Request_" + type);
    public static string Of(RequestStatus status) => Get("RequestStatus_" + status);
    public static string Of(RequestSource source) => Get("RequestSource_" + source);
    public static string Of(AlertKind kind) => Get("Alert_" + kind);
    public static string Of(AlertSeverity severity) => Get("Severity_" + severity);
    public static string Of(NotificationTemplate template) => Get("Template_" + template);
    public static string Of(NotificationStatus status) => Get("Message_" + status);
    public static string Of(ScanOperation operation) => Get("Scan_" + operation);
    public static string Of(AutomationRule rule) => Get("Rule_" + rule);
    public static string Of(Rules.HubKind kind) => Get("HubKind_" + kind);
    public static string Of(ConfirmationStatus status) => Get("Confirmation_" + status);
    public static string Of(ConfirmationOutcome outcome) => Get("ConfirmationOutcome_" + outcome);
    public static string Of(OrderConfirmationMode mode) => Get("ConfirmationMode_" + mode);
    public static string Of(CustomerRisk risk) => Get("CustomerRisk_" + risk);
    public static string Of(ProductCategory category) => Get("Category_" + category);

    /// <summary>Human message for a problem code, e.g. <c>shipment.invalid_transition</c>.</summary>
    public static string Problem(string code, params object?[] args)
    {
        var format = Resources.GetString("Problem_" + code, CultureInfo.CurrentUICulture);
        return format is null ? code : string.Format(CultureInfo.CurrentCulture, format, args);
    }
}
