using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Logistico.Modules.Merchants.Contracts;
using Logistico.Modules.Shipments.Contracts;

namespace Logistico.Web.IntegrationTests;

internal static class ApiTestHelpers
{
    public static CreateShipmentRequest InternationalBooking(string shipper) => new()
    {
        ServiceType = ServiceType.International,
        Mode = TransportMode.SeaLcl,
        ShipperName = shipper,
        ConsigneeName = "Erbil Mart",
        ConsigneePhone = "+9647501234567",
        OriginCode = "TRMER",
        DestinationCode = "IQUQR",
        Pieces = 4,
        GrossWeightKg = 900,
        VolumeCbm = 1.5m,
        DeclaredValue = 12_000,
        DeclaredCurrency = "USD",
        CodCurrency = null,
    };

    public static CreateShipmentRequest DomesticOrder(Guid merchantId, string customer) => new()
    {
        ServiceType = ServiceType.Domestic,
        MerchantId = merchantId,
        ExternalReference = "IG-7781",
        ConsigneeName = customer,
        ConsigneePhone = "0781 222 3344",
        OriginCode = "IQ-BG",
        DestinationCode = "IQ-BA",
        DestinationCity = "العشار",
        DeliveryAddress = "محلة 112، زقاق 4، دار 17",
        Landmark = "قرب جامع العشار",
        ItemsDescription = "فستان سهرة",
        CodAmount = 65_000,
        CodCurrency = "IQD",
        DeliveryFee = 8_000,
    };

