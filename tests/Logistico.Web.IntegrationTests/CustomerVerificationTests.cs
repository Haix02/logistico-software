using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;

namespace Logistico.Web.IntegrationTests;

/// <summary>
/// The last four digits of the customer's mobile are the only proof on the tracking page, and a verified request can
/// change the phone or address of a parcel. Wrong guesses are therefore limited per order, not only per client address.
/// Own fixture: the per-address rate limit of the public endpoints is shared by every test of a host.
/// </summary>
public sealed class CustomerVerificationTests(LogisticoApiFactory factory) : IClassFixture<LogisticoApiFactory>
{
    [Fact]
    public async Task Five_wrong_answers_lock_the_orders_self_service_for_a_day_even_for_the_right_digits()
    {
        var staff = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var shop = await staff.CreateMerchantAsync("متجر التحقق", "verify.flow.test");
        var created = await staff.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون التحقق"));
        var anonymous = factory.CreateClient();
        var route = "/" + ShipmentsApiRoutes.TrackRequests(created.TrackingNumber);
        static CustomerRequest NewPhone(string last4) => new() { Type = RequestType.ChangePhone, NewPhone = "0770 999 8877", PhoneLast4 = last4 };

        for (var guess = 0; guess < 5; guess++)
        {
            var wrong = await anonymous.PostAsJsonAsync(route, NewPhone($"{1000 + guess}"));
            Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        }

        // Now the right digits are refused too, on every self-service endpoint of this order, and nothing changed.
        var locked = await anonymous.PostAsJsonAsync(route, NewPhone("3344"));
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Contains("request.verification_locked", await locked.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var confirm = await anonymous.PostAsJsonAsync("/" + ShipmentsApiRoutes.TrackConfirm(created.TrackingNumber), new CustomerConfirmation { PhoneLast4 = "3344" });
        Assert.Equal(HttpStatusCode.TooManyRequests, confirm.StatusCode);
        Assert.Equal("+9647812223344", (await staff.GetShipmentAsync(created.Id)).ConsigneePhone);

        // A day later the customer can use the tracking page again.
        factory.Clock.Advance(TimeSpan.FromHours(25));
        var after = await anonymous.PostAsJsonAsync(route, NewPhone("3344"));
        Assert.True(after.IsSuccessStatusCode, await after.Content.ReadAsStringAsync());
        Assert.Equal("+9647709998877", (await staff.GetShipmentAsync(created.Id)).ConsigneePhone);
    }
}
