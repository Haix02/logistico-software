using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Tests;

public class ShipmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly MerchantSnapshot Shop = new(Guid.CreateVersion7(), "بوتيك لمار", "IQ-BG");

    private static CreateShipmentRequest International() => new()
    {
        ServiceType = ServiceType.International,
        Mode = TransportMode.Air,
        ShipperName = " Shenzhen Mobile Accessories ",
        ConsigneeName = "Baghdad Buyer",
        OriginCode = "cnszx",
        DestinationCode = "IQBGW",
        Pieces = 2,
        GrossWeightKg = 40,
        VolumeCbm = 0.5m,
    };

    private static CreateShipmentRequest Domestic() => new()
    {
        ServiceType = ServiceType.Domestic,
        MerchantId = Shop.Id,
        ConsigneeName = "زينب كاظم",
        ConsigneePhone = "0770 123 4567",
        OriginCode = "IQ-BG",
        DestinationCode = "IQ-BA",
        DestinationCity = "العشار",
        DeliveryAddress = "شارع الكورنيش",
        CodAmount = 45_000,
        CodCurrency = "IQD",
        DeliveryFee = 8_000,
    };

    [Fact]
    public void Booking_international_freight_normalises_input_and_computes_chargeable_weight()
    {
        var shipment = Shipment.Book(Tenants.Default, "LX26000000017", International(), null, "agent@logistico.local", Now, IraqHubs.Default);

        Assert.Equal(ShipmentStatus.Booked, shipment.Status);
        Assert.Equal("CNSZX", shipment.OriginCode);
        Assert.Equal("Shenzhen Mobile Accessories", shipment.ShipperName);
        Assert.Equal(83.333m, shipment.ChargeableWeightKg);
        Assert.Single(shipment.Events);
        Assert.Equal(7, shipment.Id.Version);
    }

    [Fact]
    public void Booking_a_domestic_order_takes_the_seller_normalises_the_phone_and_sets_a_promise()
    {
        var shipment = Shipment.Book(Tenants.Default, "LX26000000017", Domestic(), Shop, "agent", Now, IraqHubs.Default);

        Assert.Equal(ServiceType.Domestic, shipment.ServiceType);
        Assert.Equal(Shop.Id, shipment.MerchantId);
        Assert.Equal("بوتيك لمار", shipment.MerchantName);
        Assert.Equal("بوتيك لمار", shipment.ShipperName);
        Assert.Equal("+9647701234567", shipment.ConsigneePhone);
        Assert.True(shipment.PickupRequired);
        Assert.NotNull(shipment.PromisedAt);
        Assert.True(shipment.PromisedAt > Now.AddDays(2));
    }

    [Fact]
    public void Cash_on_delivery_requires_a_currency()
    {
        var request = International();
        request.CodAmount = 250_000;
        request.CodCurrency = null;

        var ex = Assert.Throws<DomainException>(() => Shipment.Book(Tenants.Default, "LX26000000017", request, null, null, Now, IraqHubs.Default));
        Assert.Equal("shipment.cod_currency", ex.Code);
    }

    [Fact]
    public void Cash_on_delivery_rejects_currency_that_finance_cannot_settle()
    {
        var request = International();
        request.CodAmount = 100;
        request.CodCurrency = "USD";

        var ex = Assert.Throws<DomainException>(() => Shipment.Book(Tenants.Default, "LX26000000017", request, null, null, Now, IraqHubs.Default));
        Assert.Equal("shipment.cod_currency", ex.Code);
    }

    [Fact]
    public void International_origin_and_destination_must_differ_but_domestic_may_stay_in_one_city()
    {
        var request = International();
        request.DestinationCode = "CNSZX";
        Assert.Throws<DomainException>(() => Shipment.Book(Tenants.Default, "LX26000000017", request, null, null, Now, IraqHubs.Default));

        var local = Domestic();
        local.DestinationCode = "IQ-BG";
        Assert.Equal("IQ-BG", Shipment.Book(Tenants.Default, "LX26000000017", local, Shop, null, Now, IraqHubs.Default).DestinationCode);
    }

    [Fact]
    public void Domestic_journey_tracks_shelf_courier_attempts_and_cod_collection()
    {
        var s = Shipment.Book(Tenants.Default, "LX26000000017", Domestic(), Shop, null, Now, IraqHubs.Default);
        var at = Now;
        DateTimeOffset Next() => at = at.AddHours(3);

        s.AdvanceTo(ShipmentStatus.PickedUp, Next(), new MilestoneDetails(CourierName: "مصطفى"), "scan", "driver", at);
        s.AdvanceTo(ShipmentStatus.AtWarehouse, Next(), new MilestoneDetails(WarehouseBin: "a-01-2"), "scan", "clerk", at);
        Assert.Equal("A-01-2", s.WarehouseBin);

        s.MoveToBin("b-02-1", "clerk", Next());
        Assert.Equal("B-02-1", s.WarehouseBin);
        Assert.Equal(ShipmentStatus.AtWarehouse, s.Status);

        s.AdvanceTo(ShipmentStatus.OutForDelivery, Next(), new MilestoneDetails(CourierName: "حيدر"), "scan", "clerk", at);
        Assert.Null(s.WarehouseBin);
        Assert.Equal("حيدر", s.CourierName);
        Assert.Equal(1, s.DeliveryAttempts);

        Assert.Equal("shipment.failure_reason_required",
            Assert.Throws<DomainException>(() => s.AdvanceTo(ShipmentStatus.DeliveryFailed, Next(), new MilestoneDetails(), "user", "courier", at)).Code);
        s.AdvanceTo(ShipmentStatus.DeliveryFailed, Next(), new MilestoneDetails(FailureReason: DeliveryFailureReason.NoAnswer), "user", "courier", at);
        Assert.Equal(DeliveryFailureReason.NoAnswer, s.LastFailureReason);

        s.AdvanceTo(ShipmentStatus.OutForDelivery, Next(), new MilestoneDetails(), "scan", "clerk", at);
        Assert.Equal(2, s.DeliveryAttempts);
        Assert.Equal("حيدر", s.CourierName);

        s.AdvanceTo(ShipmentStatus.Delivered, Next(), new MilestoneDetails(), "user", "courier", at);
        Assert.Equal(at, s.DeliveredAt);
        Assert.Equal(at, s.CodCollectedAt);
        Assert.Equal(8, s.Events.Count); // booked, picked up, shelved, re-shelved, out, failed, out again, delivered
    }

    [Fact]
    public void Moving_to_a_shelf_requires_the_parcel_to_be_in_the_warehouse()
    {
        var s = Shipment.Book(Tenants.Default, "LX26000000017", Domestic(), Shop, null, Now, IraqHubs.Default);

        Assert.Equal("shipment.not_in_warehouse", Assert.Throws<DomainException>(() => s.MoveToBin("A-01-1", null, Now)).Code);
    }

    [Fact]
    public void Invalid_transitions_future_and_out_of_order_milestones_are_rejected()
    {
        var s = Shipment.Book(Tenants.Default, "LX26000000017", International(), null, null, Now, IraqHubs.Default);
        var none = new MilestoneDetails();

        Assert.Equal("shipment.invalid_transition",
            Assert.Throws<DomainException>(() => s.AdvanceTo(ShipmentStatus.Delivered, Now, none, "user", null, Now)).Code);
        Assert.Equal("shipment.event_in_future",
            Assert.Throws<DomainException>(() => s.AdvanceTo(ShipmentStatus.ReceivedAtOrigin, Now.AddHours(1), none, "user", null, Now)).Code);
        Assert.Equal("shipment.event_out_of_order",
            Assert.Throws<DomainException>(() => s.AdvanceTo(ShipmentStatus.ReceivedAtOrigin, Now.AddDays(-1), none, "user", null, Now)).Code);
    }
}
