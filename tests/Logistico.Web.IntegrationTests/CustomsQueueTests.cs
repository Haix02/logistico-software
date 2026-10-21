using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;

namespace Logistico.Web.IntegrationTests;

public sealed class CustomsQueueTests(LogisticoApiFactory factory) : IClassFixture<LogisticoApiFactory>
{
    [Fact]
    public async Task Customs_queue_is_filtered_and_paged_in_the_database()
    {
        var client = factory.CreateUserClient();
        for (var i = 0; i < 3; i++)
        {
            await client.BookAsync(ApiTestHelpers.InternationalBooking($"Customs Paging Exports {i}"));
        }

        var page1 = await client.GetFromJsonAsync<CustomsSummary>($"/{ShipmentsApiRoutes.Customs}?q=Customs%20Paging&page=1&pageSize=2");
        var page2 = await client.GetFromJsonAsync<CustomsSummary>($"/{ShipmentsApiRoutes.Customs}?q=Customs%20Paging&page=2&pageSize=2");
        var unassigned = await client.GetFromJsonAsync<CustomsSummary>($"/{ShipmentsApiRoutes.Customs}?q=Customs%20Paging&lane=Unassigned");
        var green = await client.GetFromJsonAsync<CustomsSummary>($"/{ShipmentsApiRoutes.Customs}?q=Customs%20Paging&lane=Green");
        var atUmmQasr = await client.GetFromJsonAsync<CustomsSummary>($"/{ShipmentsApiRoutes.Customs}?q=Customs%20Paging&office=IQUQR");
        var atBaghdadAirport = await client.GetFromJsonAsync<CustomsSummary>($"/{ShipmentsApiRoutes.Customs}?q=Customs%20Paging&office=IQBGW");

        Assert.Equal(3, page1!.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2!.Items);
        Assert.Empty(page1.Items.Select(i => i.ShipmentId).Intersect(page2.Items.Select(i => i.ShipmentId)));
        Assert.Equal(3, unassigned!.TotalCount); // nothing lodged yet → no selectivity lane
        Assert.Equal(0, green!.TotalCount);
        Assert.Equal(3, atUmmQasr!.TotalCount); // the test bookings go to Umm Qasr
        Assert.Equal(0, atBaghdadAirport!.TotalCount);
    }
}
