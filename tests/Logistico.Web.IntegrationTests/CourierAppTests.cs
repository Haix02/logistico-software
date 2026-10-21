using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel.Identity;

namespace Logistico.Web.IntegrationTests;

/// <summary>The courier app: couriers see and complete only their own parcels, and nothing else of the company.</summary>
[Collection("Api")]
public sealed class CourierAppTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task A_courier_delivers_their_own_parcels_and_sees_their_day()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        await AddCourierAsync(admin, "Run-A", "سجاد");
        await AddCourierAsync(admin, "Run-B", "حسن");
        var merchant = await admin.CreateMerchantAsync("Courier App Shop", "courier.app.shop");
        var mine = await DispatchAsync(admin, merchant, "سجاد (Run-A)", 40_000);
        var theirs = await DispatchAsync(admin, merchant, "حسن (Run-B)", 25_000);
        var refused = await DispatchAsync(admin, merchant, "سجاد (Run-A)", 15_000);

        var courier = factory.CreateUserClient("sajjad@logistico.test", company, StaffRoles.Courier, courierCode: "Run-A");
        var run = (await courier.GetFromJsonAsync<CourierRun>("/" + ShipmentsApiRoutes.CourierRun))!;
        Assert.Equal(("Run-A", "سجاد"), (run.CourierCode, run.CourierName));
        Assert.Equal(2, run.Stops.Count);
        Assert.Contains(run.Stops, s => s.Id == mine.Id);
        Assert.Contains(run.Stops, s => s.Id == refused.Id);
        Assert.Equal(40_000, run.Stops.Single(s => s.Id == mine.Id).CodAmount);

        // Somebody else's parcel cannot be touched, and office data stays closed.
        Assert.Equal(HttpStatusCode.NotFound, (await courier.PostAsync("/" + ShipmentsApiRoutes.CourierDelivered(theirs.Id), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await courier.GetAsync("/" + ShipmentsApiRoutes.Shipments)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await courier.GetAsync("/" + ShipmentsApiRoutes.ById(mine.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await courier.GetAsync("/" + TeamApiRoutes.Team)).StatusCode);

        var delivered = await courier.PostAsync("/" + ShipmentsApiRoutes.CourierDelivered(mine.Id), null);
        Assert.True(delivered.IsSuccessStatusCode, await delivered.Content.ReadAsStringAsync());
        var failed = await courier.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierFailed(refused.Id), new CourierFailureRequest { Reason = DeliveryFailureReason.CustomerRefused });
        Assert.True(failed.IsSuccessStatusCode, await failed.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await courier.PostAsync("/" + ShipmentsApiRoutes.CourierDelivered(mine.Id), null)).StatusCode); // already done

        run = (await courier.GetFromJsonAsync<CourierRun>("/" + ShipmentsApiRoutes.CourierRun))!;
        Assert.Empty(run.Stops);
        Assert.Equal((1, 1, 40_000m), (run.DeliveredToday, run.FailedToday, run.CashCollectedTodayIqd));

        // The office sees the result: delivered with its cash collected, by this courier.
        var shipment = await admin.GetShipmentAsync(mine.Id);
        Assert.Equal(ShipmentStatus.Delivered, shipment.Status);
        Assert.Equal("سجاد (Run-A)", shipment.CourierName);
        Assert.NotEqual(ShipmentStatus.OutForDelivery, (await admin.GetShipmentAsync(refused.Id)).Status);
        Assert.Equal(ShipmentStatus.OutForDelivery, (await admin.GetShipmentAsync(theirs.Id)).Status);
    }

    [Fact]
    public async Task Only_linked_couriers_open_the_courier_app()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        await AddCourierAsync(admin, "Run-C", "علي");

        var unlinked = factory.CreateUserClient("unlinked@logistico.test", company, StaffRoles.Courier);
        var response = await unlinked.GetAsync("/" + ShipmentsApiRoutes.CourierRun);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("courier.not_linked", await CodeAsync(response));

        var unknown = factory.CreateUserClient("unknown@logistico.test", company, StaffRoles.Courier, courierCode: "Nobody-01");
        Assert.Equal("courier.not_linked", await CodeAsync(await unknown.GetAsync("/" + ShipmentsApiRoutes.CourierRun)));

        // Office roles have their own screens; the courier app is for couriers.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/" + ShipmentsApiRoutes.CourierRun)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateUserClient("c@logistico.test", company, StaffRoles.Courier, courierCode: "Run-C").GetAsync("/" + ShipmentsApiRoutes.CourierRun)).StatusCode);
    }

    [Fact]
    public async Task Administrators_link_a_courier_account_to_its_courier()
    {
        var admin = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        static CreateTeamMemberRequest Member(string? code) => new()
        {
            Email = $"courier.{Guid.NewGuid():N}@logistico.test",
            DisplayName = "مندوب",
            Role = StaffRoles.Courier,
            CourierCode = code,
            Password = "Basra-Hub-2026!",
        };

        var missing = await admin.PostAsJsonAsync("/" + TeamApiRoutes.Team, Member(null));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("\"CourierCode\"", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var created = await admin.PostAsJsonAsync("/" + TeamApiRoutes.Team, Member("Run-D"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var member = (await created.Content.ReadFromJsonAsync<TeamMember>())!;
        var listed = Assert.Single((await admin.GetFromJsonAsync<TeamList>("/" + TeamApiRoutes.Team))!.Members, m => m.Id == member.Id);
        Assert.Equal((StaffRoles.Courier, "Run-D"), (listed.Role, listed.CourierCode));

        // Another role drops the link.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/" + TeamApiRoutes.Role(member.Id), new ChangeTeamRoleRequest { Role = StaffRoles.Viewer })).StatusCode);
        listed = Assert.Single((await admin.GetFromJsonAsync<TeamList>("/" + TeamApiRoutes.Team))!.Members, m => m.Id == member.Id);
        Assert.Equal((StaffRoles.Viewer, (string?)null), (listed.Role, listed.CourierCode));
    }

    private static async Task AddCourierAsync(HttpClient admin, string code, string name)
    {
        var response = await admin.PostAsJsonAsync("/" + ShipmentsApiRoutes.FleetCouriers, new CourierRequest { Code = code, Name = name, Phone = "07701234567", Governorate = "IQ-BG" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A Baghdad order received at the Baghdad hub and handed to <paramref name="courier"/>.</summary>
    private static async Task<CreateShipmentResponse> DispatchAsync(HttpClient admin, Guid merchant, string courier, decimal cod)
    {
        var order = ApiTestHelpers.DomesticOrder(merchant, "زبون " + cod);
        order.OriginCode = "IQ-BG";
        order.DestinationCode = "IQ-BG";
        order.CodAmount = cod;
        var booked = await admin.BookAsync(order);
        await admin.ScanAsync(ScanOperation.Receive, booked.TrackingNumber, IraqHubs.Baghdad, "A-01-1");
        await admin.ScanAsync(ScanOperation.Dispatch, booked.TrackingNumber, IraqHubs.Baghdad, courier: courier);
        return booked;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
