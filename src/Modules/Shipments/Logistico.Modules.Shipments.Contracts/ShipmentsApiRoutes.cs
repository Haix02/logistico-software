namespace Logistico.Modules.Shipments.Contracts;

/// <summary>Route templates shared by the API (server) and the API client (browser).</summary>
public static class ShipmentsApiRoutes
{
    public const string Shipments = "api/v1/shipments";
    public const string BulkBook = Shipments + "/bulk-book";
    public const string ExactShipments = Shipments + "/exact";
    public const string Summary = Shipments + "/summary";
    public const string Export = Shipments + "/export.csv";
    public const string PublicTracking = "api/v1/tracking";

    public const string Warehouse = "api/v1/warehouse";
    public const string WarehouseScan = Warehouse + "/scan";
    public const string WarehouseBulk = Warehouse + "/bulk";
    public const string WarehouseRadar = Warehouse + "/radar";

    public const string Warehouses = "api/v1/warehouses";

    public const string Transfers = "api/v1/transfers";
    public const string Network = "api/v1/network";
    public const string NetworkHubs = Network + "/hubs";
    public const string Returns = "api/v1/returns";
    public const string ReturnsHandOver = Returns + "/hand-over";
    public const string Requests = "api/v1/requests";

    public const string Automation = "api/v1/automation";
    public const string AutomationOverview = Automation + "/overview";
    public const string AutomationPolicy = Automation + "/policy";
    public const string AutomationAlerts = Automation + "/alerts";
    public const string AutomationLog = Automation + "/log";
    public const string AutomationMessages = Automation + "/messages";
    public const string AutomationRun = Automation + "/run";
    public const string Counters = "api/v1/ops/counters";
    public const string AnalyticsOverview = "api/v1/analytics/overview";
    public const string Insights = "api/v1/insights";
    public const string InsightsOpenData = Insights + "/open-data.csv";
    public const string CustomerLookup = "api/v1/customers/lookup";
    public const string Confirmations = "api/v1/confirmations";
    public const string Pricing = "api/v1/pricing";

    public const string Customs = "api/v1/customs";
    public const string Fleet = "api/v1/fleet";
    public const string FleetCouriers = Fleet + "/couriers";
    public const string FleetLookup = Fleet + "/lookup";

    /// <summary>The courier app: the signed-in courier's own run.</summary>
    public const string CourierRun = "api/v1/courier/run";
    public const string Finance = "api/v1/finance";
    public const string FinanceSummary = Finance + "/summary";
    public const string FinanceSettlements = Finance + "/settlements";

    public static string ById(Guid id) => $"{Shipments}/{id}";

    public static string WarehouseById(Guid id) => $"{Warehouses}/{id}";

    public static string CancelOrder(Guid id) => $"{Shipments}/{id}/cancel";

    public static string RecordConfirmation(Guid id) => $"{Shipments}/{id}/confirmation";

    public static string TrackConfirm(string trackingNumber) => $"{PublicTracking}/{Uri.EscapeDataString(trackingNumber)}/confirm";

    public static string TrackRating(string trackingNumber) => $"{PublicTracking}/{Uri.EscapeDataString(trackingNumber)}/rating";
    public static string Events(Guid id) => $"{Shipments}/{id}/events";
    public static string StartReturn(Guid id) => $"{Shipments}/{id}/return";
    public static string Track(string trackingNumber) => $"{PublicTracking}/{Uri.EscapeDataString(trackingNumber)}";
    public static string TrackRequests(string trackingNumber) => $"{Track(trackingNumber)}/requests";
    public static string TransferById(Guid id) => $"{Transfers}/{id}";
    public static string TransferDepart(Guid id) => $"{Transfers}/{id}/depart";
    public static string TransferClose(Guid id) => $"{Transfers}/{id}/close";
    public static string ReturnManifest(string number) => $"{Returns}/manifests/{Uri.EscapeDataString(number)}";
    public static string RequestById(Guid id) => $"{Requests}/{id}";
    public static string RequestResolve(Guid id) => $"{Requests}/{id}/resolve";
    public static string AlertResolve(Guid id) => $"{AutomationAlerts}/{id}/resolve";
    public static string MessageMark(Guid id) => $"{AutomationMessages}/{id}";
    public static string CustomsByShipment(Guid shipmentId) => $"{Customs}/{shipmentId}";
    public static string CustomsSadXml(Guid shipmentId) => $"{Customs}/{shipmentId}/sad.xml";
    public static string CourierById(Guid id) => $"{FleetCouriers}/{id}";

    public static string CourierDelivered(Guid shipmentId) => $"{CourierRun}/{shipmentId}/delivered";

    public static string CourierFailed(Guid shipmentId) => $"{CourierRun}/{shipmentId}/failed";
    public static string SettlementExport(Guid id) => $"{FinanceSettlements}/{id}/export.csv";
}
