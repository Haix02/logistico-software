using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel.Identity;
using Logistico.SharedKernel.Mobile;

namespace Logistico.Web.IntegrationTests;

/// <summary>The mobile app: token sign-in with the web's rules, and what each kind of account sees, set by administrators.</summary>
[Collection("Api")]
public sealed class MobileAppTests(LogisticoApiFactory factory)
{
    private const string Password = "Basra-Hub-2026!";

    [Fact]
    public async Task The_app_signs_in_with_tokens_and_each_account_sees_its_own_app()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        await admin.EnsureCourierAsync("App-01", "مرتضى");
        var shop = await admin.CreateMerchantAsync("App Shop", "app.shop");
        var courierEmail = await CreateAsync(admin, StaffRoles.Courier, courierCode: "App-01");
        var sellerEmail = await CreateAsync(admin, StaffRoles.Seller, merchantId: shop);
        var viewerEmail = await CreateAsync(admin, StaffRoles.Viewer);

        // Wrong password: refused with a code, no tokens.
        var wrong = await factory.CreateClient().PostAsJsonAsync("/" + MobileApiRoutes.Token, new { email = courierEmail, password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("auth.invalid", await CodeAsync(wrong));

        var courier = await SignInAsync(courierEmail);
        var config = (await courier.Client.GetFromJsonAsync<MobileAppConfig>("/" + MobileApiRoutes.Config))!;
        Assert.Equal((AppKinds.Courier, "App-01"), (config.Kind, config.Account.CourierCode));
        Assert.Equal(AppModules.Courier, config.Modules);
        Assert.Contains(config.ScannerIntents, i => i.Action == "android.intent.ACTION_DECODE_DATA");

        // The token opens the courier's own API, and only that.
        Assert.Equal(HttpStatusCode.OK, (await courier.Client.GetAsync("/" + ShipmentsApiRoutes.CourierRun)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await courier.Client.GetAsync("/" + ShipmentsApiRoutes.Shipments)).StatusCode);

        var seller = await SignInAsync(sellerEmail);
        var sender = (await seller.Client.GetFromJsonAsync<MobileAppConfig>("/" + MobileApiRoutes.Config))!;
        Assert.Equal((AppKinds.Sender, (Guid?)shop), (sender.Kind, sender.Account.MerchantId));
        Assert.Equal(AppModules.Sender, sender.Modules);

        // A read-only manager gets the figures but not the hub scanning or finance screens.
        var viewer = await SignInAsync(viewerEmail);
        var manager = (await viewer.Client.GetFromJsonAsync<MobileAppConfig>("/" + MobileApiRoutes.Config))!;
        Assert.Equal(AppKinds.Manager, manager.Kind);
        Assert.Equal([AppModules.ManagerDashboard, AppModules.ManagerAlerts, AppModules.ManagerOrders], manager.Modules);

        // A refresh gives new tokens; once an administrator locks the account it no longer does.
        var refreshed = await factory.CreateClient().PostAsJsonAsync("/" + MobileApiRoutes.Refresh, new { refreshToken = courier.Refresh });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var member = (await admin.GetFromJsonAsync<TeamList>("/" + TeamApiRoutes.Team))!.Members.Single(m => m.Email == courierEmail);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/" + TeamApiRoutes.Lock(member.Id), null)).StatusCode);
        var afterLock = await factory.CreateClient().PostAsJsonAsync("/" + MobileApiRoutes.Refresh, new { refreshToken = courier.Refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, afterLock.StatusCode);
        Assert.Equal("auth.expired", await CodeAsync(afterLock));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync("/" + MobileApiRoutes.Token, new { email = courierEmail, password = Password })).StatusCode);
    }

    [Fact]
    public async Task Administrators_decide_what_the_app_shows()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        await admin.EnsureCourierAsync("App-02", "كرار");
        var courier = await SignInAsync(await CreateAsync(admin, StaffRoles.Courier, courierCode: "App-02"));

