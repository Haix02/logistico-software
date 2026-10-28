using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel.Identity;

namespace Logistico.Web.IntegrationTests;

/// <summary>The courier app records proof at each attempt: who took the parcel, a signature, where the courier was.</summary>
[Collection("Api")]
public sealed class ProofOfDeliveryTests(LogisticoApiFactory factory)
{
    private const string Signature = "M10 20 L30 40 L55.5 42 M60 20 L80 90";

    [Fact]
    public async Task Deliveries_and_failed_attempts_keep_their_proof_for_the_office()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        await admin.EnsureCourierAsync("Pod-01", "علي");
        var shop = await admin.CreateMerchantAsync("Proof Shop", "proof.shop");
        var delivered = await DispatchAsync(admin, shop, "0770 111 0001");
        var failed = await DispatchAsync(admin, shop, "0770 111 0002");
        var oldApp = await DispatchAsync(admin, shop, "0770 111 0003");
        var courier = factory.CreateUserClient("ali.pod@logistico.test", company, StaffRoles.Courier, courierCode: "Pod-01");

        var proof = new CourierDeliveryRequest
        {
            ReceivedBy = DeliveryReceiver.Family,
            ReceiverName = "أم حسين",
            Signature = Signature,
            Location = new GeoPoint { Latitude = 33.3128, Longitude = 44.3615, AccuracyMeters = 18 },
        };
        var done = await courier.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierDelivered(delivered.Id), proof);
        Assert.True(done.IsSuccessStatusCode, await done.Content.ReadAsStringAsync());
        var refused = await courier.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierFailed(failed.Id),
            new CourierFailureRequest { Reason = DeliveryFailureReason.NoAnswer, Location = new GeoPoint { Latitude = 33.30, Longitude = 44.40 } });
        Assert.True(refused.IsSuccessStatusCode, await refused.Content.ReadAsStringAsync());

        // An app opened before proof existed still delivers (no body); the attempt is recorded as the customer's, with no place.
        Assert.True((await courier.PostAsync("/" + ShipmentsApiRoutes.CourierDelivered(oldApp.Id), null)).IsSuccessStatusCode);

        var kept = Assert.Single(await ProofsAsync(admin, delivered.Id));
        Assert.Equal((true, DeliveryReceiver.Family, "أم حسين", Signature), (kept.Delivered, kept.ReceivedBy, kept.ReceiverName, kept.Signature));
        Assert.Equal((33.3128, 44.3615, 18d), (kept.Latitude, kept.Longitude, kept.AccuracyMeters));
        Assert.Equal("علي (Pod-01)", kept.CourierLabel);
        Assert.Equal(ShipmentStatus.Delivered, (await admin.GetShipmentAsync(delivered.Id)).Status);

        var attempt = Assert.Single(await ProofsAsync(admin, failed.Id));
        Assert.Equal((false, DeliveryFailureReason.NoAnswer, 33.30), (attempt.Delivered, attempt.FailureReason, attempt.Latitude));
        var plain = Assert.Single(await ProofsAsync(admin, oldApp.Id));
        Assert.Equal((DeliveryReceiver.Customer, (double?)null, (string?)null), (plain.ReceivedBy, plain.Latitude, plain.Signature));

        // Proof is office data: the courier app cannot read it back.
        Assert.Equal(HttpStatusCode.Forbidden, (await courier.GetAsync("/" + ShipmentsApiRoutes.Proofs(delivered.Id))).StatusCode);
    }

    [Fact]
    public async Task Proof_is_checked_before_the_parcel_counts_as_delivered()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient(tenant: company);
        await admin.EnsureCourierAsync("Pod-02", "زيد");
        var parcel = await DispatchAsync(admin, await admin.CreateMerchantAsync("Proof Check Shop", "proof.check.shop"), "0770 111 0009", "زيد (Pod-02)");
        var courier = factory.CreateUserClient("zaid.pod@logistico.test", company, StaffRoles.Courier, courierCode: "Pod-02");

        foreach (var bad in new[]
        {
            new CourierDeliveryRequest { ReceivedBy = DeliveryReceiver.Neighbour }, // whose name?
            new CourierDeliveryRequest { Signature = "M0 0<script>alert(1)</script>" }, // only path data is drawn
            new CourierDeliveryRequest { Location = new GeoPoint { Latitude = 120, Longitude = 44 } },
        })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await courier.PostAsJsonAsync("/" + ShipmentsApiRoutes.CourierDelivered(parcel.Id), bad)).StatusCode);
        }

        Assert.Equal(ShipmentStatus.OutForDelivery, (await admin.GetShipmentAsync(parcel.Id)).Status);
        Assert.Empty(await ProofsAsync(admin, parcel.Id));
    }

    private static async Task<CreateShipmentResponse> DispatchAsync(HttpClient admin, Guid shop, string phone, string courier = "علي (Pod-01)")
    {
        var order = ApiTestHelpers.DomesticOrder(shop, "زبون الإثبات");
        order.DestinationCode = "IQ-BG";
        order.ConsigneePhone = phone;
        var booked = await admin.BookAsync(order);
        await admin.ScanAsync(ScanOperation.Receive, booked.TrackingNumber, IraqHubs.Baghdad, "A-01-1");
        await admin.ScanAsync(ScanOperation.Dispatch, booked.TrackingNumber, IraqHubs.Baghdad, courier: courier);
        return booked;
    }

    private static async Task<List<DeliveryProofItem>> ProofsAsync(HttpClient admin, Guid id) =>
        (await admin.GetFromJsonAsync<List<DeliveryProofItem>>("/" + ShipmentsApiRoutes.Proofs(id)))!;
}
