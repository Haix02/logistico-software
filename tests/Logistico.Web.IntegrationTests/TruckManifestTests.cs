using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Web.IntegrationTests;

/// <summary>Truck manifests stay true to what is on the truck when parcels change course, are mis-scanned or a hub closes.</summary>
[Collection("Api")]
public sealed class TruckManifestTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task A_parcel_cancelled_while_waiting_for_its_truck_comes_off_the_manifest_and_the_truck_still_leaves()
    {
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var shop = await client.CreateMerchantAsync("متجر الشاحنة", "truck.cancel.test");
        var cancelled = await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون ألغى"));
        var travelling = await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون ينتظر"));
        await client.ScanAsync(ScanOperation.Receive, cancelled.TrackingNumber, IraqHubs.Baghdad, "A-01-1");
        await client.ScanAsync(ScanOperation.Receive, travelling.TrackingNumber, IraqHubs.Baghdad, "A-01-2");
        var loaded = await client.ScanAsync(ScanOperation.Load, cancelled.TrackingNumber, IraqHubs.Baghdad);
        var truckId = await TruckIdAsync(client, loaded.TransferNumber!);

        // The seller cancels after it was loaded: it is still at the seller's hub, so it waits there for hand-over.
        var cancel = await client.PostWithETagAsync(ShipmentsApiRoutes.CancelOrder(cancelled.Id), new CancelOrderRequest { Reason = "الزبون ألغى الطلب" }, await client.ETagAsync(cancelled.Id));
        Assert.True(cancel.IsSuccessStatusCode, await cancel.Content.ReadAsStringAsync());
        Assert.Equal(ShipmentStatus.ReturnAtHub, (await client.GetShipmentAsync(cancelled.Id)).Status);
        Assert.DoesNotContain((await TruckAsync(client, truckId)).Items, i => i.ShipmentId == cancelled.Id);

        // The truck leaves with the other parcel.
        await client.ScanAsync(ScanOperation.Load, travelling.TrackingNumber, IraqHubs.Baghdad, transferId: truckId);
        var get = await client.GetAsync("/" + ShipmentsApiRoutes.TransferById(truckId));
        var depart = await client.PostWithETagAsync(ShipmentsApiRoutes.TransferDepart(truckId), new DepartTransferRequest { VehiclePlate = "BG-1", DriverName = "علي" }, get.Headers.ETag!);
        Assert.True(depart.IsSuccessStatusCode, await depart.Content.ReadAsStringAsync());
        Assert.Equal(ShipmentStatus.InTransitToHub, (await client.GetShipmentAsync(travelling.Id)).Status);

        // A hand-over manifest is signed for one seller only.
        var otherShop = await client.CreateMerchantAsync("متجر آخر", "truck.cancel.other");
        var otherParcel = await client.BookAsync(ApiTestHelpers.DomesticOrder(otherShop, "زبون متجر آخر"));
        await client.ScanAsync(ScanOperation.Receive, otherParcel.TrackingNumber, IraqHubs.Baghdad, "A-01-3");
        await client.PostWithETagAsync(ShipmentsApiRoutes.CancelOrder(otherParcel.Id), new CancelOrderRequest { Reason = "ألغاه التاجر" }, await client.ETagAsync(otherParcel.Id));
        var mixed = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.ReturnsHandOver, new HandOverReturnsRequest { ShipmentIds = [cancelled.Id, otherParcel.Id] });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mixed.StatusCode);
        Assert.Contains("return.mixed_sellers", await mixed.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var handOver = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.ReturnsHandOver, new HandOverReturnsRequest { ShipmentIds = [cancelled.Id], ReceivedBy = "مندوب المتجر" });
        Assert.True(handOver.IsSuccessStatusCode, await handOver.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Scanning_a_parcel_onto_the_wrong_truck_leaves_it_on_its_own_truck()
    {
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var shop = await client.CreateMerchantAsync("متجر المسح", "truck.wrong.scan");
        var parcel = await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون البصرة"));
        await client.ScanAsync(ScanOperation.Receive, parcel.TrackingNumber, IraqHubs.Baghdad, "A-01-1");
        var basraTruck = await TruckIdAsync(client, (await client.ScanAsync(ScanOperation.Load, parcel.TrackingNumber, IraqHubs.Baghdad)).TransferNumber!);

        var opened = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.Transfers, new CreateTransferRequest { FromHubCode = IraqHubs.Baghdad, ToHubCode = IraqHubs.Erbil });
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        var erbilTruck = (await opened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var wrong = await client.TryScanAsync(ScanOperation.Load, parcel.TrackingNumber, IraqHubs.Baghdad, transferId: erbilTruck);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
        Assert.Equal("transfer.wrong_lane", (await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Contains((await TruckAsync(client, basraTruck)).Items, i => i.ShipmentId == parcel.Id && i.State == TransferItemState.Loaded);
        Assert.Empty((await TruckAsync(client, erbilTruck)).Items);
    }

    [Fact]
    public async Task Retiring_a_hub_sends_its_incoming_parcels_to_the_hub_now_covering_them_and_automation_keeps_running()
    {
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());

        // Trucks are planned by hand in this company for now, so no truck runs to Najaf.
        await SetAutoPlanAsync(client, false);
        var shop = await client.CreateMerchantAsync("متجر النجف", "truck.retire.test");
        var order = ApiTestHelpers.DomesticOrder(shop, "زبون النجف");
        order.DestinationCode = "IQ-NA";
        var parcel = await client.BookAsync(order);
        await client.ScanAsync(ScanOperation.Receive, parcel.TrackingNumber, IraqHubs.Baghdad, "A-01-1");
        Assert.Equal(IraqHubs.Najaf, (await client.GetShipmentAsync(parcel.Id)).DestinationHubCode);

        var najaf = (await client.GetFromJsonAsync<WarehouseList>("/" + ShipmentsApiRoutes.Warehouses))!.Items.Single(w => w.Code == IraqHubs.Najaf);
        var retire = WarehouseRequest.From(najaf);
        retire.IsActive = false;
        var get = await client.GetAsync("/" + ShipmentsApiRoutes.WarehouseById(najaf.Id));
        var retired = await client.PutWithETagAsync(ShipmentsApiRoutes.WarehouseById(najaf.Id), retire, get.Headers.ETag!);
        Assert.True(retired.IsSuccessStatusCode, await retired.Content.ReadAsStringAsync());

        var rerouted = await client.GetShipmentAsync(parcel.Id);
        Assert.NotEqual(IraqHubs.Najaf, rerouted.DestinationHubCode);

        // Planning trucks again works for every waiting parcel (before, one parcel bound for the retired hub failed the sweep).
        await SetAutoPlanAsync(client, true);
        await client.RunAutomationAsync();
    }

    private static async Task SetAutoPlanAsync(HttpClient client, bool on)
    {
        var response = await client.GetAsync("/" + ShipmentsApiRoutes.AutomationPolicy);
        var policy = (await response.Content.ReadFromJsonAsync<AutomationPolicy>())!;
        policy.AutoPlanTransfers = on;
        var saved = await client.PutWithETagAsync(ShipmentsApiRoutes.AutomationPolicy, policy, response.Headers.ETag!);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> TruckIdAsync(HttpClient client, string number) =>
        (await client.GetFromJsonAsync<TransferListResult>("/" + ShipmentsApiRoutes.Transfers + "?q=" + number))!.Items.Single(t => t.Number == number).Id;

    private static async Task<TransferDetails> TruckAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<TransferDetails>("/" + ShipmentsApiRoutes.TransferById(id)))!;
}
