using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;

namespace Logistico.Web.IntegrationTests;

/// <summary>
/// The optimization engine through the API (docs/20): settings, planning trucks, courier runs and zones from live data,
/// approving a plan through the normal domain rules, and who may do what.
/// </summary>
[Collection("Api")]
public sealed class PlanningTests(LogisticoApiFactory factory)
{
    /// <summary>A new company whose plans search for one second (courier runs search until the time limit).</summary>
    private async Task<HttpClient> CompanyAsync() => (await CompanyWithIdAsync()).Client;

    private async Task<(HttpClient Client, Guid Tenant)> CompanyWithIdAsync()
    {
        var tenant = Guid.CreateVersion7();
        var client = factory.CreateUserClient(tenant: tenant);
        var get = await client.GetAsync("/" + ShipmentsApiRoutes.PlanningPolicy);
        var policy = (await get.Content.ReadFromJsonAsync<PlanningPolicy>())!;
        policy.TimeLimitSeconds = 1;
        var put = await client.PutWithETagAsync(ShipmentsApiRoutes.PlanningPolicy, policy, get.Headers.ETag!);
        Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());
        return (client, tenant);
    }

    private static async Task<PlanRunDetails> PlanAsync(HttpClient client, PlanKind kind, string? hub = null, int? zones = null)
    {
        var response = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.PlanningRuns, new StartPlanRequest { Kind = kind, HubCode = hub, Zones = zones });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var details = (await response.Content.ReadFromJsonAsync<PlanRunDetails>())!;
        Assert.Empty(details.Violations);
        return details;
    }

    private static async Task<HttpResponseMessage> ApplyAsync(HttpClient client, Guid id)
    {
        var get = await client.GetAsync("/" + ShipmentsApiRoutes.PlanningRun(id));
        return await client.PostWithETagAsync<object?>(ShipmentsApiRoutes.PlanningRunApply(id), null, get.Headers.ETag!);
    }

    private static CreateShipmentRequest BaghdadOrder(Guid shop, string customer, decimal cod)
    {
        var order = ApiTestHelpers.DomesticOrder(shop, customer);
        order.DestinationCode = "IQ-BG";
        order.DestinationCity = "المنصور";
        order.CodAmount = cod;
        return order;
    }

    [Fact]
    public async Task Waiting_parcels_get_a_proven_truck_plan_and_approving_it_loads_the_next_truck()
    {
        var client = await CompanyAsync();
        var shop = await client.CreateMerchantAsync("متجر الخطة", "planning.trucks");
        var parcels = new List<CreateShipmentResponse>();
        for (var i = 0; i < 3; i++)
        {
            parcels.Add(await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون البصرة " + i)));
            await client.ScanAsync(ScanOperation.Receive, parcels[^1].TrackingNumber, IraqHubs.Baghdad, "A-01-" + i);
        }

        var plan = await PlanAsync(client, PlanKind.Trucks);

        Assert.Equal((PlanKind.Trucks, PlanRunStatus.Ready, PlanQuality.Optimal), (plan.Summary.Kind, plan.Summary.Status, plan.Summary.Quality));
        var trucks = plan.Trucks!;
        Assert.Equal(3, trucks.Plan.Parcels);
        var next = Assert.Single(trucks.Departures, d => d.IsNext);
        Assert.Equal((IraqHubs.Baghdad, IraqHubs.Basra, 1, 3), (next.FromHubCode, next.ToHubCode, next.Trucks, next.Parcels));
        Assert.True(trucks.Plan.CostIqd <= trucks.Baseline.CostIqd);
        Assert.Equal(3, trucks.EstimatedVolumes); // booked without a volume: estimated from the weight
        Assert.Equal(next.CostIqd, plan.Summary.CostIqd);

        var applied = await ApplyAsync(client, plan.Summary.Id);
        Assert.True(applied.IsSuccessStatusCode, await applied.Content.ReadAsStringAsync());
        var result = (await applied.Content.ReadFromJsonAsync<PlanApplyResult>())!;
        Assert.Equal((PlanRunStatus.Applied, 3, 0), (result.Summary.Status, result.Applied, result.Skipped));
        var truckNumber = Assert.Single(result.Transfers);
        var list = (await client.GetFromJsonAsync<TransferListResult>("/" + ShipmentsApiRoutes.Transfers + "?status=Loading"))!;
        var manifest = (await client.GetFromJsonAsync<TransferDetails>("/" + ShipmentsApiRoutes.TransferById(list.Items.Single(t => t.Number == truckNumber).Id)))!;
        Assert.Equal(parcels.Select(p => p.Id).Order(), manifest.Items.Select(i => i.ShipmentId).Order());

        // A plan is applied once.
        var again = await ApplyAsync(client, plan.Summary.Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
        Assert.Contains("planning.not_ready", await again.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Courier_runs_respect_the_cash_limit_and_approving_hands_parcels_out_in_the_planned_order()
    {
        var (client, tenant) = await CompanyWithIdAsync();
        var get = await client.GetAsync("/" + ShipmentsApiRoutes.PlanningPolicy);
        var policy = (await get.Content.ReadFromJsonAsync<PlanningPolicy>())!;
        policy.MaxCashPerCourierIqd = 150_000;
        Assert.True((await client.PutWithETagAsync(ShipmentsApiRoutes.PlanningPolicy, policy, get.Headers.ETag!)).IsSuccessStatusCode);
        await client.EnsureCourierAsync("BG-71", "علي");
        await client.EnsureCourierAsync("BG-72", "حسن");
        var shop = await client.CreateMerchantAsync("متجر الجولات", "planning.runs");
        var orders = new List<CreateShipmentResponse>();
        for (var i = 0; i < 4; i++)
        {
            orders.Add(await client.BookAsync(BaghdadOrder(shop, "زبون بغداد " + i, 60_000)));
            await client.ScanAsync(ScanOperation.Receive, orders[^1].TrackingNumber, IraqHubs.Baghdad, "B-01-" + i);
        }

        var plan = await PlanAsync(client, PlanKind.CourierRuns, IraqHubs.Baghdad);

        var runs = plan.Couriers!;
        Assert.Equal((4, 0), (runs.Plan.Planned, runs.Plan.Deferred));
        Assert.All(runs.Runs, r => Assert.True(r.CashIqd <= 150_000));
        Assert.Equal(2, runs.Runs.Count(r => r.Stops.Count > 0)); // 240,000 in cash needs both couriers
        Assert.Equal(4, runs.GovernorateStops + runs.AreaStops + runs.GpsStops);

        var applied = await ApplyAsync(client, plan.Summary.Id);
        Assert.True(applied.IsSuccessStatusCode, await applied.Content.ReadAsStringAsync());
        Assert.Equal(4, (await applied.Content.ReadFromJsonAsync<PlanApplyResult>())!.Applied);
        foreach (var run in runs.Runs.Where(r => r.Stops.Count > 0))
        {
            foreach (var stop in run.Stops)
            {
                var shipment = await client.GetShipmentAsync(stop.ShipmentId);
                Assert.Equal((ShipmentStatus.OutForDelivery, run.Courier), (shipment.Status, shipment.CourierName));
            }

            // The courier app lists the run in the planned order.
            var code = run.Courier[(run.Courier.IndexOf('(') + 1)..^1];
            var app = factory.CreateUserClient("courier@logistico.test", tenant, StaffRoles.Courier, courierCode: code);
            var listed = (await app.GetFromJsonAsync<CourierRun>("/" + ShipmentsApiRoutes.CourierRun))!;
            Assert.Equal(run.Stops.OrderBy(s => s.Sequence).Select(s => s.ShipmentId), listed.Stops.Select(s => s.Id));
        }
    }

    [Fact]
    public async Task Zones_split_a_hub_s_recent_deliveries_into_balanced_zones()
    {
        var client = await CompanyAsync();
        await client.EnsureCourierAsync("BG-81", "زيد");
        var shop = await client.CreateMerchantAsync("متجر المناطق", "planning.zones");
        string[] areas = ["المنصور", "الكرادة", "الأعظمية", "المنصور"];
        foreach (var (area, i) in areas.Select((a, i) => (a, i)))
        {
            var order = BaghdadOrder(shop, "زبون " + i, 25_000);
            order.DestinationCity = area;
            var booked = await client.BookAsync(order);
            await client.ScanAsync(ScanOperation.Receive, booked.TrackingNumber, IraqHubs.Baghdad, "C-01-" + i);
            await client.ScanAsync(ScanOperation.Dispatch, booked.TrackingNumber, IraqHubs.Baghdad, courier: "زيد (BG-81)");
            await client.ScanAsync(ScanOperation.Deliver, booked.TrackingNumber, IraqHubs.Baghdad, courier: "زيد (BG-81)");
        }

        var plan = await PlanAsync(client, PlanKind.Zones, IraqHubs.Baghdad, zones: 2);

        var zones = plan.Zones!;
        Assert.Equal(4, zones.Deliveries);
        Assert.Equal(2, zones.Zones.Count);
        Assert.Equal(3, zones.Zones.Sum(z => z.Areas.Count)); // المنصور twice is one area
        Assert.Contains(zones.Zones.SelectMany(z => z.Areas), a => a.Label == "المنصور" && a.Workload > 0);
    }

    [Fact]
    public async Task Settings_are_validated_and_only_administrators_change_them_or_viewers_plan()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var get = await admin.GetAsync("/" + ShipmentsApiRoutes.PlanningPolicy);
        var policy = (await get.Content.ReadFromJsonAsync<PlanningPolicy>())!;
        Assert.Equal(new[] { "06:00", "14:00", "22:00" }, policy.DepartureTimes);

        policy.DepartureTimes = ["25:00"];
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutWithETagAsync(ShipmentsApiRoutes.PlanningPolicy, policy, get.Headers.ETag!)).StatusCode);
        policy.DepartureTimes = ["15:30", "07:00"];
        policy.TruckCapacityKg = 12_000;
        var saved = await admin.PutWithETagAsync(ShipmentsApiRoutes.PlanningPolicy, policy, get.Headers.ETag!);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var stored = (await saved.Content.ReadFromJsonAsync<PlanningPolicy>())!;
        Assert.Equal(new[] { "07:00", "15:30" }, stored.DepartureTimes); // kept in order
        Assert.Equal(12_000d, stored.TruckCapacityKg);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await admin.PutWithETagAsync(ShipmentsApiRoutes.PlanningPolicy, policy, get.Headers.ETag!)).StatusCode);

        var operations = factory.CreateUserClient("ops@logistico.test", company, StaffRoles.Operations);
        var fresh = await operations.GetAsync("/" + ShipmentsApiRoutes.PlanningPolicy);
        Assert.Equal(HttpStatusCode.Forbidden, (await operations.PutWithETagAsync(ShipmentsApiRoutes.PlanningPolicy, policy, fresh.Headers.ETag!)).StatusCode);

        var viewer = factory.CreateUserClient("viewer@logistico.test", company, StaffRoles.Viewer);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/" + ShipmentsApiRoutes.PlanningRuns)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/" + ShipmentsApiRoutes.PlanningRuns, new StartPlanRequest { Kind = PlanKind.Trucks })).StatusCode);
    }

    [Fact]
    public async Task A_hub_planner_needs_a_known_hub_and_an_empty_network_gives_an_empty_plan()
    {
        var client = await CompanyAsync();

        var noHub = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.PlanningRuns, new StartPlanRequest { Kind = PlanKind.CourierRuns });
        Assert.Equal(HttpStatusCode.BadRequest, noHub.StatusCode);
        var unknown = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.PlanningRuns, new StartPlanRequest { Kind = PlanKind.Zones, HubCode = "HUB-XXX" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        Assert.Contains("planning.unknown_hub", await unknown.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var empty = await PlanAsync(client, PlanKind.Trucks);
        Assert.Equal(PlanQuality.Empty, empty.Summary.Quality);
        Assert.Empty(empty.Trucks!.Departures);

        var history = (await client.GetFromJsonAsync<PagedResult<PlanRunSummary>>("/" + ShipmentsApiRoutes.PlanningRuns + "?kind=Trucks"))!;
        Assert.Equal(empty.Summary.Id, Assert.Single(history.Items).Id);

        var get = await client.GetAsync("/" + ShipmentsApiRoutes.PlanningRun(empty.Summary.Id));
        var discarded = await client.PostWithETagAsync<object?>(ShipmentsApiRoutes.PlanningRunDiscard(empty.Summary.Id), null, get.Headers.ETag!);
        Assert.True(discarded.IsSuccessStatusCode, await discarded.Content.ReadAsStringAsync());
        Assert.Equal(PlanRunStatus.Discarded, (await discarded.Content.ReadFromJsonAsync<PlanRunSummary>())!.Status);
    }
}
