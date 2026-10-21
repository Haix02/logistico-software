using System.Net;

namespace Logistico.Web.IntegrationTests;

[Collection("Api")]
public sealed class HealthTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task Liveness_and_database_readiness_are_public_and_healthy()
    {
        using var client = factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
    }
}
