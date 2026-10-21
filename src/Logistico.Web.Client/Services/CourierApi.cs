using Logistico.Modules.Shipments.Contracts;

namespace Logistico.Web.Client.Services;

/// <summary>Typed client for the courier app (the signed-in courier's own run).</summary>
internal sealed class CourierApi(HttpClient http) : ApiClient(http)
{
    public Task<CourierRun> GetRunAsync(CancellationToken ct = default) => GetAsync<CourierRun>(ShipmentsApiRoutes.CourierRun, ct);

    public Task<ScanOutcome> DeliveredAsync(Guid shipmentId, CancellationToken ct = default) =>
        SendAsync<ScanOutcome>(HttpMethod.Post, ShipmentsApiRoutes.CourierDelivered(shipmentId), null, null, ct);

    public Task<ScanOutcome> FailedAsync(Guid shipmentId, DeliveryFailureReason reason, CancellationToken ct = default) =>
        SendAsync<ScanOutcome>(HttpMethod.Post, ShipmentsApiRoutes.CourierFailed(shipmentId), new CourierFailureRequest { Reason = reason }, null, ct);
}
