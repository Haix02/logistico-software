using Logistico.Modules.Shipments.Application;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Tests;

/// <summary>A Baghdad shop's parcel for a Basra customer, moving through the hub network.</summary>
public class HubNetworkDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 6, 0, 0, TimeSpan.Zero);
    private static readonly MerchantSnapshot Shop = new(Guid.CreateVersion7(), "بوتيك لمار", "IQ-BG");
    private static readonly HubInfo Baghdad = IraqHubs.Default.FindOrDefault(IraqHubs.Baghdad);
    private static readonly HubInfo Basra = IraqHubs.Default.FindOrDefault(IraqHubs.Basra);
    private static readonly Actor Clerk = Actor.Scan("clerk");

    private static Shipment Order(string destination = "IQ-BA") => Shipment.Book(
        Tenants.Default,
        "LX26000000017",
        new CreateShipmentRequest
        {
            ServiceType = ServiceType.Domestic,
            MerchantId = Shop.Id,
            ConsigneeName = "زينب كاظم",
            ConsigneePhone = "0770 123 4567",
            OriginCode = "IQ-BG",
            DestinationCode = destination,
            DeliveryAddress = "العشار",
            CodAmount = 65_000,
            CodCurrency = "IQD",
        },
        Shop,
        "seed",
        Now, IraqHubs.Default);

    [Fact]
    public void Booking_works_out_the_seller_and_customer_hubs()
    {
        var parcel = Order();

        Assert.Equal(IraqHubs.Baghdad, parcel.OriginHubCode);
        Assert.Equal(IraqHubs.Basra, parcel.DestinationHubCode);
        Assert.Null(parcel.CurrentHubCode);
    }

    [Fact]
    public void A_parcel_at_the_seller_hub_needs_a_truck_and_cannot_go_out_with_a_local_courier()
    {
        var parcel = Order();
        parcel.ReceiveAtHub(Baghdad, "a-01-1", Clerk, Now.AddHours(1));

        Assert.Equal(ShipmentStatus.AtWarehouse, parcel.Status);
        Assert.Equal("A-01-1", parcel.WarehouseBin);
        Assert.True(parcel.NeedsTransfer);
        var error = Assert.Throws<DomainException>(() => parcel.DispatchToCourier(Baghdad, "مصطفى (Baghdad-01)", Clerk, Now.AddHours(2)));
        Assert.Equal("shipment.wrong_hub_for_dispatch", error.Code);
    }

    [Fact]
    public void A_truck_moves_loaded_parcels_and_leaves_unscanned_ones_behind()
    {
        var travelling = Order();
        var forgotten = Order();
        travelling.ReceiveAtHub(Baghdad, "A-01-1", Clerk, Now.AddHours(1));
        forgotten.ReceiveAtHub(Baghdad, "A-01-2", Clerk, Now.AddHours(1));
        var truck = HubTransfer.Open(Tenants.Default, "TR-2026-000001", IraqHubs.Baghdad, IraqHubs.Basra, "BG-1", "سلام", "clerk", Now.AddHours(2));
        truck.Plan(travelling, Now.AddHours(2));
        truck.Plan(forgotten, Now.AddHours(2));
        truck.Load(travelling, Now.AddHours(3));

        var hours = IraqHubs.Default.TransitHours(IraqHubs.Baghdad, IraqHubs.Basra);
        var leftBehind = truck.Depart(null, null, "S-1", hours, "clerk", Now.AddHours(4));
        travelling.DepartOnTransfer(truck.Number, truck.FromHubCode, truck.ToHubCode, hours, Clerk, Now.AddHours(4));

        Assert.Equal(forgotten.Id, Assert.Single(leftBehind).ShipmentId);
        Assert.Equal(ShipmentStatus.InTransitToHub, travelling.Status);
        Assert.Null(travelling.CurrentHubCode);
        Assert.Equal(Now.AddHours(4 + hours), truck.ExpectedAt);

        truck.Receive(travelling, "basra clerk", Now.AddHours(11));
        travelling.ReceiveAtHub(Basra, "B-01-1", Clerk, Now.AddHours(11));
        Assert.Equal(TransferStatus.Received, truck.Status);
        Assert.Equal(IraqHubs.Basra, travelling.CurrentHubCode);
        Assert.False(travelling.NeedsTransfer);
        Assert.Null(travelling.TransferId);
    }

    [Fact]
    public void Closing_a_manifest_flags_parcels_that_never_came_off_the_truck()
    {
        var a = Order();
        var b = Order();
        a.ReceiveAtHub(Baghdad, null, Clerk, Now.AddHours(1));
        b.ReceiveAtHub(Baghdad, null, Clerk, Now.AddHours(1));
        var truck = HubTransfer.Open(Tenants.Default, "TR-2026-000002", IraqHubs.Baghdad, IraqHubs.Basra, null, null, "clerk", Now.AddHours(2));
        truck.Load(a, Now.AddHours(2));
        truck.Load(b, Now.AddHours(2));
        truck.Depart(null, null, null, 7, "clerk", Now.AddHours(3));
        truck.Receive(a, "clerk", Now.AddHours(10));

        var missing = truck.Close(null, "clerk", Now.AddHours(11));

        Assert.Equal(b.Id, Assert.Single(missing).ShipmentId);
        Assert.Equal(TransferItemState.Missing, truck.Items.Single(i => i.ShipmentId == b.Id).State);
    }

    [Fact]
    public void The_last_failed_attempt_sends_the_parcel_back_to_the_seller_without_collecting_cash()
    {
        var parcel = Order("IQ-BG");
        var policy = new AutomationPolicy { MaxDeliveryAttempts = 2 };
        parcel.ReceiveAtHub(Baghdad, null, Clerk, Now.AddHours(1));
        parcel.DispatchToCourier(Baghdad, "مصطفى (Baghdad-01)", Clerk, Now.AddHours(2));
        Assert.Equal(FailureFollowUp.Reschedule, parcel.RecordFailedAttempt(DeliveryFailureReason.NoAnswer, null, null, policy, Clerk, Now.AddHours(3)).FollowUp);
        parcel.ReceiveAtHub(Baghdad, null, Clerk, Now.AddHours(4));
        parcel.DispatchToCourier(Baghdad, "مصطفى (Baghdad-01)", Clerk, Now.AddHours(26));

        var decision = parcel.RecordFailedAttempt(DeliveryFailureReason.Unreachable, null, null, policy, Clerk, Now.AddHours(27));

        Assert.Equal(FailureFollowUp.Return, decision.FollowUp);
        Assert.Equal(ShipmentStatus.ReturnInitiated, parcel.Status);
        Assert.Equal(ReturnReason.MaxAttemptsReached, parcel.ReturnReason);
        Assert.Null(parcel.CodCollectedAt);

        // Back at the seller's hub (same city): ready to hand over, then handed over.
        parcel.ReceiveAtHub(Baghdad, "R-01", Clerk, Now.AddHours(30));
        Assert.Equal(ShipmentStatus.ReturnAtHub, parcel.Status);
        parcel.HandOverToSeller("RM-2026-000001", "أبو حسن", Clerk, Now.AddHours(31));
        Assert.Equal(ShipmentStatus.Returned, parcel.Status);
        Assert.Equal("RM-2026-000001", parcel.ReturnManifestNumber);
    }

    [Fact]
    public void A_return_away_from_the_seller_hub_must_travel_back_first()
    {
        var parcel = Order();
        parcel.ReceiveAtHub(Basra, null, Clerk, Now.AddHours(1));
        parcel.StartReturn(ReturnReason.CustomerReturn, null, new Actor("staff"), Now.AddHours(2));

        Assert.Equal(ShipmentStatus.ReturnInitiated, parcel.Status);
        Assert.Equal(IraqHubs.Baghdad, parcel.TargetHubCode);
        Assert.True(parcel.NeedsTransfer);
        Assert.Throws<DomainException>(() => parcel.HandOverToSeller("RM-1", null, Clerk, Now.AddHours(3)));
    }

    [Fact]
    public void Address_changes_to_another_governorate_move_the_destination_hub()
    {
        var parcel = Order();
        parcel.ChangeAddress("IQ-NA", "حي الأمير", "قرب الجامع", null, IraqHubs.Default, new Actor("customer", "system"), Now.AddHours(1));

        Assert.Equal("IQ-NA", parcel.DestinationCode);
        Assert.Equal(IraqHubs.Najaf, parcel.DestinationHubCode);
    }

    [Fact]
    public void Customer_messages_are_written_in_arabic_with_the_amount_to_pay()
    {
        var parcel = Order();
        parcel.ReceiveAtHub(Basra, null, Clerk, Now.AddHours(1));
        parcel.DispatchToCourier(Basra, "كرار (South-01)", Clerk, Now.AddHours(2));

        var text = CustomerMessages.Compose(parcel, NotificationTemplate.OutForDelivery);

        Assert.Contains("زينب", text, StringComparison.Ordinal);
        Assert.Contains("LX26000000017", text, StringComparison.Ordinal);
        Assert.Contains("65,000 د.ع", text, StringComparison.Ordinal);
        Assert.Contains("كرار", text, StringComparison.Ordinal);
        Assert.DoesNotContain("South-01", text, StringComparison.Ordinal);
    }
}
