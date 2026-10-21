using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Web.IntegrationTests;

/// <summary>
/// An Instagram shop in Baghdad sends a parcel to a customer in Basra: order → pickup → Baghdad hub → truck → Basra hub
/// → courier → failed attempt (rescheduled automatically) → delivered with cash on delivery.
/// </summary>
[Collection("Api")]
public sealed class DomesticDeliveryFlowTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task Instagram_order_crosses_iraq_by_truck_and_is_delivered_with_cash_on_delivery()
    {
        var client = factory.CreateUserClient();
        var shop = await client.CreateMerchantAsync("بوتيك الاختبار", "flow.test.boutique");

        // 1. Order: Baghdad seller (HUB-BGW) → Basra customer (HUB-BSR)
        var created = await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زينب كاظم"));
        var order = await client.GetShipmentAsync(created.Id);
        Assert.Equal("بوتيك الاختبار", order.MerchantName);
        Assert.Equal("+9647812223344", order.ConsigneePhone);
        Assert.Equal(IraqHubs.Baghdad, order.OriginHubCode);
        Assert.Equal(IraqHubs.Basra, order.DestinationHubCode);
        Assert.True(order.CrossesHubs);
        Assert.NotNull(order.PromisedAt);

        // 2. Picked up from the shop, received at the Baghdad hub: it is planned onto the next truck to Basra automatically.
        var picked = await client.RecordAsync(created.Id, new RecordShipmentEventRequest { Status = ShipmentStatus.PickedUp, CourierName = "مصطفى" }, await client.ETagAsync(created.Id));
        Assert.Equal(HttpStatusCode.OK, picked.StatusCode);
        var received = await client.ScanAsync(ScanOperation.Receive, created.TrackingNumber.ToLowerInvariant(), IraqHubs.Baghdad, "a-03-2");
        Assert.Equal(ShipmentStatus.AtWarehouse, received.Status);
        Assert.Equal("A-03-2", received.WarehouseBin);
        Assert.Equal(IraqHubs.Basra, received.NextHubCode);
        Assert.NotNull(received.TransferNumber);
        var reshelved = await client.ScanAsync(ScanOperation.Receive, created.TrackingNumber, IraqHubs.Baghdad, "B-01-1");
        Assert.Equal("B-01-1", reshelved.WarehouseBin);

        var waiting = await client.GetFromJsonAsync<ShipmentListResult>("/" + ShipmentsApiRoutes.Shipments + new ShipmentQuery { NeedsTransfer = true, MerchantId = shop }.ToQueryString());
        Assert.Equal(created.TrackingNumber, Assert.Single(waiting!.Items).TrackingNumber);

        // 3. A Baghdad courier cannot take it: it must reach Basra first.
        var tooEarly = await client.TryScanAsync(ScanOperation.Dispatch, created.TrackingNumber, IraqHubs.Baghdad, courier: "حيدر (Baghdad-02)");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooEarly.StatusCode);
        Assert.Contains("shipment.wrong_hub_for_dispatch", await tooEarly.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // 4. The truck: loaded in Baghdad, departs, unloaded in Basra.
        var truck = await client.TruckAsync(created.TrackingNumber, IraqHubs.Baghdad, IraqHubs.Basra);
        Assert.Equal(TransferStatus.Received, truck.Summary.Status);
        Assert.Equal(TransferItemState.Received, Assert.Single(truck.Items).State);
        var atBasra = await client.GetShipmentAsync(created.Id);
        Assert.Equal(ShipmentStatus.AtWarehouse, atBasra.Status);
        Assert.Equal(IraqHubs.Basra, atBasra.CurrentHubCode);
        Assert.Contains(atBasra.Events, e => e.Status == ShipmentStatus.InTransitToHub);

        // 5. Out with a Basra courier.
        var out1 = await client.ScanAsync(ScanOperation.Dispatch, created.TrackingNumber, IraqHubs.Basra, courier: "كرار (South-01)");
        Assert.Equal(ShipmentStatus.OutForDelivery, out1.Status);
        Assert.Null(out1.WarehouseBin);

        // 6. A failed attempt needs a reason; with a reason it is rescheduled for the next working day automatically.
        var noReason = await client.RecordAsync(created.Id, new RecordShipmentEventRequest { Status = ShipmentStatus.DeliveryFailed }, await client.ETagAsync(created.Id));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        var failedResponse = await client.RecordAsync(created.Id, new RecordShipmentEventRequest { Status = ShipmentStatus.DeliveryFailed, FailureReason = DeliveryFailureReason.NoAnswer }, await client.ETagAsync(created.Id));
        var failed = (await failedResponse.Content.ReadFromJsonAsync<ShipmentDetails>())!;
        Assert.Equal(ShipmentStatus.DeliveryFailed, failed.Status);
        Assert.NotNull(failed.RescheduledTo);
        Assert.NotEqual(DayOfWeek.Friday, failed.RescheduledTo!.Value.ToOffset(TimeSpan.FromHours(3)).DayOfWeek);

        var messages = (await client.GetFromJsonAsync<NotificationPage>("/" + ShipmentsApiRoutes.AutomationMessages + "?status=Pending"))!;
        Assert.Contains(messages.Items, m => m.TrackingNumber == created.TrackingNumber && m.Template == NotificationTemplate.DeliveryFailed);
        Assert.Contains(messages.Items, m => m.TrackingNumber == created.TrackingNumber && m.Template == NotificationTemplate.OutForDelivery && m.Body.Contains("زينب", StringComparison.Ordinal));

        // The customer's public page explains it — without personal data — and offers self-service requests.
        var publicPage = await factory.CreateClient().GetAsync("/" + ShipmentsApiRoutes.Track(created.TrackingNumber));
        var body = await publicPage.Content.ReadAsStringAsync();
        var view = JsonSerializer.Deserialize<PublicTrackingView>(body, JsonSerializerOptions.Web)!;
        Assert.Equal(ShipmentStatus.DeliveryFailed, view.Status);
        Assert.Null(view.MerchantName);
        Assert.Null(view.DestinationCity);
        Assert.Null(view.AmountToPay);
        Assert.Null(view.AmountCurrency);
        Assert.Null(view.CurrentHubCode);
        Assert.Null(view.DestinationHubCode);
        Assert.Empty(view.RouteHubs);
        Assert.Equal(DeliveryFailureReason.NoAnswer, view.Events[0].FailureReason);
        Assert.Contains(RequestType.Reschedule, view.AllowedRequests);
        Assert.DoesNotContain("زينب", body, StringComparison.Ordinal);
        Assert.DoesNotContain("7812223344", body, StringComparison.Ordinal);
        Assert.DoesNotContain("محلة 112", body, StringComparison.Ordinal);
        Assert.DoesNotContain("B-01-1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("كرار", body, StringComparison.Ordinal);
        Assert.All(view.Events.Zip(view.Events.Skip(1)), pair => Assert.NotEqual(pair.First.Status, pair.Second.Status)); // shelf moves are hidden
        Assert.All(view.Events, e =>
        {
            Assert.Null(e.Location);
            Assert.Null(e.HubCode);
        });

        // 7. Next day: back on the shelf, second attempt, delivered and the cash collected.
        await client.ScanAsync(ScanOperation.Receive, created.TrackingNumber, IraqHubs.Basra, "C-01-1");
        var out2 = await client.ScanAsync(ScanOperation.Dispatch, created.TrackingNumber, IraqHubs.Basra, courier: "كرار (South-01)");
        Assert.Equal(ShipmentStatus.OutForDelivery, out2.Status);
        var delivered = await client.ScanAsync(ScanOperation.Deliver, created.TrackingNumber, IraqHubs.Basra);
        Assert.Equal(ShipmentStatus.Delivered, delivered.Status);
        var final = await client.GetShipmentAsync(created.Id);
        Assert.Equal(2, final.DeliveryAttempts);
        Assert.NotNull(final.CodCollectedAt);

        var summary = await client.GetFromJsonAsync<ShipmentsSummary>("/" + ShipmentsApiRoutes.Summary + $"?merchantId={shop}");
        Assert.Equal(new MoneyTotal("IQD", 65_000m), Assert.Single(summary!.CodCollected));
        Assert.Empty(summary.CodToCollect);

        // Every scan is in the persistent hub radar.
        var radar = (await client.GetFromJsonAsync<HubRadar>("/" + ShipmentsApiRoutes.WarehouseRadar))!;
        Assert.Contains(radar.Recent, r => r.TrackingNumber == created.TrackingNumber && r.HubCode == IraqHubs.Basra && r.Operation == ScanOperation.Deliver);
        Assert.Contains(radar.Recent, r => r.TrackingNumber == created.TrackingNumber && r.Operation == ScanOperation.Dispatch && !r.Ok);
    }

    [Fact]
    public async Task Orders_for_unknown_sellers_are_rejected()
    {
        var response = await factory.CreateUserClient().PostAsJsonAsync("/" + ShipmentsApiRoutes.Shipments, ApiTestHelpers.DomesticOrder(Guid.CreateVersion7(), "Nobody"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("shipment.merchant_not_found", problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Scanning_an_unknown_parcel_returns_404_with_a_code()
    {
        var response = await factory.CreateUserClient().TryScanAsync(ScanOperation.Receive, "LX26999999990", IraqHubs.Baghdad);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("shipment.tracking_not_found", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api_callers_get_arabic_messages_with_accept_language()
    {
        var client = factory.CreateUserClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ar-IQ");
        var created = await client.BookAsync(ApiTestHelpers.InternationalBooking("Arabic Messages Exports"));

        var invalid = await client.RecordAsync(created.Id, new RecordShipmentEventRequest { Status = ShipmentStatus.Delivered }, await client.ETagAsync(created.Id));
        var validation = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.Shipments, new CreateShipmentRequest { ServiceType = ServiceType.Domestic });

        Assert.Contains("لا يمكن نقل شحنة حالتها «تم إنشاء الطلب» إلى «تم التسليم»", await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("هذا الحقل مطلوب.", await validation.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bulk_booking_from_parsed_whatsapp_orders_allocates_tracking_numbers_and_routes_to_hubs()
    {
        var client = factory.CreateUserClient();
        var shop = await client.CreateMerchantAsync("متجر الدفعات الذكية", "batch.smart.iq");

        var drafts = SmartOrderParser.ParseBatch(
            """
            سارة محمد - 07701234567 - بغداد المنصور شارع 14 رمضان - قرب مطعم الساعة - فستان أسود - 45 الف
            علي البصري - 07809876543 - البصرة العشار شارع الوطن - مقابل فندق شط العرب - عطر رجالي - 65,000
            """,
            "IQ-BG");

        var response = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.BulkBook, new BulkBookOrdersRequest
        {
            MerchantId = shop,
            PickupRequired = true,
            Orders = drafts.ToList(),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var booked = (await response.Content.ReadFromJsonAsync<BulkBookOrdersResponse>())!;
        Assert.Equal(2, booked.BookedCount);
        Assert.Equal(2, booked.Orders.Count);
        Assert.All(booked.Orders, o => Assert.True(TrackingNumber.IsValid(o.TrackingNumber)));

        var secondDetails = await client.GetShipmentAsync(booked.Orders[1].Id);
        Assert.Equal("IQ-BA", secondDetails.DestinationCode);
        Assert.Equal(IraqHubs.Basra, secondDetails.DestinationHubCode);
        Assert.Equal(65_000m, secondDetails.CodAmount);
        Assert.Equal(7_000m, secondDetails.DeliveryFee);
    }
}
