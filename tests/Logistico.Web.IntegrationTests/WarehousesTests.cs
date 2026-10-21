using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Web.IntegrationTests;

/// <summary>A company opens a branch in Kirkuk, routes Kirkuk's parcels through it, and later retires it.</summary>
[Collection("Api")]
public sealed class WarehousesTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task A_new_branch_takes_over_its_governorate_and_can_only_be_retired_when_empty()
    {
        // A company of its own: it starts with the standard five hubs.
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var start = (await client.GetFromJsonAsync<WarehouseList>("/" + ShipmentsApiRoutes.Warehouses))!;
        Assert.Equal(5, start.Items.Count);
        Assert.Empty(start.UncoveredGovernorates);
        Assert.Single(start.Items, w => w.IsPrimary);

        // Kirkuk is Erbil's today: taking it over needs an explicit confirmation.
        var branch = new WarehouseRequest
        {
            RoutingTag = "krk",
            NameAr = "فرع كركوك",
            NameEn = "Kirkuk Branch",
            Kind = HubKind.Branch,
            Governorate = "IQ-KI",
            Latitude = 35.468,
            Longitude = 44.392,
            Phone = "07701234567",
            Covers = ["IQ-KI"],
            ShelfCapacity = 400,
        };
        var conflict = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.Warehouses, branch);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, conflict.StatusCode);
        Assert.Contains("warehouse.coverage_conflict", await conflict.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        branch.TakeOverCoverage = true;
        var created = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.Warehouses, branch);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var kirkuk = (await created.Content.ReadFromJsonAsync<WarehouseDetails>())!;
        Assert.Equal("HUB-KRK", kirkuk.Code);
        Assert.Equal("KRK", kirkuk.RoutingTag);

        var taken = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.Warehouses, branch);
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);

        var badTag = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.Warehouses, new WarehouseRequest { RoutingTag = "K1", NameAr = "x", NameEn = "x", Governorate = "IQ-KI", Latitude = 1, Longitude = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, badTag.StatusCode);

        // The browser's network now routes Kirkuk to the branch, and Erbil no longer covers it.
        var network = (await client.GetFromJsonAsync<NetworkHubs>("/" + ShipmentsApiRoutes.NetworkHubs))!;
        Assert.Contains(network.Hubs, h => h.Code == "HUB-KRK" && h.Covers.Contains("IQ-KI") && h.Kind == HubKind.Branch);
        Assert.DoesNotContain("IQ-KI", network.Hubs.Single(h => h.Code == IraqHubs.Erbil).Covers);

        // New Kirkuk orders go to the branch; it can receive scans.
        var shop = await client.CreateMerchantAsync("متجر كركوك", "kirkuk.branch.test");
        var order = ApiTestHelpers.DomesticOrder(shop, "زبون كركوك");
        order.DestinationCode = "IQ-KI";
        var booked = await client.BookAsync(order);
        Assert.Equal("HUB-KRK", (await client.GetShipmentAsync(booked.Id)).DestinationHubCode);
        await client.ScanAsync(ScanOperation.Receive, booked.TrackingNumber, "HUB-KRK", "K-01-1");

        // Retiring is refused while the parcel is on its shelf.
        var get = await client.GetAsync("/" + ShipmentsApiRoutes.WarehouseById(kirkuk.Id));
        var retire = WarehouseRequest.From(kirkuk);
        retire.IsActive = false;
        var refused = await client.PutWithETagAsync(ShipmentsApiRoutes.WarehouseById(kirkuk.Id), retire, get.Headers.ETag!);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Contains("warehouse.not_empty", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // The routing tag is permanent.
        var renamed = WarehouseRequest.From(kirkuk);
        renamed.RoutingTag = "KIR";
        var locked = await client.PutWithETagAsync(ShipmentsApiRoutes.WarehouseById(kirkuk.Id), renamed, get.Headers.ETag!);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, locked.StatusCode);

        // Once the parcel left with a courier and was delivered, the branch can be retired; Kirkuk falls back to the nearest hub.
        await client.ScanAsync(ScanOperation.Dispatch, booked.TrackingNumber, "HUB-KRK", courier: "سرمد (Kirkuk-01)");
        await client.ScanAsync(ScanOperation.Deliver, booked.TrackingNumber, "HUB-KRK");
        get = await client.GetAsync("/" + ShipmentsApiRoutes.WarehouseById(kirkuk.Id));
        var retired = await client.PutWithETagAsync(ShipmentsApiRoutes.WarehouseById(kirkuk.Id), retire, get.Headers.ETag!);
        Assert.True(retired.IsSuccessStatusCode, await retired.Content.ReadAsStringAsync());

        var after = (await client.GetFromJsonAsync<WarehouseList>("/" + ShipmentsApiRoutes.Warehouses))!;
        Assert.Contains("IQ-KI", after.UncoveredGovernorates);
        var next = await client.BookAsync(order);
        Assert.Equal(IraqHubs.Erbil, (await client.GetShipmentAsync(next.Id)).DestinationHubCode);

        // The main warehouse moves, never disappears.
        var basra = after.Items.Single(w => w.Code == IraqHubs.Basra);
        var main = WarehouseRequest.From(basra);
        main.IsPrimary = true;
        var basraGet = await client.GetAsync("/" + ShipmentsApiRoutes.WarehouseById(basra.Id));
        Assert.True((await client.PutWithETagAsync(ShipmentsApiRoutes.WarehouseById(basra.Id), main, basraGet.Headers.ETag!)).IsSuccessStatusCode);
        var final = (await client.GetFromJsonAsync<WarehouseList>("/" + ShipmentsApiRoutes.Warehouses))!;
        Assert.Equal(IraqHubs.Basra, Assert.Single(final.Items, w => w.IsPrimary).Code);
    }
}
