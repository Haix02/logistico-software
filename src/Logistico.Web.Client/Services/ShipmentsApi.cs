using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel;

namespace Logistico.Web.Client.Services;

/// <summary>Typed client for shipments, customs, fleet and finance.</summary>
internal sealed class ShipmentsApi(HttpClient http) : ApiClient(http)
{
    public Task<ShipmentListResult> ListAsync(ShipmentQuery query, CancellationToken ct = default) =>
        GetAsync<ShipmentListResult>(ShipmentsApiRoutes.Shipments + query.ToQueryString(), ct);

    public string ExportUrl(ShipmentQuery query) => ShipmentsApiRoutes.Export + query.ToQueryString();

    public Task<ShipmentsSummary> GetSummaryAsync(Guid? merchantId = null, CancellationToken ct = default) =>
        GetAsync<ShipmentsSummary>(ShipmentsApiRoutes.Summary + Query(("merchantId", merchantId)), ct);

    public Task<ShipmentDetails?> GetAsync(Guid id, CancellationToken ct = default) => FindAsync<ShipmentDetails>(ShipmentsApiRoutes.ById(id), ct);

    public Task<CreateShipmentResponse> CreateAsync(CreateShipmentRequest request, CancellationToken ct = default) =>
        SendAsync<CreateShipmentResponse>(HttpMethod.Post, ShipmentsApiRoutes.Shipments, request, null, ct);

    public Task<BulkBookOrdersResponse> BulkBookAsync(BulkBookOrdersRequest request, CancellationToken ct = default) =>
        SendAsync<BulkBookOrdersResponse>(HttpMethod.Post, ShipmentsApiRoutes.BulkBook, request, null, ct);

    public async Task<IReadOnlyList<ShipmentListItem>> GetExactAsync(IEnumerable<string> references, CancellationToken ct = default) =>
        await SendAsync<List<ShipmentListItem>>(HttpMethod.Post, ShipmentsApiRoutes.ExactShipments,
            new ShipmentReferencesRequest { References = references.Cast<string?>().ToList() }, null, ct);

    public Task<ShipmentDetails> RecordEventAsync(Guid id, uint version, RecordShipmentEventRequest request, CancellationToken ct = default) =>
        SendAsync<ShipmentDetails>(HttpMethod.Post, ShipmentsApiRoutes.Events(id), request, version, ct);

    public Task<ShipmentDetails> StartReturnAsync(Guid id, uint version, StartReturnRequest request, CancellationToken ct = default) =>
        SendAsync<ShipmentDetails>(HttpMethod.Post, ShipmentsApiRoutes.StartReturn(id), request, version, ct);

    public Task<ShipmentDetails> UpdateOrderAsync(Guid id, uint version, UpdateOrderRequest request, CancellationToken ct = default) =>
        SendAsync<ShipmentDetails>(HttpMethod.Put, ShipmentsApiRoutes.ById(id), request, version, ct);

