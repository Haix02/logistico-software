using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Web.IntegrationTests;

/// <summary>
/// What the tracking history says stays true: steps that belong to a scan, a truck manifest, the returns desk or
/// customs cannot be typed on the details page, and a cancelled order stops asking the customer to confirm it.
/// </summary>
[Collection("Api")]
public sealed class OrderTrackingIntegrityTests(LogisticoApiFactory factory)
{
    private static async Task AssertRefusedAsync(HttpResponseMessage response, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity, body);
        Assert.Contains(code, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_return_cannot_be_marked_returned_by_hand_it_is_handed_over_against_a_manifest()
    {
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var shop = await client.CreateMerchantAsync("متجر المرتجع اليدوي", "manual.returned.test");
        var order = await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون ألغى"));
        await client.ScanAsync(ScanOperation.Receive, order.TrackingNumber, IraqHubs.Baghdad, "A-01-1");
        var cancel = await client.PostWithETagAsync(ShipmentsApiRoutes.CancelOrder(order.Id), new CancelOrderRequest { Reason = "ألغاه التاجر" }, await client.ETagAsync(order.Id));
        Assert.True(cancel.IsSuccessStatusCode, await cancel.Content.ReadAsStringAsync());
        Assert.Equal(ShipmentStatus.ReturnAtHub, (await client.GetShipmentAsync(order.Id)).Status);

        var byHand = await client.RecordAsync(order.Id, new RecordShipmentEventRequest { Status = ShipmentStatus.Returned }, await client.ETagAsync(order.Id));

        await AssertRefusedAsync(byHand, "shipment.step_by_workflow");
        var still = await client.GetShipmentAsync(order.Id);
        Assert.Equal(ShipmentStatus.ReturnAtHub, still.Status);
        Assert.Null(still.ReturnManifestNumber);

        var handOver = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.ReturnsHandOver, new HandOverReturnsRequest { ShipmentIds = [order.Id], ReceivedBy = "مندوب المتجر" });
        Assert.True(handOver.IsSuccessStatusCode, await handOver.Content.ReadAsStringAsync());
        var returned = await client.GetShipmentAsync(order.Id);
        Assert.Equal(ShipmentStatus.Returned, returned.Status);
        Assert.NotNull(returned.ReturnManifestNumber);
    }

    [Fact]
    public async Task A_parcel_on_a_truck_is_not_put_between_hubs_or_on_a_shelf_by_hand_the_scan_does_it_and_completes_the_manifest()
    {
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var shop = await client.CreateMerchantAsync("متجر الشاحنة اليدوية", "manual.truck.test");
        var order = await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون البصرة"));
        await client.ScanAsync(ScanOperation.Receive, order.TrackingNumber, IraqHubs.Baghdad, "A-01-1");

        // Between hubs without a truck: refused.
        var noTruck = await client.RecordAsync(order.Id, new RecordShipmentEventRequest { Status = ShipmentStatus.InTransitToHub }, await client.ETagAsync(order.Id));
        await AssertRefusedAsync(noTruck, "shipment.step_by_workflow");
        Assert.Equal(ShipmentStatus.AtWarehouse, (await client.GetShipmentAsync(order.Id)).Status);

        // On the truck and on the road.
        var loaded = await client.ScanAsync(ScanOperation.Load, order.TrackingNumber, IraqHubs.Baghdad);
        var truck = (await client.GetFromJsonAsync<TransferListResult>("/" + ShipmentsApiRoutes.Transfers + "?status=Loading&hub=" + IraqHubs.Baghdad))!
            .Items.Single(t => t.Number == loaded.TransferNumber);
        var get = await client.GetAsync("/" + ShipmentsApiRoutes.TransferById(truck.Id));
        var depart = await client.PostWithETagAsync(ShipmentsApiRoutes.TransferDepart(truck.Id), new DepartTransferRequest { VehiclePlate = "BG-1", DriverName = "علي" }, get.Headers.ETag!);
        Assert.True(depart.IsSuccessStatusCode, await depart.Content.ReadAsStringAsync());

        // Shelved at Basra by hand: refused, because the truck's manifest would still show it on board.
        var byHand = await client.RecordAsync(order.Id, new RecordShipmentEventRequest { Status = ShipmentStatus.AtWarehouse, Location = IraqHubs.Basra }, await client.ETagAsync(order.Id));
        await AssertRefusedAsync(byHand, "shipment.step_by_workflow");
        Assert.Equal(ShipmentStatus.InTransitToHub, (await client.GetShipmentAsync(order.Id)).Status);

        await client.ScanAsync(ScanOperation.Receive, order.TrackingNumber, IraqHubs.Basra, "B-01-1");
        var manifest = (await client.GetFromJsonAsync<TransferDetails>("/" + ShipmentsApiRoutes.TransferById(truck.Id)))!;
        Assert.Equal(TransferStatus.Received, manifest.Summary.Status);
        Assert.Equal(TransferItemState.Received, Assert.Single(manifest.Items).State);
        Assert.Equal(ShipmentStatus.AtWarehouse, (await client.GetShipmentAsync(order.Id)).Status);
    }

