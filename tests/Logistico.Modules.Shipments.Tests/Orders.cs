using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Tests;

/// <summary>
/// Orders of a Baghdad shop for customers across Iraq, on the standard hub network, and the clock their scans happen on.
/// </summary>
internal static class Orders
{
    /// <summary>Monday 19 October 2026, 09:00 in Baghdad.</summary>
    public static readonly DateTimeOffset Monday = new(2026, 10, 19, 6, 0, 0, TimeSpan.Zero);

    public static readonly MerchantSnapshot Shop = new(Guid.CreateVersion7(), "بوتيك لمار", "IQ-BG");
    public static readonly HubInfo Baghdad = IraqHubs.Default.FindOrDefault(IraqHubs.Baghdad);
    public static readonly HubInfo Basra = IraqHubs.Default.FindOrDefault(IraqHubs.Basra);
    public static readonly HubInfo Najaf = IraqHubs.Default.FindOrDefault(IraqHubs.Najaf);
    public static readonly Actor Clerk = Actor.Scan("clerk");
    public static readonly Actor Agent = new("agent");
    public const string CourierName = "مصطفى (Baghdad-01)";

    public static CreateShipmentRequest Request(string destination = "IQ-BA", decimal? cod = 65_000m, decimal? fee = 8_000m) => new()
    {
        ServiceType = ServiceType.Domestic,
        MerchantId = Shop.Id,
        ConsigneeName = "زينب كاظم",
        ConsigneePhone = "0770 123 4567",
        OriginCode = "IQ-BG",
        DestinationCode = destination,
        DestinationCity = "العشار",
        DeliveryAddress = "شارع الكورنيش",
        CodAmount = cod,
        CodCurrency = "IQD",
        DeliveryFee = fee,
    };

    public static Shipment Book(string destination = "IQ-BA", decimal? cod = 65_000m, decimal? fee = 8_000m, string tracking = "LX26000000017") =>
        Shipment.Book(Tenants.Default, tracking, Request(destination, cod, fee), Shop, "agent", Monday, IraqHubs.Default);

    public static Shipment International() => Shipment.Book(
        Tenants.Default,
        "LX26000000025",
        new CreateShipmentRequest
        {
            ServiceType = ServiceType.International,
            Mode = TransportMode.Air,
            ShipperName = "Shenzhen Mobile Accessories",
            ConsigneeName = "Baghdad Buyer",
            OriginCode = "CNSZX",
            DestinationCode = "IQBGW",
            Pieces = 2,
            GrossWeightKg = 40,
        },
        null,
        "agent",
        Monday,
        IraqHubs.Default);
}

/// <summary>A parcel and the clock its steps happen on: every step is a little later than the one before.</summary>
internal sealed class Trip(Shipment parcel)
{
    public Shipment Parcel { get; } = parcel;

    public DateTimeOffset Now { get; private set; } = Orders.Monday;

    public DateTimeOffset Next(double hours = 1) => Now = Now.AddHours(hours);

    public Trip ReceiveAt(HubInfo hub, string? shelf = null)
    {
        Parcel.ReceiveAtHub(hub, shelf, Orders.Clerk, Next());
        return this;
    }

    public Trip Dispatch(HubInfo hub, string courier = Orders.CourierName)
    {
        Parcel.DispatchToCourier(hub, courier, Orders.Clerk, Next());
        return this;
    }

    public Trip Deliver()
    {
        Parcel.AdvanceTo(ShipmentStatus.Delivered, Next(), new MilestoneDetails(), "scan", "courier", Now);
        return this;
    }

    public FailureDecision Fail(DeliveryFailureReason reason, AutomationPolicy? policy = null) =>
        Parcel.RecordFailedAttempt(reason, null, null, policy ?? new AutomationPolicy(), Orders.Clerk, Next());

    /// <summary>Loads the parcel on a truck at <paramref name="from"/> and drives it to <paramref name="to"/> (not yet unloaded).</summary>
    public HubTransfer Truck(HubInfo from, HubInfo to, string number = "TR-2026-000001")
    {
        var truck = HubTransfer.Open(Tenants.Default, number, from.Code, to.Code, "BG-1234", "سلام", "clerk", Next());
        truck.Load(Parcel, Now);
        var hours = IraqHubs.Default.TransitHours(from.Code, to.Code);
        truck.Depart(null, null, "SEAL-1", hours, "clerk", Next());
        Parcel.DepartOnTransfer(truck.Number, from.Code, to.Code, hours, Orders.Clerk, Now);
        return truck;
    }
}