    public static async Task<CreateShipmentResponse> BookAsync(this HttpClient client, CreateShipmentRequest request)
    {
        var response = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.Shipments, request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreateShipmentResponse>())!;
    }

    public static async Task<Guid> CreateMerchantAsync(this HttpClient client, string name, string handle)
    {
        var response = await client.PostAsJsonAsync("/" + MerchantsApiRoutes.Merchants, new MerchantRequest
        {
            Name = name,
            InstagramHandle = handle,
            ContactName = "Owner",
            Phone = "0770 111 2233",
            Governorate = "IQ-BG",
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreateMerchantResponse>())!.Id;
    }

    public static async Task<HttpResponseMessage> RecordAsync(this HttpClient client, Guid id, RecordShipmentEventRequest request, EntityTagHeaderValue? etag)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/" + ShipmentsApiRoutes.Events(id)) { Content = JsonContent.Create(request) };
        if (etag is not null)
        {
            message.Headers.IfMatch.Add(etag);
        }

        return await client.SendAsync(message);
    }

    public static async Task<EntityTagHeaderValue> ETagAsync(this HttpClient client, Guid id) =>
        (await client.GetAsync("/" + ShipmentsApiRoutes.ById(id))).Headers.ETag!;

    public static async Task<ShipmentDetails> GetShipmentAsync(this HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<ShipmentDetails>("/" + ShipmentsApiRoutes.ById(id)))!;

    /// <summary>One hub scan; asserts it succeeded.</summary>
    public static async Task<ScanOutcome> ScanAsync(this HttpClient client, ScanOperation operation, string trackingNumber, string hub,
        string? bin = null, string? courier = null, Guid? transferId = null, DeliveryFailureReason? reason = null)
    {
        var response = await client.TryScanAsync(operation, trackingNumber, hub, bin, courier, transferId, reason);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ScanOutcome>())!;
    }

    public static Task<HttpResponseMessage> TryScanAsync(this HttpClient client, ScanOperation operation, string trackingNumber, string hub,
        string? bin = null, string? courier = null, Guid? transferId = null, DeliveryFailureReason? reason = null) =>
        client.PostAsJsonAsync("/" + ShipmentsApiRoutes.WarehouseScan, new ScanRequest
        {
            Operation = operation,
            TrackingNumber = trackingNumber,
            HubCode = hub,
            WarehouseBin = bin,
            CourierName = courier,
            TransferId = transferId,
            FailureReason = reason,
        });

    /// <summary>Loads the parcel on the truck planned for it, sends the truck off and unloads it at the other hub.</summary>
    public static async Task<TransferDetails> TruckAsync(this HttpClient client, string trackingNumber, string from, string to)
    {
        var loaded = await client.ScanAsync(ScanOperation.Load, trackingNumber, from);
        Assert.NotNull(loaded.TransferNumber);
        var list = (await client.GetFromJsonAsync<TransferListResult>("/" + ShipmentsApiRoutes.Transfers + "?status=Loading&hub=" + from))!;
        var truck = list.Items.Single(t => t.Number == loaded.TransferNumber);
        Assert.Equal(to, truck.ToHubCode);

        var get = await client.GetAsync("/" + ShipmentsApiRoutes.TransferById(truck.Id));
        using (var depart = new HttpRequestMessage(HttpMethod.Post, "/" + ShipmentsApiRoutes.TransferDepart(truck.Id))
        {
            Content = JsonContent.Create(new DepartTransferRequest { VehiclePlate = "BG-55102", DriverName = "سلام", SealNumber = "S-1" }),
        })
        {
            depart.Headers.IfMatch.Add(get.Headers.ETag!);
            var departed = await client.SendAsync(depart);
            Assert.True(departed.IsSuccessStatusCode, await departed.Content.ReadAsStringAsync());
        }

        await client.ScanAsync(ScanOperation.Receive, trackingNumber, to, "A-01-1");
        return (await client.GetFromJsonAsync<TransferDetails>("/" + ShipmentsApiRoutes.TransferById(truck.Id)))!;
    }

    public static async Task<AutomationEngineResult> RunAutomationAsync(this HttpClient client)
    {
        var response = await client.PostAsync("/" + ShipmentsApiRoutes.AutomationRun, null);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AutomationEngineResult>())!;
    }

    public static Task<HttpResponseMessage> PutWithETagAsync<T>(this HttpClient client, string route, T body, EntityTagHeaderValue etag) =>
        client.SendWithETagAsync(HttpMethod.Put, route, body, etag);

    public static Task<HttpResponseMessage> PostWithETagAsync<T>(this HttpClient client, string route, T body, EntityTagHeaderValue etag) =>
        client.SendWithETagAsync(HttpMethod.Post, route, body, etag);

    private static async Task<HttpResponseMessage> SendWithETagAsync<T>(this HttpClient client, HttpMethod method, string route, T body, EntityTagHeaderValue etag)
    {
        using var message = new HttpRequestMessage(method, "/" + route) { Content = JsonContent.Create(body) };
        message.Headers.IfMatch.Add(etag);
        return await client.SendAsync(message);
    }

    /// <summary>Registers courier "حيدر (Baghdad-02)" in the client's company (once) and returns its id.</summary>
    public static async Task<Guid> EnsureCourierAsync(this HttpClient client, string code = "Baghdad-02", string name = "حيدر")
    {
        var known = (await client.GetFromJsonAsync<List<CourierLookupItem>>("/" + ShipmentsApiRoutes.FleetLookup))!.FirstOrDefault(c => c.Code == code);
        if (known is not null)
        {
            return known.Id;
        }

        var response = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.FleetCouriers, new CourierRequest { Code = code, Name = name, Phone = "07802223344", Governorate = "IQ-BG" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await client.GetFromJsonAsync<List<CourierLookupItem>>("/" + ShipmentsApiRoutes.FleetLookup))!.Single(c => c.Code == code).Id;
    }

    /// <summary>The courier hands in all the cash they collected, counted exactly.</summary>
    public static async Task<CashHandoverItem> HandInCashAsync(this HttpClient client, Guid courierId)
    {
        var cash = (await client.GetFromJsonAsync<CourierCash>("/" + ShipmentsApiRoutes.CourierCash(courierId)))!;
        var response = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierCash(courierId), new ReceiveCashRequest { ReceivedIqd = cash.ExpectedIqd });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CashHandoverItem>())!;
    }
}

public sealed record AutomationEngineResult(bool Ran, int Raised, int Resolved, int Planned, int ReturnsReady, int AutoCancelled = 0);
