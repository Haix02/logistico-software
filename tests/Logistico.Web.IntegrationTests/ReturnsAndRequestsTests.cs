using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Web.IntegrationTests;

/// <summary>Parcels that come back to the seller, and requests from customers on the public tracking page.</summary>
[Collection("Api")]
public sealed class ReturnsAndRequestsTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task Parcel_returns_to_the_seller_automatically_after_the_last_failed_attempt_and_is_handed_over_on_a_manifest()
    {
        // A company of its own, so changing its rules does not affect other tests.
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var shop = await client.CreateMerchantAsync("متجر المرتجعات", "returns.flow.test");

        // Rules: return after 2 failed attempts.
        var policyResponse = await client.GetAsync("/" + ShipmentsApiRoutes.AutomationPolicy);
        var policy = (await policyResponse.Content.ReadFromJsonAsync<AutomationPolicy>())!;
        policy.MaxDeliveryAttempts = 2;
        var saved = await client.PutWithETagAsync(ShipmentsApiRoutes.AutomationPolicy, policy, policyResponse.Headers.ETag!);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());

        // Baghdad seller → Basra customer: to the Basra hub by truck.
        var created = await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون المرتجع"));
        await client.ScanAsync(ScanOperation.Receive, created.TrackingNumber, IraqHubs.Baghdad, "A-01-1");
        await client.TruckAsync(created.TrackingNumber, IraqHubs.Baghdad, IraqHubs.Basra);

        // Attempt 1 fails → rescheduled. Attempt 2 fails → return starts automatically.
        await client.ScanAsync(ScanOperation.Dispatch, created.TrackingNumber, IraqHubs.Basra, courier: "كرار (South-01)");
        var first = await client.ScanAsync(ScanOperation.Fail, created.TrackingNumber, IraqHubs.Basra, reason: DeliveryFailureReason.NoAnswer);
        Assert.Equal(ShipmentStatus.DeliveryFailed, first.Status);
        await client.ScanAsync(ScanOperation.Receive, created.TrackingNumber, IraqHubs.Basra, "A-02-1");
        await client.ScanAsync(ScanOperation.Dispatch, created.TrackingNumber, IraqHubs.Basra, courier: "كرار (South-01)");
        var second = await client.ScanAsync(ScanOperation.Fail, created.TrackingNumber, IraqHubs.Basra, reason: DeliveryFailureReason.Unreachable);
        Assert.Equal(ShipmentStatus.ReturnInitiated, second.Status);

        var returning = await client.GetShipmentAsync(created.Id);
        Assert.Equal(ReturnReason.MaxAttemptsReached, returning.ReturnReason);
        Assert.Null(returning.CodCollectedAt);
        Assert.Equal(IraqHubs.Baghdad, returning.TargetHubCode);

        // The courier brings it back to Basra; it is planned on the return truck to Baghdad and arrives there.
        var back = await client.ScanAsync(ScanOperation.Receive, created.TrackingNumber, IraqHubs.Basra, "R-01");
        Assert.Equal(IraqHubs.Baghdad, back.NextHubCode);
        var truck = await client.TruckAsync(created.TrackingNumber, IraqHubs.Basra, IraqHubs.Baghdad);
        Assert.True(Assert.Single(truck.Items).IsReturn);
        var atSellerHub = await client.GetShipmentAsync(created.Id);
        Assert.Equal(ShipmentStatus.ReturnAtHub, atSellerHub.Status);
        Assert.Contains(atSellerHub.Events, e => e.Status == ShipmentStatus.ReturnInTransit);

        // Returns screen groups it under the seller; hand-over issues a manifest.
        var overview = (await client.GetFromJsonAsync<ReturnsOverview>("/" + ShipmentsApiRoutes.Returns))!;
        var group = Assert.Single(overview.ReadyBySeller);
        Assert.Equal(shop, group.MerchantId);
        Assert.Equal(IraqHubs.Baghdad, group.HubCode);

        var handOver = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.ReturnsHandOver, new HandOverReturnsRequest { ShipmentIds = [created.Id], ReceivedBy = "أبو حسن" });
        Assert.True(handOver.IsSuccessStatusCode, await handOver.Content.ReadAsStringAsync());
        var manifest = (await handOver.Content.ReadFromJsonAsync<ReturnManifest>())!;
        Assert.StartsWith("RM-", manifest.Number, StringComparison.Ordinal);
        Assert.Equal("أبو حسن", manifest.ReceivedBy);
        Assert.Equal(created.TrackingNumber, Assert.Single(manifest.Items).TrackingNumber);

        var returned = await client.GetShipmentAsync(created.Id);
        Assert.Equal(ShipmentStatus.Returned, returned.Status);
        Assert.Equal(manifest.Number, returned.ReturnManifestNumber);

        var reprint = await client.GetFromJsonAsync<ReturnManifest>("/" + ShipmentsApiRoutes.ReturnManifest(manifest.Number));
        Assert.Single(reprint!.Items);

        var log = (await client.GetFromJsonAsync<AutomationLogPage>("/" + ShipmentsApiRoutes.AutomationLog))!;
        Assert.Contains(log.Items, e => e.Rule == AutomationRule.AutoReturn && e.TrackingNumber == created.TrackingNumber);
        Assert.Contains(log.Items, e => e.Rule == AutomationRule.AutoReschedule && e.TrackingNumber == created.TrackingNumber);
    }

    [Fact]
    public async Task Customers_change_their_order_from_the_tracking_page_after_proving_the_phone_number()
    {
        var staff = factory.CreateUserClient();
        var shop = await staff.CreateMerchantAsync("متجر الطلبات", "requests.flow.test");
        var created = await staff.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون الطلبات"));
        var anonymous = factory.CreateClient();
        var route = "/" + ShipmentsApiRoutes.TrackRequests(created.TrackingNumber);
        var day = NextWorkingDay(DateTimeOffset.UtcNow.AddDays(2));

        // Wrong digits: refused without revealing anything.
        var wrong = await anonymous.PostAsJsonAsync(route, new CustomerRequest { Type = RequestType.Reschedule, PreferredDate = day, PhoneLast4 = "0000" });
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);

        // A Friday is not a delivery day.
        var friday = await anonymous.PostAsJsonAsync(route, new CustomerRequest { Type = RequestType.Reschedule, PreferredDate = NextFriday(), PhoneLast4 = "3344" });
        Assert.Equal(HttpStatusCode.BadRequest, friday.StatusCode);

        // A new day is applied automatically.
        var reschedule = await anonymous.PostAsJsonAsync(route, new CustomerRequest { Type = RequestType.Reschedule, PreferredDate = day, PhoneLast4 = "3344" });
        Assert.True(reschedule.IsSuccessStatusCode, await reschedule.Content.ReadAsStringAsync());
        var receipt = (await reschedule.Content.ReadFromJsonAsync<CustomerRequestReceipt>())!;
        Assert.True(receipt.AppliedAutomatically);
        Assert.Equal(RequestStatus.Resolved, receipt.Status);
        Assert.StartsWith("RQ-", receipt.Number, StringComparison.Ordinal);
        var rescheduled = await staff.GetShipmentAsync(created.Id);
        Assert.Equal(day.ToOffset(TimeSpan.FromHours(3)).Date, rescheduled.RescheduledTo!.Value.ToOffset(TimeSpan.FromHours(3)).Date);

        // A move to another governorate waits for staff; a second open request of the same kind is refused.
        var move = await anonymous.PostAsJsonAsync(route, new CustomerRequest { Type = RequestType.ChangeAddress, NewGovernorate = "IQ-NA", NewAddress = "حي الأمير، قرب الجامع", PhoneLast4 = "3344" });
        var moveReceipt = (await move.Content.ReadFromJsonAsync<CustomerRequestReceipt>())!;
        Assert.False(moveReceipt.AppliedAutomatically);
        Assert.Equal(RequestStatus.Open, moveReceipt.Status);
        var again = await anonymous.PostAsJsonAsync(route, new CustomerRequest { Type = RequestType.ChangeAddress, NewAddress = "عنوان آخر", PhoneLast4 = "3344" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);

        var view = (await anonymous.GetFromJsonAsync<PublicTrackingView>("/" + ShipmentsApiRoutes.Track(created.TrackingNumber)))!;
        Assert.Equal(1, view.OpenRequests);

        // Staff apply it: the destination and its hub change.
        var inbox = (await staff.GetFromJsonAsync<ServiceRequestList>("/" + ShipmentsApiRoutes.Requests + $"?shipmentId={created.Id}&status=Open"))!;
        var request = Assert.Single(inbox.Items);
        Assert.Equal(RequestSource.Customer, request.Source);
        var resolved = await staff.PostAsJsonAsync("/" + ShipmentsApiRoutes.RequestResolve(request.Id), new ResolveServiceRequest { Outcome = RequestStatus.Resolved, Apply = true, Note = "اتصلنا بالزبون" });
        Assert.True(resolved.IsSuccessStatusCode, await resolved.Content.ReadAsStringAsync());
        var moved = await staff.GetShipmentAsync(created.Id);
        Assert.Equal("IQ-NA", moved.DestinationCode);
        Assert.Equal(IraqHubs.Najaf, moved.DestinationHubCode);

        // Staff can record requests too (e.g. a complaint taken by phone).
        var complaint = await staff.PostAsJsonAsync("/" + ShipmentsApiRoutes.Requests, new CreateServiceRequest { ShipmentId = created.Id, Type = RequestType.Complaint, Details = "اتصل الزبون مرتين" });
        Assert.Equal(HttpStatusCode.Created, complaint.StatusCode);
    }

    private static DateTimeOffset NextWorkingDay(DateTimeOffset from) =>
        from.ToOffset(TimeSpan.FromHours(3)).DayOfWeek == DayOfWeek.Friday ? from.AddDays(1) : from;

    private static DateTimeOffset NextFriday()
    {
        var day = DateTimeOffset.UtcNow.AddDays(1);
        while (day.ToOffset(TimeSpan.FromHours(3)).DayOfWeek != DayOfWeek.Friday)
        {
            day = day.AddDays(1);
        }

        return day;
    }
}
