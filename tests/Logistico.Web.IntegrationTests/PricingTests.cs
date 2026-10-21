using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Web.IntegrationTests;

/// <summary>A company sets its delivery prices; orders booked without a fee are priced from them.</summary>
[Collection("Api")]
public sealed class PricingTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task Orders_are_priced_from_the_company_price_list_including_seller_discounts()
    {
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var shop = await client.CreateMerchantAsync("متجر الأسعار", "pricing.flow.test");

        // Standard prices until the company saves its own.
        var response = await client.GetAsync("/" + ShipmentsApiRoutes.Pricing);
        var card = (await response.Content.ReadFromJsonAsync<RateCard>())!;
        Assert.Equal(5_000m, card.SameGovernorateFee);
        Assert.Equal(7_000m, card.GovernorateFees["IQ-BA"]);

        // Basra becomes 9,000; this seller gets 10 % off; extra kilograms cost 1,500.
        card.GovernorateFees["IQ-BA"] = 9_000m;
        card.ExtraKgFee = 1_500m;
        card.SellerDiscounts.Add(new SellerDiscount(shop, "متجر الأسعار", 10));
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await client.PutAsJsonAsync("/" + ShipmentsApiRoutes.Pricing, card)).StatusCode);
        var saved = await client.PutWithETagAsync(ShipmentsApiRoutes.Pricing, card, response.Headers.ETag!);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var stored = (await saved.Content.ReadFromJsonAsync<RateCard>())!;
        Assert.Equal(9_000m, stored.GovernorateFees["IQ-BA"]);
        Assert.Equal(10m, Assert.Single(stored.SellerDiscounts).Percent);

        // Out of range discounts are refused.
        stored.SellerDiscounts[0] = stored.SellerDiscounts[0] with { Percent = 150 };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutWithETagAsync(ShipmentsApiRoutes.Pricing, stored, (await client.GetAsync("/" + ShipmentsApiRoutes.Pricing)).Headers.ETag!)).StatusCode);

        // An order without a fee, 7 kg to Basra: (9,000 + 2 × 1,500) − 10 % = 10,800 → 10,750 (rounded to 250).
        var order = ApiTestHelpers.DomesticOrder(shop, "زبون الأسعار");
        order.DeliveryFee = null;
        order.GrossWeightKg = 7;
        var booked = await client.BookAsync(order);
        Assert.Equal(10_750m, (await client.GetShipmentAsync(booked.Id)).DeliveryFee);

        // A typed fee is kept as typed.
        var typed = ApiTestHelpers.DomesticOrder(shop, "زبون السعر اليدوي");
        typed.DeliveryFee = 4_000m;
        Assert.Equal(4_000m, (await client.GetShipmentAsync((await client.BookAsync(typed)).Id)).DeliveryFee);

        // Batch booking without fees uses the list too: 1 kg to Basra with the discount = 8,100 → 8,000.
        var batch = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.BulkBook, new BulkBookOrdersRequest
        {
            MerchantId = shop,
            Orders = [new ParsedOrderDraft { ConsigneeName = "زبون الدفعة", ConsigneePhone = "07812223355", DestinationCode = "IQ-BA", DeliveryAddress = "العشار", CodAmount = 20_000, DeliveryFee = 0 }],
        });
        Assert.True(batch.IsSuccessStatusCode, await batch.Content.ReadAsStringAsync());
        var batchOrder = (await batch.Content.ReadFromJsonAsync<BulkBookOrdersResponse>())!.Orders.Single();
        Assert.Equal(8_000m, (await client.GetShipmentAsync(batchOrder.Id)).DeliveryFee);
    }
}
