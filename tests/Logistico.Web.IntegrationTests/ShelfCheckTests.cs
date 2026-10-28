using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;

namespace Logistico.Web.IntegrationTests;

/// <summary>Shelf checks: count a shelf against the system and see what is missing, misplaced or not expected.</summary>
[Collection("Api")]
public sealed class ShelfCheckTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task A_shelf_count_finds_missing_misplaced_and_unexpected_parcels()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var shop = await admin.CreateMerchantAsync("Shelf Shop", "shelf.shop");
        var onShelf = await ReceiveAsync(admin, shop, "A-07-1", "0770 222 0001");
        var lost = await ReceiveAsync(admin, shop, "A-07-1", "0770 222 0002");
        var otherShelf = await ReceiveAsync(admin, shop, "B-02-3", "0770 222 0003");
        var notReceived = await admin.BookAsync(Order(shop, "0770 222 0004"));

        var started = await admin.PostAsJsonAsync("/" + ShipmentsApiRoutes.ShelfChecks, new StartShelfCheckRequest { HubCode = IraqHubs.Baghdad, Bin = "a-07-1" });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var check = (await started.Content.ReadFromJsonAsync<ShelfCheckDetails>())!.Summary;
        Assert.StartsWith("SC-", check.Number, StringComparison.Ordinal);
        Assert.Equal(("A-07-1", 2, 2), (check.Bin, check.Expected, check.Pending));

        // A second clerk starting the same shelf joins the count instead of starting another.
        var joined = await admin.PostAsJsonAsync("/" + ShipmentsApiRoutes.ShelfChecks, new StartShelfCheckRequest { HubCode = IraqHubs.Baghdad, Bin = "A-07-1" });
        Assert.Equal((HttpStatusCode.OK, check.Id), (joined.StatusCode, (await joined.Content.ReadFromJsonAsync<ShelfCheckDetails>())!.Summary.Id));

        Assert.Equal(ShelfCheckState.Found, (await ScanAsync(admin, check.Id, " " + onShelf.TrackingNumber.ToLowerInvariant())).Line.State);
        Assert.Equal(ShelfCheckState.Found, (await ScanAsync(admin, check.Id, onShelf.TrackingNumber)).Line.State); // twice: no change
        var misplaced = await ScanAsync(admin, check.Id, otherShelf.TrackingNumber);
        Assert.Equal((ShelfCheckState.WrongShelf, "B-02-3"), (misplaced.Line.State, misplaced.Line.SystemBin));
        Assert.Equal(ShelfCheckState.NotExpected, (await ScanAsync(admin, check.Id, notReceived.TrackingNumber)).Line.State);
        Assert.Equal(ShelfCheckState.NotExpected, (await ScanAsync(admin, check.Id, "LX99999999999")).Line.State); // unknown label

        var finished = await admin.PostAsync("/" + ShipmentsApiRoutes.ShelfCheckFinish(check.Id), null);
        var result = (await finished.Content.ReadFromJsonAsync<ShelfCheckDetails>())!;
        Assert.Equal((true, 2, 1, 1, 1, 2), (result.Summary.Finished, result.Summary.Expected, result.Summary.Found, result.Summary.Missing, result.Summary.WrongShelf, result.Summary.NotExpected));
        Assert.Equal(lost.TrackingNumber, Assert.Single(result.Lines, l => l.State == ShelfCheckState.Missing).TrackingNumber);

        // A finished count takes no more scans, and it is listed for the hub.
        var late = await admin.PostAsJsonAsync("/" + ShipmentsApiRoutes.ShelfCheckScan(check.Id), new ShelfCheckScanRequest { TrackingNumber = lost.TrackingNumber });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, late.StatusCode);
        var listed = (await admin.GetFromJsonAsync<PagedResult<ShelfCheckSummary>>("/" + ShipmentsApiRoutes.ShelfChecks + "?hub=" + IraqHubs.Baghdad))!;
        Assert.Equal(check.Id, Assert.Single(listed.Items).Id);
    }

    [Fact]
    public async Task Only_hub_staff_count_and_other_companies_see_nothing()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var start = new StartShelfCheckRequest { HubCode = IraqHubs.Baghdad };
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateUserClient(tenant: company, role: StaffRoles.Viewer).PostAsJsonAsync("/" + ShipmentsApiRoutes.ShelfChecks, start)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/" + ShipmentsApiRoutes.ShelfChecks, new StartShelfCheckRequest { HubCode = "HUB-NOPE" })).StatusCode);

        var whole = (await (await admin.PostAsJsonAsync("/" + ShipmentsApiRoutes.ShelfChecks, start)).Content.ReadFromJsonAsync<ShelfCheckDetails>())!.Summary;
        Assert.Null(whole.Bin);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateUserClient(tenant: Guid.CreateVersion7()).GetAsync("/" + ShipmentsApiRoutes.ShelfCheck(whole.Id))).StatusCode);
    }

    private static CreateShipmentRequest Order(Guid shop, string phone)
    {
        var order = ApiTestHelpers.DomesticOrder(shop, "زبون الجرد");
        order.DestinationCode = "IQ-BG";
        order.ConsigneePhone = phone;
        return order;
    }

    private static async Task<CreateShipmentResponse> ReceiveAsync(HttpClient admin, Guid shop, string bin, string phone)
    {
        var booked = await admin.BookAsync(Order(shop, phone));
        await admin.ScanAsync(ScanOperation.Receive, booked.TrackingNumber, IraqHubs.Baghdad, bin);
        return booked;
    }

    private static async Task<ShelfCheckScanResult> ScanAsync(HttpClient client, Guid id, string trackingNumber)
    {
        var response = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.ShelfCheckScan(id), new ShelfCheckScanRequest { TrackingNumber = trackingNumber });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ShelfCheckScanResult>())!;
    }
}
