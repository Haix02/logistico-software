using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Logistico.Modules.Merchants.Contracts;
using Logistico.SharedKernel;

namespace Logistico.Web.IntegrationTests;

[Collection("Api")]
public sealed class MerchantsApiTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task Sellers_are_created_searched_updated_and_protected_against_duplicates()
    {
        var client = factory.CreateUserClient();
        var id = await client.CreateMerchantAsync("Crud Test Shop", "@Crud.Test.Shop");

        var duplicate = await client.PostAsJsonAsync("/" + MerchantsApiRoutes.Merchants, new MerchantRequest
        {
            Name = "Copycat",
            InstagramHandle = "crud.test.shop",
            ContactName = "X",
            Phone = "07701112233",
            Governorate = "IQ-BG",
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var get = await client.GetAsync("/" + MerchantsApiRoutes.ById(id));
        var details = (await get.Content.ReadFromJsonAsync<MerchantDetails>())!;
        Assert.Equal("crud.test.shop", details.InstagramHandle);
        Assert.Equal("+9647701112233", details.Phone);

        var found = await client.GetFromJsonAsync<PagedResult<MerchantListItem>>("/" + MerchantsApiRoutes.Merchants + "?q=@crud.test");
        Assert.Equal(id, Assert.Single(found!.Items).Id);

        using var update = new HttpRequestMessage(HttpMethod.Put, "/" + MerchantsApiRoutes.ById(id))
        {
            Content = JsonContent.Create(new MerchantRequest { Name = "Crud Test Shop 2", InstagramHandle = "crud.test.shop", ContactName = "Owner", Phone = "07701112233", Governorate = "IQ-BA", IsActive = false }),
        };
        update.Headers.IfMatch.Add(get.Headers.ETag!);
        var updated = await client.SendAsync(update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("IQ-BA", (await updated.Content.ReadFromJsonAsync<MerchantDetails>())!.Governorate);

        var lookup = await client.GetFromJsonAsync<List<MerchantLookupItem>>("/" + MerchantsApiRoutes.Lookup);
        Assert.DoesNotContain(lookup!, m => m.Id == id); // inactive sellers are not offered for new orders
    }

    [Fact]
    public async Task Seller_validation_requires_an_iraqi_mobile_and_a_known_governorate()
    {
        var response = await factory.CreateUserClient().PostAsJsonAsync("/" + MerchantsApiRoutes.Merchants, new MerchantRequest
        {
            Name = "Bad Shop",
            ContactName = "X",
            Phone = "123",
            Governorate = "XX-YY",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"Phone\"", body, StringComparison.Ordinal);
        Assert.Contains("\"Governorate\"", body, StringComparison.Ordinal);
    }
}
