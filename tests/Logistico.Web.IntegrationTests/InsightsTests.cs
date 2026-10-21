using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Web.IntegrationTests;

/// <summary>Demand intelligence is computed in PostgreSQL from the company's own orders, and the open-data export hides small groups.</summary>
[Collection("Api")]
public sealed class InsightsTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task Insights_show_what_customers_order_where_and_how_the_orders_end()
    {
        // A company of its own, so other tests' orders do not count.
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var shop = await client.CreateMerchantAsync("متجر الرؤى", "insights.flow.test");

        Task<CreateShipmentResponse> Book(string governorate, string items, string phone, decimal? cod)
        {
            var order = ApiTestHelpers.DomesticOrder(shop, "زبون");
            order.DestinationCode = governorate;
            order.ItemsDescription = items;
            order.ConsigneePhone = phone;
            order.CodAmount = cod;
            order.CodCurrency = cod is null ? null : "IQD";
            return client.BookAsync(order);
        }

        var dress = await Book("IQ-BG", "فستان سهرة", "0770 100 0001", 30_000);
        await Book("IQ-BG", "فستان صيفي", "0770 100 0001", 60_000); // the same customer again
        var headphones = await Book("IQ-BG", "سماعات بلوتوث", "0770 100 0002", 250_000);
        await Book("IQ-BA", "عطر 100 مل", "0770 100 0003", null);

        // One delivered, one refused at the door (the return starts automatically).
        await client.ScanAsync(ScanOperation.Receive, dress.TrackingNumber, IraqHubs.Baghdad, "A-01-1");
        await client.ScanAsync(ScanOperation.Dispatch, dress.TrackingNumber, IraqHubs.Baghdad, courier: "علي (Baghdad-03)");
        await client.ScanAsync(ScanOperation.Deliver, dress.TrackingNumber, IraqHubs.Baghdad);
        await client.ScanAsync(ScanOperation.Receive, headphones.TrackingNumber, IraqHubs.Baghdad, "A-01-2");
        await client.ScanAsync(ScanOperation.Dispatch, headphones.TrackingNumber, IraqHubs.Baghdad, courier: "علي (Baghdad-03)");
        await client.ScanAsync(ScanOperation.Fail, headphones.TrackingNumber, IraqHubs.Baghdad, reason: DeliveryFailureReason.CustomerRefused);

        var insights = (await client.GetFromJsonAsync<DemandInsights>("/" + ShipmentsApiRoutes.Insights + "?days=30"))!;

        Assert.Equal(30, insights.Days);
        Assert.Equal(4, insights.Orders.Value);
        Assert.Equal(50, insights.SuccessRate.Value); // 1 delivered, 1 returned

        var baghdad = insights.Governorates.Single(g => g.Code == "IQ-BG");
        Assert.Equal((3, 1, 1), (baghdad.Orders, baghdad.Delivered, baghdad.Returned));
        Assert.Equal(ProductCategory.Fashion, baghdad.TopCategory);
        Assert.Equal(DeliveryFailureReason.CustomerRefused, baghdad.TopFailureReason);
        Assert.Equal(113_333, baghdad.AvgBasketIqd);
        Assert.Equal(insights.Governorates[0], baghdad); // busiest first
        Assert.Equal(1, insights.Governorates.Single(g => g.Code == "IQ-BA").Orders);

        Assert.Equal(2, insights.Categories.Single(c => c.Category == ProductCategory.Fashion).Orders);
        Assert.Equal(1, insights.Categories.Single(c => c.Category == ProductCategory.Electronics).Returned);
        Assert.Equal(1, insights.Categories.Single(c => c.Category == ProductCategory.Beauty).Orders);

        Assert.Equal(3, insights.Customers.Customers);
        Assert.Equal(1, insights.Customers.RepeatCustomers);
        Assert.Equal(2, insights.Customers.Repeat.Orders);
        Assert.Equal(1, insights.Customers.Repeat.Delivered);

        var expensive = insights.Baskets.Single(b => b.Band == BasketBands.Of(250_000));
        Assert.Equal((1, 1), (expensive.Orders, expensive.Refused));
        Assert.Equal(1, insights.Baskets.Single(b => b.Band == BasketBands.NoCod).Orders);
        Assert.Equal(4, insights.Baskets.Sum(b => b.Orders));

        var lane = insights.Lanes.Single(l => l.DestinationCode == "IQ-BG");
        Assert.Equal(("IQ-BG", 3), (lane.OriginCode, lane.Orders));

        Assert.Equal(7 * 24, insights.OrderTimes.Count);
        Assert.Equal(4, insights.OrderTimes.Sum(c => c.Orders));

        var dresses = insights.Terms.Single(t => t.Term == "فستان");
        Assert.Equal((2, ProductCategory.Fashion), (dresses.Orders, dresses.Category));
    }

    [Fact]
    public async Task Open_data_export_leaves_out_groups_smaller_than_the_minimum()
    {
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var shop = await client.CreateMerchantAsync("متجر البيانات المفتوحة", "opendata.flow.test");
        await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون واحد"));

        var response = await client.GetAsync("/" + ShipmentsApiRoutes.InsightsOpenData + "?days=30");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var lines = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("period_start,period_end,section,key,name,orders", lines[0], StringComparison.Ordinal);
        Assert.Single(lines); // one order is far below the minimum group size, so nothing is published
    }

    [Fact]
    public async Task Insights_need_a_signed_in_user()
    {
        var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/" + ShipmentsApiRoutes.Insights)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/" + ShipmentsApiRoutes.InsightsOpenData)).StatusCode);
    }
}
