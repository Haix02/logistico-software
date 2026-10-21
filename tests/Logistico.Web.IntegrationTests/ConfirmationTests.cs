using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Web.IntegrationTests;

/// <summary>
/// Orders wait for the customer's confirmation before pickup: the customer confirms on the tracking page, staff record
/// call results, declined orders are cancelled and, when the company wants it, unconfirmed orders cancel themselves.
/// </summary>
public sealed class ConfirmationTests(LogisticoApiFactory factory) : IClassFixture<LogisticoApiFactory>
{
    [Fact]
    public async Task Orders_wait_for_the_customer_and_are_confirmed_declined_or_cancelled_automatically()
    {
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var shop = await client.CreateMerchantAsync("متجر التأكيد", "confirm.flow.test");

        // This company confirms every order and cancels after a day without an answer.
        var policyResponse = await client.GetAsync("/" + ShipmentsApiRoutes.AutomationPolicy);
        var policy = (await policyResponse.Content.ReadFromJsonAsync<AutomationPolicy>())!;
        Assert.Equal(OrderConfirmationMode.RiskyCustomers, policy.OrderConfirmation); // the default
        // Limits are enforced on the server, not only in the form.
        policy.MaxDeliveryAttempts = 40;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutWithETagAsync(ShipmentsApiRoutes.AutomationPolicy, policy, policyResponse.Headers.ETag!)).StatusCode);
        policy.MaxDeliveryAttempts = 3;

        policy.OrderConfirmation = OrderConfirmationMode.AllOrders;
        policy.AutoCancelUnconfirmedHours = 24;
        Assert.True((await client.PutWithETagAsync(ShipmentsApiRoutes.AutomationPolicy, policy, policyResponse.Headers.ETag!)).IsSuccessStatusCode);

        // Booked: waiting, and the customer gets a WhatsApp message asking to confirm.
        var first = await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون التأكيد"));
        Assert.Equal(ConfirmationStatus.Pending, (await client.GetShipmentAsync(first.Id)).Confirmation);
        var messages = (await client.GetFromJsonAsync<NotificationPage>("/" + ShipmentsApiRoutes.AutomationMessages + "?status=Pending"))!;
        Assert.Contains(messages.Items, m => m.TrackingNumber == first.TrackingNumber && m.Template == NotificationTemplate.ConfirmOrder);

        // The hub cannot take it yet.
        var early = await client.TryScanAsync(ScanOperation.Receive, first.TrackingNumber, IraqHubs.Baghdad);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, early.StatusCode);
        Assert.Contains("shipment.awaiting_confirmation", await early.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var queue = (await client.GetFromJsonAsync<ConfirmationQueue>("/" + ShipmentsApiRoutes.Confirmations))!;
        Assert.Contains(queue.Items, i => i.Id == first.Id && i.Risk == CustomerRisk.New);
        Assert.True((await client.GetFromJsonAsync<OpsCounters>("/" + ShipmentsApiRoutes.Counters))!.PendingConfirmations >= 1);

        // The customer confirms on the tracking page (wrong digits are refused).
        var anonymous = factory.CreateClient();
        var view = (await anonymous.GetFromJsonAsync<PublicTrackingView>("/" + ShipmentsApiRoutes.Track(first.TrackingNumber)))!;
        Assert.True(view.AwaitingConfirmation);
        var wrong = await anonymous.PostAsJsonAsync("/" + ShipmentsApiRoutes.TrackConfirm(first.TrackingNumber), new CustomerConfirmation { PhoneLast4 = "0000" });
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        var confirmed = await anonymous.PostAsJsonAsync("/" + ShipmentsApiRoutes.TrackConfirm(first.TrackingNumber), new CustomerConfirmation { PhoneLast4 = "3344" });
        Assert.True(confirmed.IsSuccessStatusCode, await confirmed.Content.ReadAsStringAsync());
        Assert.Equal(ConfirmationStatus.Confirmed, (await client.GetShipmentAsync(first.Id)).Confirmation);
        await client.ScanAsync(ScanOperation.Receive, first.TrackingNumber, IraqHubs.Baghdad, "A-01-1");

        // Staff call: no answer keeps it waiting and counts the attempt; declined cancels it.
        var second = await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون لا يرد"));
        var noAnswer = await client.PostWithETagAsync(ShipmentsApiRoutes.RecordConfirmation(second.Id), new RecordConfirmationRequest { Outcome = ConfirmationOutcome.NoAnswer }, await client.ETagAsync(second.Id));
        Assert.True(noAnswer.IsSuccessStatusCode, await noAnswer.Content.ReadAsStringAsync());
        var waiting = await client.GetShipmentAsync(second.Id);
        Assert.Equal(1, waiting.ConfirmationAttempts);
        Assert.Equal(ConfirmationStatus.Pending, waiting.Confirmation);
        var declined = await client.PostWithETagAsync(ShipmentsApiRoutes.RecordConfirmation(second.Id), new RecordConfirmationRequest { Outcome = ConfirmationOutcome.Declined, Note = "غيّر رأيه" }, await client.ETagAsync(second.Id));
        Assert.True(declined.IsSuccessStatusCode, await declined.Content.ReadAsStringAsync());
        Assert.Equal(ShipmentStatus.Cancelled, (await client.GetShipmentAsync(second.Id)).Status);

        // Nobody answers the third one: alert after 12 hours, cancelled after 24.
        var third = await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون غائب"));
        factory.Clock.Advance(TimeSpan.FromHours(13));
        await client.RunAutomationAsync();
        var alerts = (await client.GetFromJsonAsync<AlertList>("/" + ShipmentsApiRoutes.AutomationAlerts + "?state=open&pageSize=200"))!;
        Assert.Contains(alerts.Items, a => a.ShipmentId == third.Id && a.Kind == AlertKind.ConfirmationOverdue);
        Assert.DoesNotContain(alerts.Items, a => a.ShipmentId == third.Id && a.Kind == AlertKind.PickupOverdue);

        factory.Clock.Advance(TimeSpan.FromHours(12));
        var sweep = await client.RunAutomationAsync();
        Assert.True(sweep.AutoCancelled >= 1);
        Assert.Equal(ShipmentStatus.Cancelled, (await client.GetShipmentAsync(third.Id)).Status);
        var log = (await client.GetFromJsonAsync<AutomationLogPage>("/" + ShipmentsApiRoutes.AutomationLog + "?pageSize=200"))!;
        Assert.Contains(log.Items, e => e.Rule == AutomationRule.AutoCancel && e.TrackingNumber == third.TrackingNumber);
        Assert.Contains(log.Items, e => e.Rule == AutomationRule.ConfirmationRequested && e.TrackingNumber == first.TrackingNumber);
    }
}
