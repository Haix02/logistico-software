using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Web.IntegrationTests;

/// <summary>The automation sweep raises alerts when parcels and trucks run late and clears them when the problem is gone.</summary>
public sealed class AutomationTests(LogisticoApiFactory factory) : IClassFixture<LogisticoApiFactory>
{
    [Fact]
    public async Task Late_pickups_and_late_trucks_raise_alerts_that_clear_themselves()
    {
        var client = factory.CreateUserClient();
        var shop = await client.CreateMerchantAsync("متجر الأتمتة", "automation.flow.test");
        var created = await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون الأتمتة"));

        // Nothing is late yet.
        await client.RunAutomationAsync();
        Assert.DoesNotContain(await OpenAlertsAsync(client), a => a.ShipmentId == created.Id);

        // A day and a half later the seller still has the parcel: pickup overdue.
        factory.Clock.Advance(TimeSpan.FromHours(30));
        var swept = await client.RunAutomationAsync();
        Assert.True(swept.Ran);
        Assert.Contains(await OpenAlertsAsync(client), a => a.ShipmentId == created.Id && a.Kind == AlertKind.PickupOverdue && a.Hours >= 30);

        // Collected and received at the hub: the alert clears and the parcel is planned on the truck to Basra.
        await client.RecordAsync(created.Id, new RecordShipmentEventRequest { Status = ShipmentStatus.PickedUp }, await client.ETagAsync(created.Id));
        await client.ScanAsync(ScanOperation.Receive, created.TrackingNumber, IraqHubs.Baghdad, "A-01-1");
        await client.RunAutomationAsync();
        Assert.DoesNotContain(await OpenAlertsAsync(client), a => a.ShipmentId == created.Id && a.Kind == AlertKind.PickupOverdue);

        // The truck leaves and never arrives in time: truck late.
        await client.ScanAsync(ScanOperation.Load, created.TrackingNumber, IraqHubs.Baghdad);
        var trucks = (await client.GetFromJsonAsync<TransferListResult>("/" + ShipmentsApiRoutes.Transfers + "?status=Loading"))!;
        var truck = Assert.Single(trucks.Items);
        var get = await client.GetAsync("/" + ShipmentsApiRoutes.TransferById(truck.Id));
        using (var depart = new HttpRequestMessage(HttpMethod.Post, "/" + ShipmentsApiRoutes.TransferDepart(truck.Id)) { Content = JsonContent.Create(new DepartTransferRequest()) })
        {
            depart.Headers.IfMatch.Add(get.Headers.ETag!);
            Assert.True((await client.SendAsync(depart)).IsSuccessStatusCode);
        }

        factory.Clock.Advance(TimeSpan.FromHours(IraqHubs.Default.TransitHours(IraqHubs.Baghdad, IraqHubs.Basra) + new AutomationPolicy().LineHaulGraceHours + 1));
        await client.RunAutomationAsync();
        Assert.Contains(await OpenAlertsAsync(client), a => a.TransferId == truck.Id && a.Kind == AlertKind.TransferOverdue);

        // It arrives: unloaded at Basra, the alert clears.
        await client.ScanAsync(ScanOperation.Receive, created.TrackingNumber, IraqHubs.Basra, "B-01-1");
        await client.RunAutomationAsync();
        Assert.DoesNotContain(await OpenAlertsAsync(client), a => a.TransferId == truck.Id && a.Kind == AlertKind.TransferOverdue);

        // Everything the engine did is in the activity log; the side-menu counters reflect the state.
        var log = (await client.GetFromJsonAsync<AutomationLogPage>("/" + ShipmentsApiRoutes.AutomationLog + "?pageSize=200"))!;
        Assert.Contains(log.Items, e => e.Rule == AutomationRule.AlertRaised && e.Detail == nameof(AlertKind.PickupOverdue));
        Assert.Contains(log.Items, e => e.Rule == AutomationRule.AlertResolved && e.Detail == nameof(AlertKind.TransferOverdue));
        Assert.Contains(log.Items, e => e.Rule == AutomationRule.PlanTransfer && e.TrackingNumber == created.TrackingNumber);
        var counters = (await client.GetFromJsonAsync<OpsCounters>("/" + ShipmentsApiRoutes.Counters))!;
        Assert.Equal(0, counters.TrucksOnRoad);

        var network = (await client.GetFromJsonAsync<NetworkOverview>("/" + ShipmentsApiRoutes.Network))!;
        Assert.Contains(network.Governorates, g => g.Code == "IQ-BA" && g.AtHub == 1);
        Assert.Contains(network.Hubs, h => h.HubCode == IraqHubs.Basra && h.ToDispatch == 1);
    }

    private static async Task<IReadOnlyList<OpsAlertItem>> OpenAlertsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<AlertList>("/" + ShipmentsApiRoutes.AutomationAlerts + "?state=open&pageSize=200"))!.Items;
}
