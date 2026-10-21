using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Web.IntegrationTests;

[Collection("Api")]
public sealed class ShipmentsApiTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task Anonymous_callers_get_401_not_a_login_redirect()
    {
        var response = await factory.CreateClient(new() { AllowAutoRedirect = false }).GetAsync("/" + ShipmentsApiRoutes.Shipments);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Api_responses_with_operational_data_are_not_cacheable()
    {
        var response = await factory.CreateUserClient().GetAsync("/" + ShipmentsApiRoutes.Shipments);

        Assert.True(response.IsSuccessStatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("no-cache", response.Headers.Pragma.Select(p => p.Name));
    }

    [Fact]
    public async Task Booking_allocates_a_valid_tracking_number_and_computes_chargeable_weight()
    {
        var client = factory.CreateUserClient();
        var created = await client.BookAsync(ApiTestHelpers.InternationalBooking("Booking Test Exports"));

        Assert.True(TrackingNumber.IsValid(created.TrackingNumber));
        var details = await client.GetFromJsonAsync<ShipmentDetails>("/" + ShipmentsApiRoutes.ById(created.Id));
        Assert.Equal(ShipmentStatus.Booked, details!.Status);
        Assert.Equal(ServiceType.International, details.ServiceType);
        Assert.Equal(1500m, details.ChargeableWeightKg); // W/M: 1.5 m³ beats 900 kg
        Assert.Equal("agent@logistico.test", details.CreatedBy);
        Assert.Single(details.Events);
    }

    [Fact]
    public async Task Milestones_require_the_current_etag_and_reject_stale_ones()
    {
        var client = factory.CreateUserClient();
        var created = await client.BookAsync(ApiTestHelpers.InternationalBooking("Concurrency Test Exports"));
        var etag = await client.ETagAsync(created.Id);
        var step = new RecordShipmentEventRequest { Status = ShipmentStatus.ReceivedAtOrigin, Location = "Mersin" };

        Assert.Equal(HttpStatusCode.PreconditionRequired, (await client.RecordAsync(created.Id, step, null)).StatusCode);

        var ok = await client.RecordAsync(created.Id, step, etag);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.NotEqual(etag, ok.Headers.ETag);

        var stale = await client.RecordAsync(created.Id, new RecordShipmentEventRequest { Status = ShipmentStatus.DepartedOrigin }, etag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }

    [Fact]
    public async Task Invalid_transitions_return_422_with_a_stable_problem_code()
    {
        var client = factory.CreateUserClient();
        var created = await client.BookAsync(ApiTestHelpers.InternationalBooking("Transition Test Exports"));

        var response = await client.RecordAsync(created.Id, new RecordShipmentEventRequest { Status = ShipmentStatus.Delivered }, await client.ETagAsync(created.Id));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("shipment.invalid_transition", problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Invalid_bookings_return_400_with_field_errors()
    {
        var request = ApiTestHelpers.InternationalBooking("");
        request.OriginCode = "1";
        request.Pieces = 0;

        var response = await factory.CreateUserClient().PostAsJsonAsync("/" + ShipmentsApiRoutes.Shipments, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("ShipperName", out _));
        Assert.True(errors.TryGetProperty("OriginCode", out _));
        Assert.True(errors.TryGetProperty("Pieces", out _));
    }

    [Fact]
    public async Task List_filters_searches_sorts_pages_and_totals_cod_in_the_database()
    {
        var client = factory.CreateUserClient();
        var merchant = await client.CreateMerchantAsync("List Test Shop", "list.test.shop");
        for (var i = 0; i < 3; i++)
        {
            await client.BookAsync(ApiTestHelpers.DomesticOrder(merchant, $"List Customer {i}"));
        }

        var query = new ShipmentQuery { MerchantId = merchant, Sort = "consignee", Desc = false, PageSize = 2 };
        var page1 = await client.GetFromJsonAsync<ShipmentListResult>("/" + ShipmentsApiRoutes.Shipments + query.ToQueryString());

        Assert.Equal(3, page1!.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal("List Customer 0", page1.Items[0].ConsigneeName);
        Assert.Equal(new MoneyTotal("IQD", 195_000m), Assert.Single(page1.CodTotals));

        query.Page = 2;
        var page2 = await client.GetFromJsonAsync<ShipmentListResult>("/" + ShipmentsApiRoutes.Shipments + query.ToQueryString());
        Assert.Equal("List Customer 2", Assert.Single(page2!.Items).ConsigneeName);

        var byPhone = await client.GetFromJsonAsync<ShipmentListResult>("/" + ShipmentsApiRoutes.Shipments + new ShipmentQuery { Q = "0781 222 3344", MerchantId = merchant }.ToQueryString());
        Assert.Equal(3, byPhone!.TotalCount);

        var byName = await client.GetFromJsonAsync<ShipmentListResult>("/" + ShipmentsApiRoutes.Shipments + new ShipmentQuery { Q = "list customer 1" }.ToQueryString());
        Assert.Equal("List Customer 1", Assert.Single(byName!.Items).ConsigneeName);
    }

    [Theory]
    [InlineData("api/v1/shipments?page=2147483647&pageSize=200")]
    [InlineData("api/v1/requests?page=2147483647")]
    [InlineData("api/v1/automation/log?page=2147483647&pageSize=200")]
    [InlineData("api/v1/merchants?page=2147483647&pageSize=200")]
    public async Task Huge_page_numbers_return_an_empty_page_instead_of_a_server_error(string route)
    {
        var response = await factory.CreateUserClient().GetAsync("/" + route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Tenants_cannot_see_each_others_shipments()
    {
        var created = await factory.CreateUserClient().BookAsync(ApiTestHelpers.InternationalBooking("Tenant Isolation Exports"));
        var otherCompany = factory.CreateUserClient("someone@other.test", LogisticoApiFactory.OtherTenant);

        Assert.Equal(HttpStatusCode.NotFound, (await otherCompany.GetAsync("/" + ShipmentsApiRoutes.ById(created.Id))).StatusCode);
        var list = await otherCompany.GetFromJsonAsync<ShipmentListResult>("/" + ShipmentsApiRoutes.Shipments + "?q=Tenant%20Isolation");
        Assert.Equal(0, list!.TotalCount);
    }

    [Fact]
    public async Task Public_tracking_rejects_numbers_with_a_bad_check_digit()
    {
        var response = await factory.CreateClient().GetAsync("/" + ShipmentsApiRoutes.Track("LX26000000000"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Csv_export_is_excel_friendly_and_contains_the_filtered_rows()
    {
        var client = factory.CreateUserClient();
        var created = await client.BookAsync(ApiTestHelpers.InternationalBooking("Csv Export Exports"));

        var response = await client.GetAsync("/" + ShipmentsApiRoutes.Export + "?q=Csv%20Export");

        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]); // UTF-8 BOM so Excel shows Arabic correctly
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("Tracking number,Service,Status", text, StringComparison.Ordinal);
        Assert.Contains(created.TrackingNumber, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enum_numbers_that_name_no_option_are_refused()
    {
        var client = factory.CreateUserClient();
        var booking = ApiTestHelpers.InternationalBooking("Enum Check Co");
        booking.ServiceType = (ServiceType)99;

        var response = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.Shipments, booking);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(nameof(CreateShipmentRequest.ServiceType), await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