        var settings = (await admin.GetFromJsonAsync<MobileAppSettingsView>("/" + MobileApiRoutes.Settings))!;
        Assert.Null(settings.Version); // nothing saved yet: the defaults
        var change = settings.Settings;
        change.CourierModules = [AppModules.CourierRun];
        change.RequireSignature = true;
        change.ScannerAction = "com.acme.SCAN";
        change.ScannerExtra = "barcode";
        change.Announcement = "  العيد: لا توصيل يوم الجمعة  ";
        change.AppName = "Basra Express";
        var saved = await admin.PutAsJsonAsync("/" + MobileApiRoutes.Settings, change);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var config = (await courier.Client.GetFromJsonAsync<MobileAppConfig>("/" + MobileApiRoutes.Config))!;
        Assert.Equal([AppModules.CourierRun], config.Modules);
        Assert.Equal((true, "Basra Express", "العيد: لا توصيل يوم الجمعة"), (config.RequireSignature, config.AppName, config.Announcement));
        Assert.Contains(new ScannerIntent("com.acme.SCAN", "barcode"), config.ScannerIntents);

        // The server holds couriers to it: no signature, no delivery.
        var shop = await admin.CreateMerchantAsync("Signature Shop", "signature.shop");
        var order = ApiTestHelpers.DomesticOrder(shop, "زبون التوقيع");
        order.DestinationCode = "IQ-BG";
        var parcel = await admin.BookAsync(order);
        await admin.ScanAsync(ScanOperation.Receive, parcel.TrackingNumber, Modules.Shipments.Contracts.Rules.IraqHubs.Baghdad, "A-01-1");
        await admin.ScanAsync(ScanOperation.Dispatch, parcel.TrackingNumber, Modules.Shipments.Contracts.Rules.IraqHubs.Baghdad, courier: "كرار (App-02)");
        var unsigned = await courier.Client.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierDelivered(parcel.Id), new CourierDeliveryRequest());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unsigned.StatusCode);
        Assert.Equal("courier.signature_required", await CodeAsync(unsigned));
        Assert.True((await courier.Client.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierDelivered(parcel.Id), new CourierDeliveryRequest { Signature = "M1 1 L20 20" })).IsSuccessStatusCode);

        // Saving again needs the version; wrong modules and half a scanner pair are refused; only administrators save.
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await admin.PutAsJsonAsync("/" + MobileApiRoutes.Settings, change)).StatusCode);
        change.CourierModules = ["manager.finance"];
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutWithETagAsync(MobileApiRoutes.Settings, change, saved.Headers.ETag!)).StatusCode);
        change.CourierModules = [AppModules.CourierRun];
        change.ScannerExtra = null;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutWithETagAsync(MobileApiRoutes.Settings, change, saved.Headers.ETag!)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateUserClient(tenant: company, role: StaffRoles.Operations).PutAsJsonAsync("/" + MobileApiRoutes.Settings, change)).StatusCode);
    }

    private async Task<string> CreateAsync(HttpClient admin, string role, string? courierCode = null, Guid? merchantId = null)
    {
        var email = $"{role.ToLowerInvariant()}.{Guid.NewGuid():N}@logistico.test";
        var created = await admin.PostAsJsonAsync("/" + TeamApiRoutes.Team,
            new CreateTeamMemberRequest { Email = email, DisplayName = "عضو " + role, Role = role, Password = Password, CourierCode = courierCode, MerchantId = merchantId });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return email;
    }

    private async Task<(HttpClient Client, string Refresh)> SignInAsync(string email)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/" + MobileApiRoutes.Token, new { email, password = Password });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var tokens = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Bearer", tokens.RootElement.GetProperty("tokenType").GetString());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.RootElement.GetProperty("accessToken").GetString());
        return (client, tokens.RootElement.GetProperty("refreshToken").GetString()!);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