    [Fact]
    public async Task Customs_steps_go_through_the_customs_workbench_not_the_details_page()
    {
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var freight = await client.BookAsync(ApiTestHelpers.InternationalBooking("Mersin Customs Gate"));
        foreach (var step in new[] { ShipmentStatus.ReceivedAtOrigin, ShipmentStatus.DepartedOrigin, ShipmentStatus.ArrivedPortOfEntry })
        {
            var recorded = await client.RecordAsync(freight.Id, new RecordShipmentEventRequest { Status = step }, await client.ETagAsync(freight.Id));
            Assert.True(recorded.IsSuccessStatusCode, await recorded.Content.ReadAsStringAsync());
        }

        foreach (var step in new[] { ShipmentStatus.CustomsPending, ShipmentStatus.Released })
        {
            var byHand = await client.RecordAsync(freight.Id, new RecordShipmentEventRequest { Status = step }, await client.ETagAsync(freight.Id));
            await AssertRefusedAsync(byHand, "shipment.step_by_workflow");
        }

        Assert.Equal(ShipmentStatus.ArrivedPortOfEntry, (await client.GetShipmentAsync(freight.Id)).Status);
    }

    [Fact]
    public async Task A_cancelled_order_stops_asking_the_customer_to_confirm_and_cannot_be_confirmed_any_more()
    {
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var shop = await client.CreateMerchantAsync("متجر الإلغاء قبل التأكيد", "cancel.before.confirm");
        var policyResponse = await client.GetAsync("/" + ShipmentsApiRoutes.AutomationPolicy);
        var policy = (await policyResponse.Content.ReadFromJsonAsync<AutomationPolicy>())!;
        policy.OrderConfirmation = OrderConfirmationMode.AllOrders;
        Assert.True((await client.PutWithETagAsync(ShipmentsApiRoutes.AutomationPolicy, policy, policyResponse.Headers.ETag!)).IsSuccessStatusCode);

        var order = await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون غيّر رأيه"));
        var anonymous = factory.CreateCustomerClient();
        Assert.True((await anonymous.GetFromJsonAsync<PublicTrackingView>("/" + ShipmentsApiRoutes.Track(order.TrackingNumber)))!.AwaitingConfirmation);

        var cancel = await client.PostWithETagAsync(ShipmentsApiRoutes.CancelOrder(order.Id), new CancelOrderRequest { Reason = "التاجر ألغى" }, await client.ETagAsync(order.Id));
        Assert.True(cancel.IsSuccessStatusCode, await cancel.Content.ReadAsStringAsync());

        var view = (await anonymous.GetFromJsonAsync<PublicTrackingView>("/" + ShipmentsApiRoutes.Track(order.TrackingNumber)))!;
        Assert.Equal(ShipmentStatus.Cancelled, view.Status);
        Assert.False(view.AwaitingConfirmation);

        var lateYes = await anonymous.PostAsJsonAsync("/" + ShipmentsApiRoutes.TrackConfirm(order.TrackingNumber), new CustomerConfirmation { Confirm = true, PhoneLast4 = "3344" });
        await AssertRefusedAsync(lateYes, "shipment.confirmation_not_pending");
        var details = await client.GetShipmentAsync(order.Id);
        Assert.Equal((ShipmentStatus.Cancelled, ConfirmationStatus.Pending), (details.Status, details.Confirmation));
    }
}
