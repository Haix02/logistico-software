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

    private static async Task<CreateShipmentResponse> DeliverAsync(HttpClient admin, Guid shop, decimal cod)
    {
        var booked = await OutForDeliveryAsync(admin, shop, cod);
        await admin.ScanAsync(ScanOperation.Deliver, booked.TrackingNumber, IraqHubs.Baghdad, courier: "سجاد (Cash-01)");
        return booked;
    }

    private static async Task<CreateShipmentResponse> OutForDeliveryAsync(HttpClient admin, Guid shop, decimal cod)
    {
        var order = ApiTestHelpers.DomesticOrder(shop, "زبون " + cod);
        order.DestinationCode = "IQ-BG";
        order.CodAmount = cod;
        var booked = await admin.BookAsync(order);
        await admin.ScanAsync(ScanOperation.Receive, booked.TrackingNumber, IraqHubs.Baghdad, "A-01-1");
        await admin.ScanAsync(ScanOperation.Dispatch, booked.TrackingNumber, IraqHubs.Baghdad, courier: "سجاد (Cash-01)");
        return booked;
    }
}
