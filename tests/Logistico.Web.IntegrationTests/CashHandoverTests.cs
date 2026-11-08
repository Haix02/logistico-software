using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;

namespace Logistico.Web.IntegrationTests;

/// <summary>Couriers hand the cash they collected in at the office; only that cash is paid out to sellers.</summary>
[Collection("Api")]
public sealed class CashHandoverTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task A_courier_hands_in_collected_cash_and_a_shortage_stays_on_record()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var courierId = await admin.EnsureCourierAsync("Cash-01", "سجاد");
        var shop = await admin.CreateMerchantAsync("Cash Shop", "cash.shop");
        var first = await DeliverAsync(admin, shop, 40_000);
        var second = await DeliverAsync(admin, shop, 25_000);
        await OutForDeliveryAsync(admin, shop, 10_000); // not collected yet: not part of the hand-in

        var courier = factory.CreateUserClient("cash.courier@logistico.test", company, StaffRoles.Courier, courierCode: "Cash-01");
        var run = (await courier.GetFromJsonAsync<CourierRun>("/" + ShipmentsApiRoutes.CourierRun))!;
        Assert.Equal((65_000m, 2), (run.CashToHandInIqd, run.ParcelsToHandIn));

        var fleet = (await admin.GetFromJsonAsync<FleetOverviewResult>("/" + ShipmentsApiRoutes.Fleet))!;
        var row = Assert.Single(fleet.Couriers, c => c.Id == courierId);
        Assert.Equal((65_000m, 2, 10_000m), (row.CashToHandInIqd, row.ParcelsToHandIn, row.CashOnRunIqd));
        Assert.Equal(65_000m, fleet.CashWithCouriersIqd);

        var cash = (await admin.GetFromJsonAsync<CourierCash>("/" + ShipmentsApiRoutes.CourierCash(courierId)))!;
        Assert.Equal(65_000m, cash.ExpectedIqd);
        Assert.Equal(new[] { first.Id, second.Id }.Order(), cash.Parcels.Select(p => p.Id).Order());

        // Viewers read but do not receive cash.
        var viewer = factory.CreateUserClient("cash.viewer@logistico.test", company, StaffRoles.Viewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierCash(courierId), new ReceiveCashRequest { ReceivedIqd = 1 })).StatusCode);

        // Two people receive the same cash at once: it is counted once.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            admin.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierCash(courierId), new ReceiveCashRequest { ReceivedIqd = 60_000, Notes = "نقص 5,000" })));
        var created = Assert.Single(attempts, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Contains(attempts.Single(r => r != created).StatusCode, new[] { HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity, HttpStatusCode.PreconditionFailed });
        var handover = (await created.Content.ReadFromJsonAsync<CashHandoverItem>())!;
        Assert.Equal((2, 65_000m, 60_000m, -5_000m), (handover.ParcelCount, handover.ExpectedIqd, handover.ReceivedIqd, handover.DifferenceIqd));

        var again = await admin.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierCash(courierId), new ReceiveCashRequest { ReceivedIqd = 0 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
        Assert.Contains("cash.nothing_to_hand_in", await again.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(0m, (await courier.GetFromJsonAsync<CourierRun>("/" + ShipmentsApiRoutes.CourierRun))!.CashToHandInIqd);
        Assert.Equal(0m, (await admin.GetFromJsonAsync<FleetOverviewResult>("/" + ShipmentsApiRoutes.Fleet))!.CashWithCouriersIqd);
        var history = (await admin.GetFromJsonAsync<PagedResult<CashHandoverItem>>("/" + ShipmentsApiRoutes.CashHandovers + $"?courierId={courierId}"))!;
        Assert.Equal(handover.Number, Assert.Single(history.Items).Number);
        Assert.Equal(ShipmentStatus.Delivered, (await admin.GetShipmentAsync(first.Id)).Status);
    }

    [Fact]
    public async Task The_cash_desk_takes_what_it_counted_and_a_shortage_stays_until_paid_back()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var finance = factory.CreateUserClient("desk.finance@logistico.test", company, StaffRoles.Finance);
        var courierId = await admin.EnsureCourierAsync("Desk-01", "ليث");
        const string label = "ليث (Desk-01)";
        var shop = await admin.CreateMerchantAsync("Desk Shop", "desk.shop");
        var first = await DeliverAsync(admin, shop, 30_000, label);
        var second = await DeliverAsync(admin, shop, 20_000, label);

        // Finance runs the cash desk. It opens the hand-in; meanwhile the courier collects one more parcel.
        var screen = (await finance.GetFromJsonAsync<CourierCash>("/" + ShipmentsApiRoutes.CourierCash(courierId)))!;
        var third = await DeliverAsync(admin, shop, 15_000, label);
        var counted = await finance.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierCash(courierId),
            new ReceiveCashRequest { ReceivedIqd = 45_000, ParcelIds = [.. screen.Parcels.Select(p => p.Id)] });
        Assert.Equal(HttpStatusCode.Created, counted.StatusCode);
        var handover = (await counted.Content.ReadFromJsonAsync<CashHandoverItem>())!;
        Assert.Equal((2, 50_000m, -5_000m), (handover.ParcelCount, handover.ExpectedIqd, handover.DifferenceIqd));

        // The third parcel's cash waits for the next hand-in, and the shortage stays on the courier.
        var after = (await finance.GetFromJsonAsync<CourierCash>("/" + ShipmentsApiRoutes.CourierCash(courierId)))!;
        Assert.Equal((15_000m, third.Id, 5_000m), (after.ExpectedIqd, Assert.Single(after.Parcels).Id, after.ShortageIqd));

        // A screen that still shows a parcel handed in meanwhile is stale.
        var stale = await finance.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierCash(courierId), new ReceiveCashRequest { ReceivedIqd = 45_000, ParcelIds = [first.Id, third.Id] });
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        // The courier hands in the third parcel's cash and pays the shortage back with it.
        var repaid = await finance.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierCash(courierId), new ReceiveCashRequest { ReceivedIqd = 20_000, ParcelIds = [third.Id] });
        Assert.Equal(HttpStatusCode.Created, repaid.StatusCode);
        var settled = (await finance.GetFromJsonAsync<CourierCash>("/" + ShipmentsApiRoutes.CourierCash(courierId)))!;
        Assert.Equal((0m, 0m), (settled.ExpectedIqd, settled.ShortageIqd));
        var nothing = await finance.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierCash(courierId), new ReceiveCashRequest { ReceivedIqd = 1_000 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, nothing.StatusCode);

        // A shortage alone can be paid back with a hand-in of its own.
        var fourth = await DeliverAsync(admin, shop, 10_000, label);
        await finance.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierCash(courierId), new ReceiveCashRequest { ReceivedIqd = 7_000, ParcelIds = [fourth.Id] });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await finance.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierCash(courierId),
            new ReceiveCashRequest { ReceivedIqd = 3_000 })).StatusCode); // not said to be a repayment: nothing to hand in
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await finance.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierCash(courierId),
            new ReceiveCashRequest { ReceivedIqd = 4_000, RepaysShortage = true })).StatusCode); // more than owed
        var repayment = await finance.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierCash(courierId), new ReceiveCashRequest { ReceivedIqd = 3_000, RepaysShortage = true });
        Assert.Equal(HttpStatusCode.Created, repayment.StatusCode);
        Assert.Equal(0m, (await finance.GetFromJsonAsync<CourierCash>("/" + ShipmentsApiRoutes.CourierCash(courierId)))!.ShortageIqd);
        Assert.Equal(ShipmentStatus.Delivered, (await admin.GetShipmentAsync(second.Id)).Status);
    }

    [Fact]
    public async Task A_renamed_courier_keeps_their_run_and_their_cash()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var courierId = await admin.EnsureCourierAsync("Rename-01", "مصطفى");
        var shop = await admin.CreateMerchantAsync("Rename Shop", "rename.shop");
        var delivered = await DeliverAsync(admin, shop, 25_000, "مصطفى (Rename-01)");
        var onRun = await OutForDeliveryAsync(admin, shop, 10_000, "مصطفى (Rename-01)");

        var row = (await admin.GetFromJsonAsync<FleetOverviewResult>("/" + ShipmentsApiRoutes.Fleet))!.Couriers.Single(c => c.Id == courierId);
        var etag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"{row.Version}\"");
        var renamed = await admin.PutWithETagAsync(ShipmentsApiRoutes.CourierById(courierId),
            new CourierRequest { Code = "Rename-01", Name = "مصطفى كريم", Phone = "07802223344", Governorate = "IQ-BG" }, etag);
        Assert.True(renamed.IsSuccessStatusCode, await renamed.Content.ReadAsStringAsync());

        var cash = (await admin.GetFromJsonAsync<CourierCash>("/" + ShipmentsApiRoutes.CourierCash(courierId)))!;
        Assert.Equal(delivered.Id, Assert.Single(cash.Parcels).Id);
        var courier = factory.CreateUserClient("rename.courier@logistico.test", company, StaffRoles.Courier, courierCode: "Rename-01");
        var run = (await courier.GetFromJsonAsync<CourierRun>("/" + ShipmentsApiRoutes.CourierRun))!;
        Assert.Equal(onRun.Id, Assert.Single(run.Stops).Id);
        Assert.Equal(25_000m, run.CashToHandInIqd);
    }

    [Fact]
    public async Task A_parcel_already_paid_to_the_seller_does_not_start_a_return_on_its_own()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var courierId = await admin.EnsureCourierAsync("Paid-01", "علي");
        var shop = await admin.CreateMerchantAsync("Paid Shop", "paid.shop");
        var parcel = await DeliverAsync(admin, shop, 30_000, "علي (Paid-01)");
        await admin.HandInCashAsync(courierId);
        var payout = await admin.PostAsJsonAsync("/" + ShipmentsApiRoutes.FinanceSettlements, new CreateCodSettlementRequest { MerchantId = shop });
        Assert.True(payout.IsSuccessStatusCode, await payout.Content.ReadAsStringAsync());

        var start = await admin.PostWithETagAsync(ShipmentsApiRoutes.StartReturn(parcel.Id), new StartReturnRequest { Reason = ReturnReason.CustomerReturn },
            await admin.ETagAsync(parcel.Id));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, start.StatusCode);
        Assert.Contains("shipment.return_after_payout", await start.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(ShipmentStatus.Delivered, (await admin.GetShipmentAsync(parcel.Id)).Status);
    }

    private static async Task<CreateShipmentResponse> DeliverAsync(HttpClient admin, Guid shop, decimal cod, string courier = "سجاد (Cash-01)")
    {
        var booked = await OutForDeliveryAsync(admin, shop, cod, courier);
        await admin.ScanAsync(ScanOperation.Deliver, booked.TrackingNumber, IraqHubs.Baghdad, courier: courier);
        return booked;
    }

    private static async Task<CreateShipmentResponse> OutForDeliveryAsync(HttpClient admin, Guid shop, decimal cod, string courier = "سجاد (Cash-01)")
    {
        var order = ApiTestHelpers.DomesticOrder(shop, "زبون " + cod);
        order.DestinationCode = "IQ-BG";
        order.CodAmount = cod;
        var booked = await admin.BookAsync(order);
        await admin.ScanAsync(ScanOperation.Receive, booked.TrackingNumber, IraqHubs.Baghdad, "A-01-1");
        await admin.ScanAsync(ScanOperation.Dispatch, booked.TrackingNumber, IraqHubs.Baghdad, courier: courier);
        return booked;
    }
}
