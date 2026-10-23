using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;

namespace Logistico.Web.IntegrationTests;

/// <summary>The dashboard compares a period still under way with the same stretch of the period before.</summary>
[Collection("Api")]
public sealed class DashboardTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task Today_so_far_is_compared_with_yesterday_until_the_same_hour()
    {
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var shop = await client.CreateMerchantAsync("متجر اللوحة", "dashboard.compare.test");

        // Yesterday: one order at this hour, one an hour later.
        await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون مبكر"));
        factory.Clock.Advance(TimeSpan.FromHours(1));
        await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون متأخر"));
        factory.Clock.Advance(TimeSpan.FromHours(23));

        var today = (await client.GetFromJsonAsync<AnalyticsOverview>("/" + ShipmentsApiRoutes.AnalyticsOverview + "?days=1"))!;

        Assert.Equal(1, today.Booked.Previous); // the later order had not been booked by this time yesterday
    }
}