    public Task<ConfirmationQueue> GetConfirmationsAsync(string? q = null, Guid? merchantId = null, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        GetAsync<ConfirmationQueue>(ShipmentsApiRoutes.Confirmations + Query(("q", q), ("merchantId", merchantId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<ShipmentDetails> RecordConfirmationAsync(Guid id, uint version, ConfirmationOutcome outcome, string? note = null, CancellationToken ct = default) =>
        SendAsync<ShipmentDetails>(HttpMethod.Post, ShipmentsApiRoutes.RecordConfirmation(id), new RecordConfirmationRequest { Outcome = outcome, Note = note }, version, ct);

    /// <summary>The customer confirms (or declines) on the public tracking page.</summary>
    public Task ConfirmAsCustomerAsync(string trackingNumber, string phoneLast4, bool confirm, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, ShipmentsApiRoutes.TrackConfirm(trackingNumber), new CustomerConfirmation { PhoneLast4 = phoneLast4, Confirm = confirm }, null, ct);

    public Task<Logistico.Modules.Shipments.Contracts.Rules.RateCard> GetPricingAsync(CancellationToken ct = default) =>
        GetAsync<Logistico.Modules.Shipments.Contracts.Rules.RateCard>(ShipmentsApiRoutes.Pricing, ct);

    public Task<Logistico.Modules.Shipments.Contracts.Rules.RateCard> UpdatePricingAsync(Logistico.Modules.Shipments.Contracts.Rules.RateCard card, CancellationToken ct = default) =>
        SendAsync<Logistico.Modules.Shipments.Contracts.Rules.RateCard>(HttpMethod.Put, ShipmentsApiRoutes.Pricing, card, card.Version, ct);

    /// <summary>The customer rates the delivery on the public tracking page.</summary>
    public Task RateAsCustomerAsync(string trackingNumber, CustomerRating rating, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, ShipmentsApiRoutes.TrackRating(trackingNumber), rating, null, ct);

    public Task<ShipmentDetails> CancelOrderAsync(Guid id, uint version, string reason, CancellationToken ct = default) =>
        SendAsync<ShipmentDetails>(HttpMethod.Post, ShipmentsApiRoutes.CancelOrder(id), new CancelOrderRequest { Reason = reason }, version, ct);

    /// <summary>History and last address for a mobile number; null while the number is incomplete.</summary>
    public async Task<CustomerProfile?> LookupCustomerAsync(string phone, CancellationToken ct = default)
    {
        if (!Logistico.SharedKernel.Iraq.IraqPhone.IsValid(phone))
        {
            return null;
        }

        return await GetAsync<CustomerProfile>(ShipmentsApiRoutes.CustomerLookup + Query(("phone", phone)), ct);
    }

    public Task<PublicTrackingView?> TrackAsync(string trackingNumber, CancellationToken ct = default) =>
        FindAsync<PublicTrackingView>(ShipmentsApiRoutes.Track(trackingNumber), ct);

    public Task<CustomerRequestReceipt> SendCustomerRequestAsync(string trackingNumber, CustomerRequest request, CancellationToken ct = default) =>
        SendAsync<CustomerRequestReceipt>(HttpMethod.Post, ShipmentsApiRoutes.TrackRequests(trackingNumber), request, null, ct);

    public Task<CustomsSummary> GetCustomsSummaryAsync(string? q = null, ShipmentStatus? status = null, CustomsLane? lane = null, string? office = null,
        int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        GetAsync<CustomsSummary>(ShipmentsApiRoutes.Customs + Query(("q", q), ("status", status), ("lane", lane), ("office", office), ("page", page), ("pageSize", pageSize)), ct);

    public Task<CustomsQueueItem?> GetCustomsByShipmentAsync(Guid shipmentId, CancellationToken ct = default) =>
        FindAsync<CustomsQueueItem>(ShipmentsApiRoutes.CustomsByShipment(shipmentId), ct);

    /// <summary>Saves the declaration; <paramref name="version"/> is the one the form was opened with (null for a new one).</summary>
    public Task<CustomsQueueItem> UpsertCustomsAsync(Guid shipmentId, UpsertCustomsDeclarationRequest request, uint? version, CancellationToken ct = default) =>
        SendAsync<CustomsQueueItem>(HttpMethod.Put, ShipmentsApiRoutes.CustomsByShipment(shipmentId), request, version, ct);

    public string SadXmlUrl(Guid shipmentId) => ShipmentsApiRoutes.CustomsSadXml(shipmentId);

    public Task<FleetOverviewResult> GetFleetOverviewAsync(string? q = null, string? governorate = null, bool? activeOnly = null, CancellationToken ct = default) =>
        GetAsync<FleetOverviewResult>(ShipmentsApiRoutes.Fleet + Query(("q", q), ("governorate", governorate), ("activeOnly", activeOnly)), ct);

    public Task<CourierCash> GetCourierCashAsync(Guid courierId, CancellationToken ct = default) =>
        GetAsync<CourierCash>(ShipmentsApiRoutes.CourierCash(courierId), ct);

    public Task<CashHandoverItem> ReceiveCashAsync(Guid courierId, ReceiveCashRequest request, CancellationToken ct = default) =>
        SendAsync<CashHandoverItem>(HttpMethod.Post, ShipmentsApiRoutes.CourierCash(courierId), request, null, ct);

    public Task<PagedResult<CashHandoverItem>> ListCashHandoversAsync(Guid? courierId = null, int page = 1, int pageSize = 10, CancellationToken ct = default) =>
        GetAsync<PagedResult<CashHandoverItem>>(ShipmentsApiRoutes.CashHandovers + Query(("courierId", courierId), ("page", page), ("pageSize", pageSize)), ct);

    /// <summary>The signed-in person's saved views of a list.</summary>
    public async Task<IReadOnlyList<SavedViewItem>> ListViewsAsync(string list, CancellationToken ct = default) =>
        await GetAsync<List<SavedViewItem>>(ShipmentsApiRoutes.Views + Query(("list", list)), ct);

    public Task<SavedViewItem> SaveViewAsync(SaveViewRequest request, CancellationToken ct = default) =>
        SendAsync<SavedViewItem>(HttpMethod.Post, ShipmentsApiRoutes.Views, request, null, ct);

    public Task DeleteViewAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, ShipmentsApiRoutes.ViewById(id), null, null, ct);

    public async Task<IReadOnlyList<CourierLookupItem>> LookupCouriersAsync(CancellationToken ct = default) =>
        await GetAsync<List<CourierLookupItem>>(ShipmentsApiRoutes.FleetLookup, ct);

    public Task<CourierListItem> CreateCourierAsync(CourierRequest request, CancellationToken ct = default) =>
        SendAsync<CourierListItem>(HttpMethod.Post, ShipmentsApiRoutes.FleetCouriers, request, null, ct);

    public Task<CourierListItem> UpdateCourierAsync(Guid id, uint version, CourierRequest request, CancellationToken ct = default) =>
        SendAsync<CourierListItem>(HttpMethod.Put, ShipmentsApiRoutes.CourierById(id), request, version, ct);

    public Task<FinanceSummary> GetFinanceSummaryAsync(Guid? merchantId = null, CancellationToken ct = default) =>
        GetAsync<FinanceSummary>(ShipmentsApiRoutes.FinanceSummary + Query(("merchantId", merchantId)), ct);

    public Task<CodSettlementItem> CreateSettlementAsync(CreateCodSettlementRequest request, CancellationToken ct = default) =>
        SendAsync<CodSettlementItem>(HttpMethod.Post, ShipmentsApiRoutes.FinanceSettlements, request, null, ct);

    public string SettlementExportUrl(Guid id) => ShipmentsApiRoutes.SettlementExport(id);
}
