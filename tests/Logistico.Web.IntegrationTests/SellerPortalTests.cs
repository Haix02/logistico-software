using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel.Identity;

namespace Logistico.Web.IntegrationTests;

/// <summary>The seller portal: a shop books and follows only its own orders, at the company's prices, and sees its money.</summary>
[Collection("Api")]
public sealed class SellerPortalTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task A_seller_books_and_follows_only_their_own_orders()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var shop = await admin.CreateMerchantAsync("Seller Portal Shop", "seller.portal.shop");
        var other = await admin.CreateMerchantAsync("Other Shop", "other.portal.shop");
        var seller = factory.CreateUserClient("shop@logistico.test", company, StaffRoles.Seller, merchantId: shop);

        // The shop cannot book for another shop, from another city, or at its own price.
        var order = ApiTestHelpers.DomesticOrder(other, "زبون المتجر");
        order.OriginCode = "IQ-NI";
        order.DeliveryFee = 1;
        var created = await seller.PostAsJsonAsync("/" + ShipmentsApiRoutes.SellerOrders, order);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var mine = (await created.Content.ReadFromJsonAsync<CreateShipmentResponse>())!;
        var quote = (await seller.GetFromJsonAsync<PriceQuote>("/" + ShipmentsApiRoutes.SellerQuote + "?destination=IQ-BA&weightKg=1"))!;
        var booked = await admin.GetShipmentAsync(mine.Id);
        Assert.Equal((shop, "IQ-BG", quote.Fee), (booked.MerchantId, booked.OriginCode, booked.DeliveryFee));
        Assert.NotEqual(1, quote.Fee);

        var second = await BookAsync(seller, "زبون ثاني");
        var theirs = await admin.BookAsync(ApiTestHelpers.DomesticOrder(other, "زبون غيري"));

        var list = (await seller.GetFromJsonAsync<SellerOrderList>("/" + ShipmentsApiRoutes.SellerOrders))!;
        Assert.Equal(2, list.Total);
        Assert.DoesNotContain(list.Items, o => o.Id == theirs.Id);
        Assert.Single((await seller.GetFromJsonAsync<SellerOrderList>("/" + ShipmentsApiRoutes.SellerOrders + "?q=" + Uri.EscapeDataString(second.TrackingNumber)))!.Items);

        // Office data stays closed, and another shop's order looks like it does not exist.
        Assert.Equal(HttpStatusCode.Forbidden, (await seller.GetAsync("/" + ShipmentsApiRoutes.Shipments)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await seller.GetAsync("/" + ShipmentsApiRoutes.ById(mine.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await seller.PostAsync("/" + ShipmentsApiRoutes.SellerCancel(theirs.Id), null)).StatusCode);

        // Before pickup the shop cancels on its own; after it, the office handles the return.
        Assert.Equal(HttpStatusCode.NoContent, (await seller.PostAsync("/" + ShipmentsApiRoutes.SellerCancel(mine.Id), null)).StatusCode);
        Assert.Equal(ShipmentStatus.Cancelled, (await admin.GetShipmentAsync(mine.Id)).Status);
        await admin.ScanAsync(ScanOperation.Receive, second.TrackingNumber, IraqHubs.Baghdad, "A-01-1");
        var late = await seller.PostAsync("/" + ShipmentsApiRoutes.SellerCancel(second.Id), null);
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("seller.cannot_cancel", await CodeAsync(late));

        var active = (await seller.GetFromJsonAsync<SellerOrderList>("/" + ShipmentsApiRoutes.SellerOrders + "?view=Active"))!;
        Assert.Equal(second.Id, Assert.Single(active.Items).Id);
    }

    [Fact]
    public async Task A_seller_sees_what_they_are_owed_and_their_payouts()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var shop = await admin.CreateMerchantAsync("Seller Money Shop", "seller.money.shop");
        var seller = factory.CreateUserClient("money@logistico.test", company, StaffRoles.Seller, merchantId: shop);

        var delivered = await BookAsync(seller, "زبون مستلم", 50_000);
        var onTheWay = await BookAsync(seller, "زبون بالطريق", 30_000);
        await admin.ScanAsync(ScanOperation.Receive, delivered.TrackingNumber, IraqHubs.Baghdad, "A-01-1");
        await admin.ScanAsync(ScanOperation.Dispatch, delivered.TrackingNumber, IraqHubs.Baghdad, courier: "حيدر (Baghdad-02)");
        await admin.ScanAsync(ScanOperation.Deliver, delivered.TrackingNumber, IraqHubs.Baghdad, courier: "حيدر (Baghdad-02)");
        var fee = (await admin.GetShipmentAsync(delivered.Id)).DeliveryFee!.Value;

        // Still with the courier: on its way to the shop, not owed yet.
        var early = (await seller.GetFromJsonAsync<SellerHome>("/" + ShipmentsApiRoutes.Seller))!;
        Assert.Equal((0m, 80_000m), (early.OwedIqd, early.OnTheWayIqd));
        await admin.HandInCashAsync(await admin.EnsureCourierAsync());

        var home = (await seller.GetFromJsonAsync<SellerHome>("/" + ShipmentsApiRoutes.Seller))!;
        Assert.Equal(("Seller Money Shop", 1, 1), (home.MerchantName, home.ActiveOrders, home.DeliveredLast30Days));
        Assert.Equal((50_000m - fee, 1), (home.OwedIqd, home.OwedOrders));
        Assert.Equal((30_000m, 1), (home.OnTheWayIqd, home.OnTheWayOrders));
        Assert.Empty(home.Payouts);

        var settled = await admin.PostAsJsonAsync("/" + ShipmentsApiRoutes.FinanceSettlements, new CreateCodSettlementRequest { MerchantId = shop, PayoutMethod = PayoutMethod.ZainCash });
        Assert.Equal(HttpStatusCode.Created, settled.StatusCode);

        home = (await seller.GetFromJsonAsync<SellerHome>("/" + ShipmentsApiRoutes.Seller))!;
        Assert.Equal((0m, 50_000m - fee), (home.OwedIqd, home.PaidIqd));
        var payout = Assert.Single(home.Payouts);
        var paid = await admin.GetShipmentAsync(delivered.Id);
        Assert.Equal(payout.SettlementNumber, paid.CodSettlementNumber); // the order says which payout carried its cash
        Assert.NotNull(paid.CodSettledAt);
        var statement = await seller.GetAsync("/" + ShipmentsApiRoutes.SellerStatement(payout.Id));
        Assert.Equal(HttpStatusCode.OK, statement.StatusCode);
        Assert.Contains(delivered.TrackingNumber, await statement.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True((await seller.GetFromJsonAsync<SellerOrderList>("/" + ShipmentsApiRoutes.SellerOrders + "?view=Delivered"))!.Items.Single().PaidOut);
        Assert.Equal(onTheWay.Id, (await seller.GetFromJsonAsync<SellerOrderList>("/" + ShipmentsApiRoutes.SellerOrders + "?view=Active"))!.Items.Single().Id);

        // Another shop's statement is not there for this one.
        var stranger = factory.CreateUserClient("stranger@logistico.test", company, StaffRoles.Seller, merchantId: await admin.CreateMerchantAsync("Stranger Shop", "stranger.shop"));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync("/" + ShipmentsApiRoutes.SellerStatement(payout.Id))).StatusCode);
    }

    [Fact]
    public async Task Only_linked_sellers_open_the_seller_portal()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var unlinked = await factory.CreateUserClient("nolink@logistico.test", company, StaffRoles.Seller).GetAsync("/" + ShipmentsApiRoutes.Seller);
        Assert.Equal(HttpStatusCode.Forbidden, unlinked.StatusCode);
        Assert.Equal("seller.not_linked", await CodeAsync(unlinked));

        // A shop of another company is not this account's shop.
        var elsewhere = await factory.CreateUserClient(tenant: Guid.CreateVersion7()).CreateMerchantAsync("Elsewhere Shop", "elsewhere.shop");
        Assert.Equal("seller.not_linked", await CodeAsync(await factory.CreateUserClient("x@logistico.test", company, StaffRoles.Seller, merchantId: elsewhere).GetAsync("/" + ShipmentsApiRoutes.Seller)));

        // Office roles have their own screens.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/" + ShipmentsApiRoutes.Seller)).StatusCode);

        // Administrators link a seller account to its shop; another role drops the link.
        var shop = await admin.CreateMerchantAsync("Linked Shop", "linked.shop");
        var request = new CreateTeamMemberRequest { Email = $"seller.{Guid.NewGuid():N}@logistico.test", DisplayName = "متجر", Role = StaffRoles.Seller, Password = "Basra-Hub-2026!" };
        var missing = await admin.PostAsJsonAsync("/" + TeamApiRoutes.Team, request);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("\"MerchantId\"", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        request.MerchantId = shop;
        var created = await admin.PostAsJsonAsync("/" + TeamApiRoutes.Team, request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var member = (await created.Content.ReadFromJsonAsync<TeamMember>())!;
        var listed = Assert.Single((await admin.GetFromJsonAsync<TeamList>("/" + TeamApiRoutes.Team))!.Members, m => m.Id == member.Id);
        Assert.Equal((StaffRoles.Seller, (Guid?)shop), (listed.Role, listed.MerchantId));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/" + TeamApiRoutes.Role(member.Id), new ChangeTeamRoleRequest { Role = StaffRoles.Viewer })).StatusCode);
        listed = Assert.Single((await admin.GetFromJsonAsync<TeamList>("/" + TeamApiRoutes.Team))!.Members, m => m.Id == member.Id);
        Assert.Equal((StaffRoles.Viewer, (Guid?)null), (listed.Role, listed.MerchantId));
    }

    /// <summary>A Baghdad delivery booked by the seller itself.</summary>
    private static async Task<CreateShipmentResponse> BookAsync(HttpClient seller, string customer, decimal cod = 25_000)
    {
        var order = ApiTestHelpers.DomesticOrder(Guid.Empty, customer);
        order.DestinationCode = "IQ-BG";
        order.CodAmount = cod;
        var response = await seller.PostAsJsonAsync("/" + ShipmentsApiRoutes.SellerOrders, order);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreateShipmentResponse>())!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
