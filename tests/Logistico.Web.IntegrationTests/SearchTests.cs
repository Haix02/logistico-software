using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Merchants.Contracts;
using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel;

namespace Logistico.Web.IntegrationTests;

/// <summary>Search finds what staff type: Arabic letter variants, part of a phone, several words across fields.</summary>
[Collection("Api")]
public sealed class SearchTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task Shipments_are_found_however_the_name_phone_or_shop_is_typed()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        var shop = await admin.CreateMerchantAsync("دار الأناقة", "dar.alanaqa.search");
        var order = ApiTestHelpers.DomesticOrder(shop, "أحمد فاطمة");
        order.ConsigneePhone = "0781 555 1234";
        order.ExternalReference = "IG-5521";
        order.DestinationCity = "الكرّادة";
        var booked = await admin.BookAsync(order);
        var other = ApiTestHelpers.DomesticOrder(await admin.CreateMerchantAsync("Other Shop", "other.search.shop"), "زينب علي");
        other.ConsigneePhone = "0770 999 8888";
        await admin.BookAsync(other);

        foreach (var typed in new[] { "احمد", "أحمد", "فاطمه", "الاناقه", "احمد الاناقة", "0781 555", "7815551", "٠٧٨١٥٥٥", "ig-5521", "الكرادة", "احمد البصرة", "احمد basra", booked.TrackingNumber[..12] })
        {
            var found = await SearchAsync(admin, typed);
            Assert.True(found.Items.Any(s => s.Id == booked.Id), $"'{typed}' should find {booked.TrackingNumber}");
            Assert.DoesNotContain(found.Items, s => s.ConsigneeName == "زينب علي");
        }

        Assert.Equal(0, (await SearchAsync(admin, "احمد اربيل")).TotalCount); // every word must appear
        Assert.Equal(0, (await SearchAsync(admin, "50%")).TotalCount); // % is a character, not a wildcard
        Assert.Single((await SearchAsync(admin, "0781 555 1234")).Items); // a whole number is an exact match
    }

    [Fact]
    public async Task Sellers_are_found_by_folded_name_handle_or_phone()
    {
        var admin = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var shop = await admin.CreateMerchantAsync("عطور الفرات", "alfurat.search");

        foreach (var typed in new[] { "عطور الفرات", "الفرات", "@alfurat", "ALFURAT", "0770 111", "Owner" })
        {
            var page = (await admin.GetFromJsonAsync<PagedResult<MerchantListItem>>("/" + MerchantsApiRoutes.Merchants + "?q=" + Uri.EscapeDataString(typed)))!;
            Assert.True(page.Items.Any(m => m.Id == shop), $"'{typed}' should find the shop");
        }
    }

    [Fact]
    public async Task Request_truck_and_customs_searches_fold_in_the_database()
    {
        var admin = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        foreach (var url in new[] { ShipmentsApiRoutes.Requests + "?q=طلب%20أحمد", ShipmentsApiRoutes.Transfers + "?q=TR-2026", ShipmentsApiRoutes.Customs + "?q=أحمد", ShipmentsApiRoutes.Confirmations + "?q=فاطمة" })
        {
            var response = await admin.GetAsync("/" + url);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
    }

    private static async Task<ShipmentListResult> SearchAsync(HttpClient client, string q) =>
        (await client.GetFromJsonAsync<ShipmentListResult>("/" + ShipmentsApiRoutes.Shipments + "?pageSize=50&q=" + Uri.EscapeDataString(q)))!;
}
