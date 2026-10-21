using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Web.Client.Services;

/// <summary>Typed client for the seller portal (the signed-in shop's own orders and money).</summary>
internal sealed class SellerApi(HttpClient http) : ApiClient(http)
{
    public Task<SellerHome> GetHomeAsync(CancellationToken ct = default) => GetAsync<SellerHome>(ShipmentsApiRoutes.Seller, ct);

    public Task<SellerOrderList> GetOrdersAsync(string? q, SellerOrderView view, int page, CancellationToken ct = default) =>
        GetAsync<SellerOrderList>(ShipmentsApiRoutes.SellerOrders + Query(("q", q), ("view", view == SellerOrderView.All ? null : view), ("page", page > 1 ? page : null)), ct);

    public Task<CreateShipmentResponse> BookAsync(CreateShipmentRequest request, CancellationToken ct = default) =>
        SendAsync<CreateShipmentResponse>(HttpMethod.Post, ShipmentsApiRoutes.SellerOrders, request, null, ct);

    public Task<PriceQuote> QuoteAsync(string destination, decimal weightKg, CancellationToken ct = default) =>
        GetAsync<PriceQuote>(ShipmentsApiRoutes.SellerQuote + Query(("destination", destination), ("weightKg", weightKg)), ct);

    public Task CancelAsync(Guid id, CancellationToken ct = default) => SendAsync(HttpMethod.Post, ShipmentsApiRoutes.SellerCancel(id), null, null, ct);

    public string StatementUrl(Guid settlementId) => ShipmentsApiRoutes.SellerStatement(settlementId);
}
