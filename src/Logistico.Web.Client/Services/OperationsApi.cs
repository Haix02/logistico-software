using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;

namespace Logistico.Web.Client.Services;

/// <summary>Typed client for hub stations, line-haul trucks, the Iraq network, returns, requests and automation.</summary>
internal sealed class OperationsApi(HttpClient http) : ApiClient(http)
{
    // Hub stations
    public Task<ScanOutcome> ScanAsync(ScanRequest request, CancellationToken ct = default) =>
        SendAsync<ScanOutcome>(HttpMethod.Post, ShipmentsApiRoutes.WarehouseScan, request, null, ct);

    public Task<PagedResult<ShelfCheckSummary>> ListShelfChecksAsync(string? hub, int page, CancellationToken ct = default) =>
        GetAsync<PagedResult<ShelfCheckSummary>>(ShipmentsApiRoutes.ShelfChecks + Query(("hub", hub), ("page", page)), ct);

    public Task<ShelfCheckDetails> StartShelfCheckAsync(StartShelfCheckRequest request, CancellationToken ct = default) =>
        SendAsync<ShelfCheckDetails>(HttpMethod.Post, ShipmentsApiRoutes.ShelfChecks, request, null, ct);

    public Task<ShelfCheckDetails?> GetShelfCheckAsync(Guid id, CancellationToken ct = default) =>
        FindAsync<ShelfCheckDetails>(ShipmentsApiRoutes.ShelfCheck(id), ct);

    public Task<ShelfCheckScanResult> ScanShelfAsync(Guid id, string trackingNumber, CancellationToken ct = default) =>
        SendAsync<ShelfCheckScanResult>(HttpMethod.Post, ShipmentsApiRoutes.ShelfCheckScan(id), new ShelfCheckScanRequest { TrackingNumber = trackingNumber }, null, ct);

    public Task<ShelfCheckDetails> FinishShelfCheckAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<ShelfCheckDetails>(HttpMethod.Post, ShipmentsApiRoutes.ShelfCheckFinish(id), null, null, ct);

    public Task<BulkScanResult> BulkScanAsync(BulkScanRequest request, CancellationToken ct = default) =>
        SendAsync<BulkScanResult>(HttpMethod.Post, ShipmentsApiRoutes.WarehouseBulk, request, null, ct);

    public Task<HubRadar> GetRadarAsync(long? afterSeq = null, string? hub = null, CancellationToken ct = default) =>
        GetAsync<HubRadar>(ShipmentsApiRoutes.WarehouseRadar + Query(("afterSeq", afterSeq), ("hub", hub)), ct);

    // Line-haul trucks
    public Task<TransferListResult> ListTransfersAsync(TransferStatus? status = null, string? hub = null, bool? overdue = null, string? q = null, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
        GetAsync<TransferListResult>(ShipmentsApiRoutes.Transfers + Query(("status", status), ("hub", hub), ("overdue", overdue), ("q", q), ("page", page), ("pageSize", pageSize)), ct);

    public Task<TransferDetails?> GetTransferAsync(Guid id, CancellationToken ct = default) => FindAsync<TransferDetails>(ShipmentsApiRoutes.TransferById(id), ct);

    public Task<TransferRef> OpenTransferAsync(CreateTransferRequest request, CancellationToken ct = default) =>
        SendAsync<TransferRef>(HttpMethod.Post, ShipmentsApiRoutes.Transfers, request, null, ct);

    public Task DepartTransferAsync(Guid id, uint version, DepartTransferRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, ShipmentsApiRoutes.TransferDepart(id), request, version, ct);

    public Task CloseTransferAsync(Guid id, uint version, CloseTransferRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, ShipmentsApiRoutes.TransferClose(id), request, version, ct);

    // Iraq network
    public Task<NetworkOverview> GetNetworkAsync(CancellationToken ct = default) => GetAsync<NetworkOverview>(ShipmentsApiRoutes.Network, ct);

    // Returns
    public Task<ReturnsOverview> GetReturnsAsync(CancellationToken ct = default) => GetAsync<ReturnsOverview>(ShipmentsApiRoutes.Returns, ct);

    public Task<ReturnManifest> HandOverAsync(HandOverReturnsRequest request, CancellationToken ct = default) =>
        SendAsync<ReturnManifest>(HttpMethod.Post, ShipmentsApiRoutes.ReturnsHandOver, request, null, ct);

    public Task<ReturnManifest?> GetManifestAsync(string number, CancellationToken ct = default) => FindAsync<ReturnManifest>(ShipmentsApiRoutes.ReturnManifest(number), ct);

    // Requests
    public Task<ServiceRequestList> ListRequestsAsync(ServiceRequestQuery query, CancellationToken ct = default) =>
        GetAsync<ServiceRequestList>(ShipmentsApiRoutes.Requests + Query(
            [.. (query.Status ?? []).Select(s => ("status", (object?)s)), ("type", query.Type), ("source", query.Source), ("shipmentId", query.ShipmentId),
             ("overdue", query.Overdue), ("q", query.Q), ("page", query.Page), ("pageSize", query.PageSize)]), ct);

    public Task<ServiceRequestItem> CreateRequestAsync(CreateServiceRequest request, CancellationToken ct = default) =>
        SendAsync<ServiceRequestItem>(HttpMethod.Post, ShipmentsApiRoutes.Requests, request, null, ct);

    public Task StartRequestAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, ShipmentsApiRoutes.RequestById(id) + "/start", null, null, ct);

