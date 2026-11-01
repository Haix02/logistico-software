using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Logistico.Modules.Shipments;
using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Mobile;

namespace Logistico.Web.IntegrationTests;

/// <summary>A database filled the way Docker and development fill it: demo staff, a demo seller, shops and orders.</summary>
public sealed class DemoDataFactory : LogisticoApiFactory
{
    public const int Orders = 150;
    public const string Password = "Logistico#2026";

    protected override IEnumerable<(string Key, string Value)> ExtraSettings =>
    [
        ("Seed:DemoShipments", Orders.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ("Seed:AdminEmail", "admin@logistico.local"),
        ("Seed:AdminPassword", Password),
        ("Seed:DemoTeam", "true"),
    ];
}

/// <summary>
/// The demo data people first see (docker compose, development) must load, be consistent, and work on every screen:
/// it is the product's first impression and the realistic data the other tests do not build.
/// </summary>
public sealed class DemoDataTests(DemoDataFactory factory) : IClassFixture<DemoDataFactory>
{
    private HttpClient Admin => factory.CreateUserClient(tenant: Tenants.Default);

    [Fact]
    public async Task The_demo_company_is_complete()
    {
        var all = (await Admin.GetFromJsonAsync<ShipmentListResult>("/" + ShipmentsApiRoutes.Shipments + "?pageSize=200"))!;
        Assert.True(all.TotalCount >= DemoDataFactory.Orders, $"only {all.TotalCount} orders");
        var statuses = all.Items.Select(s => s.Status).Distinct().ToList();
        Assert.True(statuses.Count >= 8, "the demo should show orders at many stages: " + string.Join(", ", statuses));
        Assert.Contains(all.Items, s => s.ServiceType == ServiceType.International);

        var fleet = (await Admin.GetFromJsonAsync<List<CourierLookupItem>>("/" + ShipmentsApiRoutes.FleetLookup))!;
        Assert.NotEmpty(fleet);
        var finance = (await Admin.GetFromJsonAsync<FinanceSummary>("/" + ShipmentsApiRoutes.FinanceSummary))!;
        Assert.NotEmpty(finance.RecentSettlements);
        Assert.NotEmpty(finance.SellerBalances);
    }

    [Fact]
    public async Task Money_adds_up_in_the_demo_data()
    {
        var finance = (await Admin.GetFromJsonAsync<FinanceSummary>("/" + ShipmentsApiRoutes.FinanceSummary))!;
        foreach (var b in finance.SellerBalances)
        {
            Assert.Equal(b.GrossCodIqd - b.DeliveryFeesIqd - b.ReturnFeesIqd, b.NetPayableIqd);
        }

        foreach (var s in finance.RecentSettlements)
        {
            Assert.Equal(s.GrossCodAmount - s.DeliveryFeesAmount - s.ReturnFeesAmount, s.NetPayoutAmount);
            Assert.True(s.ShipmentCount > 0);
        }

        Assert.True(finance.CodWithCouriersIqd >= 0 && finance.UnsettledNetPayableIqd == finance.SellerBalances.Sum(b => b.NetPayableIqd));
    }

    [Theory]
    [InlineData(ShipmentsApiRoutes.AnalyticsOverview + "?days=1")]
    [InlineData(ShipmentsApiRoutes.AnalyticsOverview + "?days=90")]
    [InlineData(ShipmentsApiRoutes.Insights + "?days=365")]
    [InlineData(ShipmentsApiRoutes.Summary)]
    [InlineData(ShipmentsApiRoutes.Counters)]
    [InlineData(ShipmentsApiRoutes.Network)]
    [InlineData(ShipmentsApiRoutes.NetworkHubs)]
    [InlineData(ShipmentsApiRoutes.Warehouses)]
    [InlineData(ShipmentsApiRoutes.WarehouseRadar)]
    [InlineData(ShipmentsApiRoutes.Transfers)]
    [InlineData(ShipmentsApiRoutes.Returns)]
    [InlineData(ShipmentsApiRoutes.Requests)]
    [InlineData(ShipmentsApiRoutes.Confirmations)]
    [InlineData(ShipmentsApiRoutes.AutomationOverview)]
    [InlineData(ShipmentsApiRoutes.AutomationAlerts + "?state=all")]
    [InlineData(ShipmentsApiRoutes.AutomationLog)]
    [InlineData(ShipmentsApiRoutes.AutomationMessages)]
    [InlineData(ShipmentsApiRoutes.Customs)]
    [InlineData(ShipmentsApiRoutes.Fleet)]
    [InlineData(ShipmentsApiRoutes.CashHandovers)]
    [InlineData(ShipmentsApiRoutes.FinanceSummary)]
    [InlineData(ShipmentsApiRoutes.ShelfChecks)]
    [InlineData(ShipmentsApiRoutes.Pricing)]
    [InlineData("api/v1/merchants")]
    [InlineData("api/v1/audit")]
    public async Task Every_screen_reads_the_demo_data(string route)
    {
        var response = await Admin.GetAsync("/" + route);
        Assert.True(response.IsSuccessStatusCode, $"{route}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    [Theory]
    [InlineData("trackingNumber")]
    [InlineData("status")]
    [InlineData("consignee")]
    [InlineData("merchant")]
    [InlineData("cod")]
    [InlineData("eta")]
    [InlineData("destination")]
    [InlineData("age")]
    [InlineData("hub")]
    [InlineData("bin")]
    [InlineData("createdAt")]
    public async Task The_order_list_sorts_by_every_column_both_ways(string key)
    {
        foreach (var desc in new[] { false, true })
        {
            var page = (await Admin.GetFromJsonAsync<ShipmentListResult>($"/{ShipmentsApiRoutes.Shipments}?sort={key}&desc={desc}&pageSize=50"))!;
            Assert.Equal(50, page.Items.Count);
        }
    }

    [Theory]
    [InlineData("admin@logistico.local", AppKinds.Manager)]
    [InlineData("courier@logistico.local", AppKinds.Courier)]
    [InlineData("seller@logistico.local", AppKinds.Sender)]
    public async Task The_demo_accounts_open_their_app(string email, string kind)
    {
        var tokens = await factory.CreateClient().PostAsJsonAsync("/" + MobileApiRoutes.Token, new { email, password = DemoDataFactory.Password });
        Assert.True(tokens.IsSuccessStatusCode, await tokens.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await tokens.Content.ReadAsStringAsync());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
        var config = (await client.GetFromJsonAsync<MobileAppConfig>("/" + MobileApiRoutes.Config))!;
        Assert.Equal(kind, config.Kind);
        Assert.NotEmpty(config.Modules);
    }

    [Fact]
    public async Task Seeding_again_adds_nothing()
    {
        var before = (await Admin.GetFromJsonAsync<ShipmentListResult>("/" + ShipmentsApiRoutes.Shipments + "?pageSize=1"))!.TotalCount;
        await factory.Services.SeedShipmentsDemoDataAsync(DemoDataFactory.Orders);
        var after = (await Admin.GetFromJsonAsync<ShipmentListResult>("/" + ShipmentsApiRoutes.Shipments + "?pageSize=1"))!.TotalCount;
        Assert.Equal(before, after);
        Assert.Equal(HttpStatusCode.OK, (await Admin.GetAsync("/" + ShipmentsApiRoutes.FinanceSummary)).StatusCode);
    }
}
