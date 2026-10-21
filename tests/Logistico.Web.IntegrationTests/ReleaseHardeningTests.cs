using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;

namespace Logistico.Web.IntegrationTests;

[Collection("Api")]
public sealed class ReleaseHardeningTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task Role_policies_protect_mutations_finance_and_tenant_resolution()
    {
        var viewer = factory.CreateUserClient(role: StaffRoles.Viewer);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/" + ShipmentsApiRoutes.Shipments)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/" + ShipmentsApiRoutes.Shipments, ApiTestHelpers.InternationalBooking("blocked"))).StatusCode);

        var operations = factory.CreateUserClient(role: StaffRoles.Operations);
        Assert.Equal(HttpStatusCode.Forbidden, (await operations.GetAsync("/" + ShipmentsApiRoutes.FinanceSummary)).StatusCode);

        var finance = factory.CreateUserClient(role: StaffRoles.Finance);
        Assert.Equal(HttpStatusCode.OK, (await finance.GetAsync("/" + ShipmentsApiRoutes.FinanceSummary)).StatusCode);

        var invalidTenant = factory.CreateClient();
        invalidTenant.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "invalid-tenant@logistico.test");
        invalidTenant.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, "not-a-guid");
        invalidTenant.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, StaffRoles.Viewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await invalidTenant.GetAsync("/" + ShipmentsApiRoutes.Shipments)).StatusCode);
    }

    [Fact]
    public async Task In_transit_parcel_cannot_be_received_at_the_wrong_manifest_destination()
    {
        var client = factory.CreateUserClient();
        var merchant = await client.CreateMerchantAsync("Wrong Hub Guard Shop", "wrong.hub.guard");
        var booked = await client.BookAsync(ApiTestHelpers.DomesticOrder(merchant, "Wrong Hub Guard Customer"));
        await client.ScanAsync(ScanOperation.Receive, booked.TrackingNumber, IraqHubs.Baghdad, "A-01-1");
        var loaded = await client.ScanAsync(ScanOperation.Load, booked.TrackingNumber, IraqHubs.Baghdad);
        var transfers = (await client.GetFromJsonAsync<TransferListResult>("/" + ShipmentsApiRoutes.Transfers + "?status=Loading&hub=" + IraqHubs.Baghdad))!;
        var transfer = transfers.Items.Single(item => item.Number == loaded.TransferNumber);
        var get = await client.GetAsync("/" + ShipmentsApiRoutes.TransferById(transfer.Id));
        var departed = await client.PostWithETagAsync(ShipmentsApiRoutes.TransferDepart(transfer.Id),
            new DepartTransferRequest { VehiclePlate = "BG-21001", DriverName = "Driver", SealNumber = "V21" }, get.Headers.ETag!);
        Assert.True(departed.IsSuccessStatusCode, await departed.Content.ReadAsStringAsync());

        var wrongHub = await client.TryScanAsync(ScanOperation.Receive, booked.TrackingNumber, IraqHubs.Erbil, "X-01");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongHub.StatusCode);
        using var problem = JsonDocument.Parse(await wrongHub.Content.ReadAsStringAsync());
        Assert.Equal("transfer.wrong_destination", problem.RootElement.GetProperty("code").GetString());
        var shipment = await client.GetShipmentAsync(booked.Id);
        Assert.Equal(ShipmentStatus.InTransitToHub, shipment.Status);
        Assert.Equal(transfer.Id, shipment.TransferId);
        var manifest = (await client.GetFromJsonAsync<TransferDetails>("/" + ShipmentsApiRoutes.TransferById(transfer.Id)))!;
        Assert.Equal(TransferStatus.InTransit, manifest.Summary.Status);
        Assert.Equal(TransferItemState.Loaded, Assert.Single(manifest.Items).State);
    }

    [Fact]
    public async Task Booking_and_manual_updates_reject_inconsistent_locations()
    {
        var client = factory.CreateUserClient();
        var merchant = await client.CreateMerchantAsync("Origin Guard Shop", "origin.guard");
        var inconsistent = ApiTestHelpers.DomesticOrder(merchant, "Origin Guard Customer");
        inconsistent.OriginCode = "IQ-BA";

        var booking = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.Shipments, inconsistent);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, booking.StatusCode);
        Assert.Contains("shipment.merchant_origin_mismatch", await booking.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var valid = await client.BookAsync(ApiTestHelpers.DomesticOrder(merchant, "Hub Guard Customer"));
        var update = await client.RecordAsync(valid.Id,
            new RecordShipmentEventRequest { Status = ShipmentStatus.AtWarehouse, Location = "HUB-NOT-REAL" },
            await client.ETagAsync(valid.Id));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, update.StatusCode);
        Assert.Contains("scan.unknown_hub", await update.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exact_shipment_lookup_preserves_all_requested_label_references()
    {
        var client = factory.CreateUserClient();
        var first = await client.BookAsync(ApiTestHelpers.InternationalBooking("Exact Labels One"));
        var second = await client.BookAsync(ApiTestHelpers.InternationalBooking("Exact Labels Two"));
        var third = await client.BookAsync(ApiTestHelpers.InternationalBooking("Exact Labels Three"));

        var response = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.ExactShipments,
            new ShipmentReferencesRequest { References = [third.Id.ToString(), first.TrackingNumber, second.Id.ToString()] });
        var rows = (await response.Content.ReadFromJsonAsync<List<ShipmentListItem>>())!;

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal([third.Id, first.Id, second.Id], rows.Select(row => row.Id));
    }
}
