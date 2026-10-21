using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Web.IntegrationTests;

/// <summary>Staff recognise returning customers, correct orders before dispatch and cancel them safely.</summary>
[Collection("Api")]
public sealed class OrderEditingTests(LogisticoApiFactory factory)
{
    private const string Phone = "07812223344";

    [Fact]
    public async Task Customers_are_recognised_by_phone_and_orders_can_be_corrected_or_cancelled_until_dispatch()
    {
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var shop = await client.CreateMerchantAsync("متجر التعديل", "edit.flow.test");

        // A number never seen before.
        var unknown = (await client.GetFromJsonAsync<CustomerProfile>("/" + ShipmentsApiRoutes.CustomerLookup + "?phone=" + Phone))!;
        Assert.Equal(0, unknown.Orders);
        Assert.Equal(CustomerRisk.New, unknown.Risk);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/" + ShipmentsApiRoutes.CustomerLookup + "?phone=123")).StatusCode);

        // After one order, the next one finds the name, last address and the order in progress (a possible duplicate).
        var first = await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زينب كاظم"));
        var known = (await client.GetFromJsonAsync<CustomerProfile>("/" + ShipmentsApiRoutes.CustomerLookup + "?phone=0781 222 3344"))!;
        Assert.Equal("زينب كاظم", known.Name);
        Assert.Equal("IQ-BA", known.Governorate);
        Assert.Equal(first.TrackingNumber, Assert.Single(known.Active).TrackingNumber);
        Assert.Equal(shop, known.Active[0].MerchantId);

        // Correct the order: new mobile and a move to Najaf (its hub changes); the history says what changed.
        var order = await client.GetShipmentAsync(first.Id);
        var edit = UpdateOrderRequest.From(order);
        edit.ConsigneePhone = "07701112233";
        edit.DestinationCode = "IQ-NA";
        edit.DeliveryAddress = "حي الأمير، قرب الجامع";
        edit.CodAmount = 70_000;
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await client.PutAsJsonAsync("/" + ShipmentsApiRoutes.ById(first.Id), edit)).StatusCode);

        var etag = await client.ETagAsync(first.Id);
        var saved = await client.PutWithETagAsync(ShipmentsApiRoutes.ById(first.Id), edit, etag);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var corrected = (await saved.Content.ReadFromJsonAsync<ShipmentDetails>())!;
        Assert.Equal("+9647701112233", corrected.ConsigneePhone);
        Assert.Equal(IraqHubs.Najaf, corrected.DestinationHubCode);
        Assert.Equal(70_000m, corrected.CodAmount);
        Assert.Contains(corrected.Events, e => e.Notes is { } n && n.StartsWith('✎'));

        // The old ETag is stale now.
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.PutWithETagAsync(ShipmentsApiRoutes.ById(first.Id), edit, etag)).StatusCode);

        // Cancelling before collection cancels it.
        var cancel = await client.PostWithETagAsync(ShipmentsApiRoutes.CancelOrder(first.Id), new CancelOrderRequest { Reason = "الزبون طلب مرتين" }, await client.ETagAsync(first.Id));
        Assert.True(cancel.IsSuccessStatusCode, await cancel.Content.ReadAsStringAsync());
        Assert.Equal(ShipmentStatus.Cancelled, (await client.GetShipmentAsync(first.Id)).Status);

        // Once we hold the parcel, cancelling sends it back to the seller; once it is out with a courier it can no longer be edited.
        var second = await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زينب كاظم"));
        await client.ScanAsync(ScanOperation.Receive, second.TrackingNumber, IraqHubs.Baghdad, "A-01-1");
        await client.TruckAsync(second.TrackingNumber, IraqHubs.Baghdad, IraqHubs.Basra);
        await client.ScanAsync(ScanOperation.Dispatch, second.TrackingNumber, IraqHubs.Basra, courier: "كرار (South-01)");
        var late = await client.PutWithETagAsync(ShipmentsApiRoutes.ById(second.Id), UpdateOrderRequest.From(await client.GetShipmentAsync(second.Id)), await client.ETagAsync(second.Id));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, late.StatusCode);
        Assert.Contains("shipment.not_changeable", await late.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var third = await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زينب كاظم"));
        await client.ScanAsync(ScanOperation.Receive, third.TrackingNumber, IraqHubs.Baghdad, "A-01-2");
        var back = await client.PostWithETagAsync(ShipmentsApiRoutes.CancelOrder(third.Id), new CancelOrderRequest { Reason = "ألغاه المتجر" }, await client.ETagAsync(third.Id));
        Assert.True(back.IsSuccessStatusCode, await back.Content.ReadAsStringAsync());
        var returning = await client.GetShipmentAsync(third.Id);
        Assert.True(ShipmentLifecycle.IsReturning(returning.Status));
        Assert.Equal(ReturnReason.SellerCancelled, returning.ReturnReason);

        // The customer's profile counts the history.
        var history = (await client.GetFromJsonAsync<CustomerProfile>("/" + ShipmentsApiRoutes.CustomerLookup + "?phone=" + Phone))!;
        Assert.Equal(2, history.Orders); // the first order moved to another number
        Assert.Equal(1, history.Returned);
    }
}
