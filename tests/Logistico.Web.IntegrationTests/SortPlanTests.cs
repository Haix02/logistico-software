using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel.Identity;

namespace Logistico.Web.IntegrationTests;

/// <summary>Hub sort plans through the API: the shelf chosen at the scan, overflow, the plan's view and who may change it.</summary>
[Collection("Api")]
public sealed class SortPlanTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task Receive_scans_without_a_shelf_follow_the_hub_sort_plan()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var shop = await admin.CreateMerchantAsync("متجر الفرز", "sort.plan.shop");
        await AddRuleAsync(admin, new ShelfRuleRequest { Purpose = ShelfPurpose.Deliver, Governorate = "IQ-BG", Area = "الكرادة", Shelves = ["K-1", "K-2"], CapacityPerShelf = 1 });
        await AddRuleAsync(admin, new ShelfRuleRequest { Purpose = ShelfPurpose.Forward, ToHubCode = IraqHubs.Basra, Shelves = ["T-BSR"] });
        var catchAll = await AddRuleAsync(admin, new ShelfRuleRequest { Shelves = ["z-1"], CapacityPerShelf = 1, Note = "كل ما تبقى" });
        Assert.Equal("Z-1", Assert.Single(catchAll.Shelves));

        // Two shelves of one parcel each for Karrada, then the catch-all.
        var karrada = new List<ScanOutcome>();
        for (var i = 0; i < 3; i++)
        {
            var order = await admin.BookAsync(LocalOrder(shop, "الكراده خارج"));
            karrada.Add(await admin.ScanAsync(ScanOperation.Receive, order.TrackingNumber, IraqHubs.Baghdad));
        }

        Assert.Equal(["K-1", "K-2", "Z-1"], karrada.Select(o => o.WarehouseBin));
        Assert.All(karrada, o => Assert.True(o.ShelfFromPlan));
        Assert.Contains("K-1", karrada[0].Message, StringComparison.Ordinal);

        // Waiting for the Basra truck: the lane's shelf. A shelf typed at the station always wins.
        var toBasra = await admin.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون البصرة"));
        Assert.Equal("T-BSR", (await admin.ScanAsync(ScanOperation.Receive, toBasra.TrackingNumber, IraqHubs.Baghdad)).WarehouseBin);
        var typed = await admin.BookAsync(LocalOrder(shop, "الكرادة"));
        var manual = await admin.ScanAsync(ScanOperation.Receive, typed.TrackingNumber, IraqHubs.Baghdad, "a-10");
        Assert.Equal(("A-10", false), (manual.WarehouseBin, manual.ShelfFromPlan));

        // Scanned again without a shelf: it already sits on a shelf the plan allows, so it stays.
        var again = await admin.ScanAsync(ScanOperation.Receive, karrada[1].TrackingNumber, IraqHubs.Baghdad);
        Assert.Equal("K-2", again.WarehouseBin);

        // The plan: most specific rule first; every shelf in natural order with its load and capacity.
        var plan = (await admin.GetFromJsonAsync<SortPlanView>("/" + ShipmentsApiRoutes.SortPlanFor(IraqHubs.Baghdad)))!;
        Assert.Equal(["K-1", "T-BSR", "Z-1"], plan.Rules.Select(r => r.Shelves[0]));
        Assert.Equal(["A-10", "K-1", "K-2", "T-BSR", "Z-1"], plan.Shelves.Select(s => s.Shelf));
        var k1 = plan.Shelves.Single(s => s.Shelf == "K-1");
        Assert.Equal((1, 1, true), (k1.Parcels, k1.Capacity, k1.IsFull));
        Assert.Null(plan.Shelves.Single(s => s.Shelf == "A-10").RuleId);

        // What the plan would do with the next Karrada parcel, without scanning it: every shelf for it is full, so the
        // emptiest (the first among equals), and the scan says so.
        var next = await admin.BookAsync(LocalOrder(shop, "الكرادة"));
        var suggestion = (await admin.GetFromJsonAsync<ShelfSuggestion>("/" + ShipmentsApiRoutes.SortPlanSuggest(IraqHubs.Baghdad, next.TrackingNumber)))!;
        Assert.Equal((ShelfPurpose.Deliver, "K-1", true, false), (suggestion.Purpose, suggestion.Shelf, suggestion.Full, suggestion.AlreadyThere));
        var full = await admin.ScanAsync(ScanOperation.Receive, next.TrackingNumber, IraqHubs.Baghdad);
        Assert.Equal(("K-1", true), (full.WarehouseBin, full.ShelfFull));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/" + ShipmentsApiRoutes.SortPlanSuggest(IraqHubs.Baghdad, "LX26999999990"))).StatusCode);
    }

    [Fact]
    public async Task Rules_are_checked_changed_and_removed()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var route = "/" + ShipmentsApiRoutes.SortPlan + "/rules";

        var sameHub = await admin.PostAsJsonAsync(route, new ShelfRuleRequest { HubCode = IraqHubs.Baghdad, Purpose = ShelfPurpose.Forward, ToHubCode = IraqHubs.Baghdad, Shelves = ["T-1"] });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sameHub.StatusCode);
        var unknownHub = await admin.PostAsJsonAsync(route, new ShelfRuleRequest { HubCode = "HUB-XYZ", Shelves = ["T-1"] });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknownHub.StatusCode);
        var noShelves = await admin.PostAsJsonAsync(route, new ShelfRuleRequest { HubCode = IraqHubs.Baghdad, Shelves = [] });
        Assert.Equal(HttpStatusCode.BadRequest, noShelves.StatusCode);
        var unknownSeller = await admin.PostAsJsonAsync(route, new ShelfRuleRequest { HubCode = IraqHubs.Baghdad, MerchantId = Guid.CreateVersion7(), Shelves = ["M-1"] });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknownSeller.StatusCode);

        var shop = await admin.CreateMerchantAsync("متجر الرف", "sort.plan.rules");
        var rule = await AddRuleAsync(admin, new ShelfRuleRequest { MerchantId = shop, Shelves = ["M-1"] });
        Assert.Equal(("متجر الرف", 16), (rule.MerchantName, rule.Specificity));

        var changed = await admin.PutAsJsonAsync("/" + ShipmentsApiRoutes.SortPlanRule(rule.Id),
            new ShelfRuleRequest { HubCode = IraqHubs.Baghdad, MerchantId = shop, Purpose = ShelfPurpose.Return, Shelves = ["R-2", "R-3"], CapacityPerShelf = 40 });
        Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());
        Assert.Equal(["R-2", "R-3"], (await changed.Content.ReadFromJsonAsync<ShelfRuleItem>())!.Shelves);
        var moved = await admin.PutAsJsonAsync("/" + ShipmentsApiRoutes.SortPlanRule(rule.Id), new ShelfRuleRequest { HubCode = IraqHubs.Basra, Shelves = ["R-2"] });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, moved.StatusCode);

        // Other companies never see or touch it.
        var stranger = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync("/" + ShipmentsApiRoutes.SortPlanRule(rule.Id))).StatusCode);
        Assert.Empty((await stranger.GetFromJsonAsync<SortPlanView>("/" + ShipmentsApiRoutes.SortPlanFor(IraqHubs.Baghdad)))!.Rules);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/" + ShipmentsApiRoutes.SortPlanRule(rule.Id))).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<SortPlanView>("/" + ShipmentsApiRoutes.SortPlanFor(IraqHubs.Baghdad)))!.Rules);
    }

    [Fact]
    public async Task Office_roles_read_the_plan_and_only_operations_change_it()
    {
        var company = Guid.CreateVersion7();
        var viewer = factory.CreateUserClient("viewer@logistico.test", company, StaffRoles.Viewer);
        var operations = factory.CreateUserClient("ops@logistico.test", company, StaffRoles.Operations);
        var request = new ShelfRuleRequest { HubCode = IraqHubs.Baghdad, Shelves = ["A-1"] };

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/" + ShipmentsApiRoutes.SortPlanFor(IraqHubs.Baghdad))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/" + ShipmentsApiRoutes.SortPlan + "/rules", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await operations.PostAsJsonAsync("/" + ShipmentsApiRoutes.SortPlan + "/rules", request)).StatusCode);
    }

    [Fact]
    public async Task Shelves_list_in_natural_order()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var shop = await admin.CreateMerchantAsync("متجر الترتيب", "natural.order.shop");
        foreach (var shelf in new[] { "S-10", "S-2", "S-1-10", "S-1-9" })
        {
            var order = await admin.BookAsync(LocalOrder(shop, "المنصور"));
            await admin.ScanAsync(ScanOperation.Receive, order.TrackingNumber, IraqHubs.Baghdad, shelf);
        }

        var list = (await admin.GetFromJsonAsync<ShipmentListResult>(
            "/" + ShipmentsApiRoutes.Shipments + $"?hub={IraqHubs.Baghdad}&inWarehouse=true&sort=bin&desc=false"))!;
        Assert.Equal(["S-1-9", "S-1-10", "S-2", "S-10"], list.Items.Select(s => s.WarehouseBin));
    }

    private static async Task<ShelfRuleItem> AddRuleAsync(HttpClient client, ShelfRuleRequest rule)
    {
        rule.HubCode = rule.HubCode.Length == 0 ? IraqHubs.Baghdad : rule.HubCode;
        var response = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.SortPlan + "/rules", rule);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ShelfRuleItem>())!;
    }

    private static CreateShipmentRequest LocalOrder(Guid shop, string area)
    {
        var order = ApiTestHelpers.DomesticOrder(shop, "زبون بغداد");
        order.DestinationCode = "IQ-BG";
        order.DestinationCity = area;
        return order;
    }
}