    public Task ResolveRequestAsync(Guid id, ResolveServiceRequest resolution, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, ShipmentsApiRoutes.RequestResolve(id), resolution, null, ct);

    // Automation
    public Task<AutomationOverview> GetAutomationOverviewAsync(CancellationToken ct = default) => GetAsync<AutomationOverview>(ShipmentsApiRoutes.AutomationOverview, ct);

    public Task<AutomationPolicy> GetPolicyAsync(CancellationToken ct = default) => GetAsync<AutomationPolicy>(ShipmentsApiRoutes.AutomationPolicy, ct);

    public Task<AutomationPolicy> UpdatePolicyAsync(AutomationPolicy policy, CancellationToken ct = default) =>
        SendAsync<AutomationPolicy>(HttpMethod.Put, ShipmentsApiRoutes.AutomationPolicy, policy, policy.Version, ct);

    public Task<AlertList> ListAlertsAsync(bool? open = true, AlertKind? kind = null, AlertSeverity? severity = null, string? hub = null, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
        GetAsync<AlertList>(ShipmentsApiRoutes.AutomationAlerts + Query(("state", open switch { true => "open", false => "closed", _ => "all" }),
            ("kind", kind), ("severity", severity), ("hub", hub), ("page", page), ("pageSize", pageSize)), ct);

    public Task ResolveAlertAsync(Guid id, CancellationToken ct = default) => SendAsync(HttpMethod.Post, ShipmentsApiRoutes.AlertResolve(id), null, null, ct);

    public Task<AutomationLogPage> GetLogAsync(AutomationRule? rule = null, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        GetAsync<AutomationLogPage>(ShipmentsApiRoutes.AutomationLog + Query(("rule", rule), ("page", page), ("pageSize", pageSize)), ct);

    public Task<NotificationPage> GetMessagesAsync(NotificationStatus? status = null, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
        GetAsync<NotificationPage>(ShipmentsApiRoutes.AutomationMessages + Query(("status", status), ("page", page), ("pageSize", pageSize)), ct);

    public Task MarkMessageAsync(Guid id, NotificationStatus status, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, ShipmentsApiRoutes.MessageMark(id), new MarkNotificationRequest { Status = status }, null, ct);

    public Task RunAutomationAsync(CancellationToken ct = default) => SendAsync(HttpMethod.Post, ShipmentsApiRoutes.AutomationRun, null, null, ct);

    public Task<OpsCounters> GetCountersAsync(CancellationToken ct = default) => GetAsync<OpsCounters>(ShipmentsApiRoutes.Counters, ct);

    // Analytics
    public Task<AnalyticsOverview> GetAnalyticsAsync(int days, CancellationToken ct = default) =>
        GetAsync<AnalyticsOverview>(ShipmentsApiRoutes.AnalyticsOverview + Query(("days", days)), ct);

    public Task<DemandInsights> GetInsightsAsync(int days, CancellationToken ct = default) =>
        GetAsync<DemandInsights>(ShipmentsApiRoutes.Insights + Query(("days", days)), ct);

    /// <summary>Download link for the anonymised open-data CSV (the browser sends the sign-in cookie).</summary>
    public static string OpenDataUrl(int days) => ShipmentsApiRoutes.InsightsOpenData + Query(("days", days));

    // Warehouses
    public Task<NetworkHubs> GetNetworkHubsAsync(CancellationToken ct = default) => GetAsync<NetworkHubs>(ShipmentsApiRoutes.NetworkHubs, ct);

    public Task<WarehouseList> ListWarehousesAsync(CancellationToken ct = default) => GetAsync<WarehouseList>(ShipmentsApiRoutes.Warehouses, ct);

    public Task<WarehouseDetails?> GetWarehouseAsync(Guid id, CancellationToken ct = default) => FindAsync<WarehouseDetails>(ShipmentsApiRoutes.WarehouseById(id), ct);

    public Task<WarehouseDetails> CreateWarehouseAsync(WarehouseRequest request, CancellationToken ct = default) =>
        SendAsync<WarehouseDetails>(HttpMethod.Post, ShipmentsApiRoutes.Warehouses, request, null, ct);

    public Task<WarehouseDetails> UpdateWarehouseAsync(Guid id, WarehouseRequest request, uint version, CancellationToken ct = default) =>
        SendAsync<WarehouseDetails>(HttpMethod.Put, ShipmentsApiRoutes.WarehouseById(id), request, version, ct);

    /// <summary>Reloads the company's network into <see cref="Ui.HubCatalog"/> (after warehouses change, or at start-up).</summary>
    public async Task RefreshNetworkAsync(CancellationToken ct = default) => Ui.HubCatalog.Set((await GetNetworkHubsAsync(ct)).Hubs);
}

public sealed record TransferRef(Guid Id, string Number);
