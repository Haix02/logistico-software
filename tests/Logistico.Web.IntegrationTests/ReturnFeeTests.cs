using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel.Identity;

namespace Logistico.Web.IntegrationTests;

/// <summary>A parcel the customer refused goes back to the seller, who pays the company's return fee from their payout.</summary>
[Collection("Api")]
public sealed class ReturnFeeTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task A_refused_parcel_costs_the_seller_its_return_fee_on_the_next_payout()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var courierId = await admin.EnsureCourierAsync("Fee-01", "سجاد");
        var shop = await admin.CreateMerchantAsync("Return Fee Shop", "return.fee.shop");

        // Half of the delivery fee is charged for a return.
        var pricing = await admin.GetAsync("/" + ShipmentsApiRoutes.Pricing);
        var card = (await pricing.Content.ReadFromJsonAsync<RateCard>())!;
        card.ReturnFeePercent = 50;
        Assert.True((await admin.PutWithETagAsync(ShipmentsApiRoutes.Pricing, card, pricing.Headers.ETag!)).IsSuccessStatusCode);

        var delivered = await DispatchAsync(admin, shop, 60_000);
        await admin.ScanAsync(ScanOperation.Deliver, delivered.TrackingNumber, IraqHubs.Baghdad, courier: "سجاد (Fee-01)");
        var refused = await DispatchAsync(admin, shop, 30_000);
        var damaged = await DispatchAsync(admin, shop, 20_000);

        // The courier records the refusal; the return starts by itself and the parcel comes back to the shelf.
        var courier = factory.CreateUserClient("fee.courier@logistico.test", company, StaffRoles.Courier, courierCode: "Fee-01");
        Assert.True((await courier.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierFailed(refused.Id), new CourierFailureRequest { Reason = DeliveryFailureReason.CustomerRefused })).IsSuccessStatusCode);
        await admin.ScanAsync(ScanOperation.Receive, refused.TrackingNumber, IraqHubs.Baghdad, "R-01");

        // Damaged in our care: returned without a fee.
        Assert.True((await courier.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierFailed(damaged.Id), new CourierFailureRequest { Reason = DeliveryFailureReason.NoAnswer })).IsSuccessStatusCode);
        await admin.ScanAsync(ScanOperation.Receive, damaged.TrackingNumber, IraqHubs.Baghdad, "R-02");
        var startDamaged = await admin.PostWithETagAsync(ShipmentsApiRoutes.StartReturn(damaged.Id), new StartReturnRequest { Reason = ReturnReason.Damaged }, await admin.ETagAsync(damaged.Id));
        Assert.True(startDamaged.IsSuccessStatusCode, await startDamaged.Content.ReadAsStringAsync());

        var handOver = await admin.PostAsJsonAsync("/" + ShipmentsApiRoutes.ReturnsHandOver, new HandOverReturnsRequest { ShipmentIds = [refused.Id, damaged.Id], ReceivedBy = "أبو حسن" });
        Assert.True(handOver.IsSuccessStatusCode, await handOver.Content.ReadAsStringAsync());

        var refusedNow = await admin.GetShipmentAsync(refused.Id);
        var fee = refusedNow.DeliveryFee!.Value / 2;
        Assert.Equal((ShipmentStatus.Returned, fee), (refusedNow.Status, refusedNow.ReturnFee));
        Assert.Null((await admin.GetShipmentAsync(damaged.Id)).ReturnFee);

        // Finance deducts it from what the seller is owed, and the payout settles it.
        await admin.HandInCashAsync(courierId);
        var balance = Assert.Single((await admin.GetFromJsonAsync<FinanceSummary>("/" + ShipmentsApiRoutes.FinanceSummary + $"?merchantId={shop}"))!.SellerBalances);
        var deliveryFee = (await admin.GetShipmentAsync(delivered.Id)).DeliveryFee!.Value;
        Assert.Equal((1, fee, 60_000m - deliveryFee - fee), (balance.ReturnCount, balance.ReturnFeesIqd, balance.NetPayableIqd));

        var settled = await admin.PostAsJsonAsync("/" + ShipmentsApiRoutes.FinanceSettlements, new CreateCodSettlementRequest { MerchantId = shop, PayoutMethod = PayoutMethod.Cash });
        Assert.Equal(HttpStatusCode.Created, settled.StatusCode);
        var settlement = (await settled.Content.ReadFromJsonAsync<CodSettlementItem>())!;
        Assert.Equal((1, 1, fee, 60_000m - deliveryFee - fee), (settlement.ShipmentCount, settlement.ReturnCount, settlement.ReturnFeesAmount, settlement.NetPayoutAmount));

        var csv = await (await admin.GetAsync("/" + ShipmentsApiRoutes.SettlementExport(settlement.Id))).Content.ReadAsStringAsync();
        Assert.Contains(refused.TrackingNumber, csv, StringComparison.Ordinal);
        Assert.DoesNotContain(damaged.TrackingNumber, csv, StringComparison.Ordinal);

        // The seller sees the fee on the returned order.
        var seller = factory.CreateUserClient("fee.seller@logistico.test", company, StaffRoles.Seller, merchantId: shop);
        var order = Assert.Single((await seller.GetFromJsonAsync<SellerOrderList>("/" + ShipmentsApiRoutes.SellerOrders + "?q=" + refused.TrackingNumber))!.Items);
        Assert.Equal(fee, order.ReturnFee);
    }

    [Fact]
    public async Task A_return_fee_bigger_than_the_cash_waits_for_the_next_payout()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var courierId = await admin.EnsureCourierAsync("Fee-02", "حسن");
        var shop = await admin.CreateMerchantAsync("Small Cash Shop", "small.cash.shop");
        var pricing = await admin.GetAsync("/" + ShipmentsApiRoutes.Pricing);
        var card = (await pricing.Content.ReadFromJsonAsync<RateCard>())!;
        card.ReturnFeePercent = 100;
        Assert.True((await admin.PutWithETagAsync(ShipmentsApiRoutes.Pricing, card, pricing.Headers.ETag!)).IsSuccessStatusCode);

        var courier = factory.CreateUserClient("fee2.courier@logistico.test", company, StaffRoles.Courier, courierCode: "Fee-02");
        var refused = await DispatchAsync(admin, shop, 50_000, "حسن (Fee-02)");
        Assert.True((await courier.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierFailed(refused.Id), new CourierFailureRequest { Reason = DeliveryFailureReason.CustomerRefused })).IsSuccessStatusCode);
        await admin.ScanAsync(ScanOperation.Receive, refused.TrackingNumber, IraqHubs.Baghdad, "R-01");
        Assert.True((await admin.PostAsJsonAsync("/" + ShipmentsApiRoutes.ReturnsHandOver, new HandOverReturnsRequest { ShipmentIds = [refused.Id] })).IsSuccessStatusCode);
        var fee = (await admin.GetShipmentAsync(refused.Id)).ReturnFee!.Value;

        // Just over the delivery fee collected: the seller is paid what is left, and the return fee waits.
        var small = await DispatchAsync(admin, shop, (await admin.GetShipmentAsync(refused.Id)).DeliveryFee!.Value + 1_000, "حسن (Fee-02)");
        await admin.ScanAsync(ScanOperation.Deliver, small.TrackingNumber, IraqHubs.Baghdad, courier: "حسن (Fee-02)");
        await admin.HandInCashAsync(courierId);
        var first = await SettleAsync(admin, shop);
        Assert.Equal((0, 0m, 1_000m), (first.ReturnCount, first.ReturnFeesAmount, first.NetPayoutAmount));
        Assert.Null((await admin.GetShipmentAsync(refused.Id)).CodSettlementNumber);

        var big = await DispatchAsync(admin, shop, 100_000, "حسن (Fee-02)");
        await admin.ScanAsync(ScanOperation.Deliver, big.TrackingNumber, IraqHubs.Baghdad, courier: "حسن (Fee-02)");
        await admin.HandInCashAsync(courierId);
        var second = await SettleAsync(admin, shop);
        Assert.Equal((1, fee), (second.ReturnCount, second.ReturnFeesAmount));
        Assert.Equal(second.SettlementNumber, (await admin.GetShipmentAsync(refused.Id)).CodSettlementNumber);
    }

    private static async Task<CodSettlementItem> SettleAsync(HttpClient admin, Guid shop)
    {
        var response = await admin.PostAsJsonAsync("/" + ShipmentsApiRoutes.FinanceSettlements, new CreateCodSettlementRequest { MerchantId = shop, PayoutMethod = PayoutMethod.Cash });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CodSettlementItem>())!;
    }

    private static async Task<CreateShipmentResponse> DispatchAsync(HttpClient admin, Guid shop, decimal cod, string courier = "سجاد (Fee-01)")
    {
        var order = ApiTestHelpers.DomesticOrder(shop, "زبون " + cod);
        order.DestinationCode = "IQ-BG";
        order.CodAmount = cod;
        order.ConsigneePhone = $"0770{1_000_000 + (int)(cod / 10)}"; // a customer who refused before has their next orders held
        order.DeliveryFee = null; // the company's price
        var booked = await admin.BookAsync(order);
        await admin.ScanAsync(ScanOperation.Receive, booked.TrackingNumber, IraqHubs.Baghdad, "A-01-1");
        await admin.ScanAsync(ScanOperation.Dispatch, booked.TrackingNumber, IraqHubs.Baghdad, courier: courier);
        return booked;
    }
}
