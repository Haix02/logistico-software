using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel.Setup;

namespace Logistico.Web.IntegrationTests;

/// <summary>A company outside Iraq adds its own regions and warehouses instead of the Iraqi network.</summary>
[Collection("Api")]
public sealed class OperatingCountryTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task A_company_outside_Iraq_defines_regions_and_routes_through_its_own_warehouse()
    {
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());

        var saved = await client.PutAsJsonAsync("/" + SetupApiRoutes.Company, new CompanyProfile
        {
            Name = "عمان للشحن",
            Phone = "+962790000000",
            Country = "JO",
        });
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());

        var start = (await client.GetFromJsonAsync<WarehouseList>("/" + ShipmentsApiRoutes.Warehouses))!;
        Assert.Equal("JO", start.Country);
        Assert.Empty(start.Items);
        Assert.Empty(start.Places ?? []);

        var area = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.NetworkAreas, new ServiceAreaRequest
        {
            Code = "JO-AM",
            NameEn = "Amman",
            NameAr = "عمّان",
            Latitude = 31.9539,
            Longitude = 35.9106,
        });
        Assert.Equal(HttpStatusCode.Created, area.StatusCode);

        var wrong = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.NetworkAreas, new ServiceAreaRequest
        {
            Code = "IQ-BG",
            NameEn = "Baghdad",
            NameAr = "بغداد",
            Latitude = 33.3,
            Longitude = 44.4,
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);

        var branch = new WarehouseRequest
        {
            RoutingTag = "AMM",
            NameAr = "مستودع عمّان",
            NameEn = "Amman Warehouse",
            Kind = HubKind.SortingHub,
            Governorate = "JO-AM",
            Latitude = 31.9539,
            Longitude = 35.9106,
            Phone = "+962790000001",
            Covers = ["JO-AM"],
        };
        var created = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.Warehouses, branch);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var warehouse = (await created.Content.ReadFromJsonAsync<WarehouseDetails>())!;
        Assert.Equal("HUB-AMM", warehouse.Code);
        Assert.True(warehouse.IsPrimary);

        var order = new CreateShipmentRequest
        {
            ServiceType = ServiceType.Domestic,
            ConsigneeName = "سارة",
            ConsigneePhone = "+962791112233",
            OriginCode = "JO-AM",
            DestinationCode = "JO-AM",
            DeliveryAddress = "الجبيهة",
            ItemsDescription = "كتب",
            CodAmount = 10,
            CodCurrency = "IQD",
        };
        var booked = await client.BookAsync(order);
        Assert.Equal("HUB-AMM", (await client.GetShipmentAsync(booked.Id)).DestinationHubCode);

        var list = (await client.GetFromJsonAsync<WarehouseList>("/" + ShipmentsApiRoutes.Warehouses))!;
        Assert.Empty(list.UncoveredGovernorates);
        Assert.Contains(list.Places!, place => place.Code == "JO-AM");
    }
}
