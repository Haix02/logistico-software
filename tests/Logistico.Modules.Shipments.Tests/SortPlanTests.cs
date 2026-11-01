using System.ComponentModel.DataAnnotations;
using Logistico.Modules.Shipments.Application;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Tests;

/// <summary>Hub sort plans: which shelf a parcel goes on, overflow when shelves fill up, and natural shelf order.</summary>
public class SortPlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 6, 0, 0, TimeSpan.Zero);
    private static readonly MerchantSnapshot Shop = new(Guid.CreateVersion7(), "بوتيك لمار", "IQ-BG");
    private static readonly MerchantSnapshot OtherShop = new(Guid.CreateVersion7(), "متجر النخلة", "IQ-BG");
    private static readonly HubInfo Baghdad = IraqHubs.Default.FindOrDefault(IraqHubs.Baghdad);
    private static readonly Actor Clerk = Actor.Scan("clerk");
    private static int sequence;

    [Fact]
    public void Shelves_sort_the_way_people_walk_a_hub()
    {
        string?[] shelves = ["A-10", "R-01", null, "B-01-10", "A-2-1", "A-1", "B-01-9", "A-2"];

        var sorted = shelves.Order(ShelfOrder.Instance).ToList();

        Assert.Equal(["A-1", "A-2", "A-2-1", "A-10", "B-01-9", "B-01-10", "R-01", null], sorted);
        Assert.True(ShelfOrder.Instance.Compare("A-007", "A-7") != 0); // still a total order: equal values differ in spelling
    }

    [Fact]
    public void Status_order_follows_the_lifecycle_and_lists_every_status_once()
    {
        Assert.Equal(Enum.GetValues<ShipmentStatus>().Order(), ShipmentLifecycle.Order.Order());
        static int Step(ShipmentStatus s) => Array.IndexOf(ShipmentLifecycle.Order, s);
        Assert.True(Step(ShipmentStatus.Booked) < Step(ShipmentStatus.AtWarehouse));
        Assert.True(Step(ShipmentStatus.AtWarehouse) < Step(ShipmentStatus.InTransitToHub));
        Assert.True(Step(ShipmentStatus.InTransitToHub) < Step(ShipmentStatus.OutForDelivery));
        Assert.True(Step(ShipmentStatus.OutForDelivery) < Step(ShipmentStatus.Delivered));
        Assert.True(Step(ShipmentStatus.Delivered) < Step(ShipmentStatus.Returned));
    }

    [Fact]
    public void The_most_specific_matching_rule_chooses_the_shelf()
    {
        var plan = Plan(
            Rule(ShelfPurpose.Any, ["Z-1"]),
            Rule(ShelfPurpose.Deliver, ["D-1"], governorate: "IQ-BG"),
            Rule(ShelfPurpose.Deliver, ["K-1"], governorate: "IQ-BG", area: "الكرادة"),
            Rule(ShelfPurpose.Any, ["M-1"], merchant: Shop.Id));

        Assert.Equal("M-1", plan.Place(Order("IQ-BG", "الكرادة"))!.Shelf); // the seller's own shelf beats everything
        Assert.Equal("K-1", plan.Place(Order("IQ-BG", "الكراده داخل", OtherShop))!.Shelf); // ة/ه written either way
        Assert.Equal("D-1", plan.Place(Order("IQ-BG", "المنصور", OtherShop))!.Shelf);
        Assert.Equal("Z-1", plan.Place(Order("IQ-BA", "العشار", OtherShop))!.Shelf); // waiting for a truck: only the catch-all
        Assert.Equal(["M-1", "K-1", "D-1", "Z-1"], plan.Rules.Select(r => r.Shelves[0]));
    }

    [Fact]
    public void A_full_shelf_overflows_to_the_next_shelf_then_to_the_next_rule()
    {
        var plan = Plan(
            Rule(ShelfPurpose.Deliver, ["D-1", "D-2"], governorate: "IQ-BG", capacity: 1),
            Rule(ShelfPurpose.Any, ["Z-1"], capacity: 1));

        var shelves = Enumerable.Range(0, 4).Select(_ => Receive(plan, Order("IQ-BG", "المنصور"))).ToList();

        Assert.Equal(["D-1", "D-2", "Z-1", "D-1"], shelves.Select(p => p.Shelf));
        Assert.Equal([false, false, false, true], shelves.Select(p => p.Full)); // every shelf full: the emptiest, and the scan says so
        Assert.Equal(2, plan.Load("D-1"));
    }

    [Fact]
    public void A_parcel_scanned_again_on_a_shelf_the_plan_allows_stays_there()
    {
        var plan = Plan(Rule(ShelfPurpose.Deliver, ["D-1", "D-2"], capacity: 1));
        var parcel = Order("IQ-BG", "المنصور");
        Receive(plan, parcel);

        var again = plan.Place(parcel)!;

        Assert.Equal(("D-1", true, false), (again.Shelf, again.AlreadyThere, again.Full));
    }

    [Fact]
    public void Trucks_and_returns_have_their_own_shelves()
    {
        var plan = Plan(
            Rule(ShelfPurpose.Forward, ["T-NJF"], toHub: IraqHubs.Najaf),
            Rule(ShelfPurpose.Forward, ["T-BSR"], toHub: IraqHubs.Basra),
            Rule(ShelfPurpose.Return, ["R-01"]),
            Rule(ShelfPurpose.Deliver, ["D-1"]));

        var toBasra = Order("IQ-BA", "العشار");
        Assert.Equal((ShelfPurpose.Forward, "T-BSR"), (plan.Place(toBasra)!.Purpose, plan.Place(toBasra)!.Shelf));

        toBasra.ReceiveAtHub(Baghdad, "T-BSR", Clerk, Now);
        toBasra.StartReturn(ReturnReason.SellerCancelled, null, Clerk, Now.AddHours(1));
        var back = plan.Place(toBasra)!;
        Assert.Equal((ShelfPurpose.Return, "R-01"), (back.Purpose, back.Shelf)); // back at the seller's hub: the returns shelf
    }

    [Fact]
    public void No_matching_rule_leaves_the_shelf_to_the_person_scanning()
    {
        Assert.Null(Plan().Place(Order("IQ-BG", "المنصور")));
        Assert.Null(Plan(Rule(ShelfPurpose.Return, ["R-01"])).Place(Order("IQ-BG", "المنصور")));
    }

    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "B-07", "B-08" }, true)]
    [InlineData(new[] { " b-07 " }, true)]
    [InlineData(new[] { "B-07", "b-07" }, false)]
    [InlineData(new[] { "B 07" }, false)]
    [InlineData(new[] { "رف-1" }, false)]
    public void Rules_list_one_to_twelve_distinct_shelves(string[] shelves, bool valid)
    {
        var request = new ShelfRuleRequest { HubCode = IraqHubs.Baghdad, Shelves = [.. shelves] };

        Assert.Equal(valid, Validator.TryValidateObject(request, new ValidationContext(request), [], validateAllProperties: true));
    }

    [Fact]
    public void A_destination_hub_only_applies_to_parcels_waiting_for_a_truck()
    {
        var request = new ShelfRuleRequest { HubCode = IraqHubs.Baghdad, Purpose = ShelfPurpose.Deliver, ToHubCode = IraqHubs.Basra, Shelves = ["B-1"] };
        var problems = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), problems, validateAllProperties: true));
        Assert.Contains(problems, p => p.MemberNames.Contains(nameof(request.ToHubCode)));
    }

    private static HubSortPlan Plan(params ShelfRule[] rules) => new(Baghdad.Code, rules, []);

    private static ShelfRule Rule(
        ShelfPurpose purpose, List<string> shelves, string? governorate = null, string? area = null, string? toHub = null, Guid? merchant = null, int? capacity = null) =>
        ShelfRule.Create(Tenants.Default, new ShelfRuleRequest
        {
            HubCode = Baghdad.Code,
            Purpose = purpose,
            Shelves = shelves,
            Governorate = governorate,
            Area = area,
            ToHubCode = toHub,
            MerchantId = merchant,
            CapacityPerShelf = capacity,
        }, merchant is null ? null : "shop", "test", Now.AddMinutes(Interlocked.Increment(ref sequence)));

    /// <summary>Scans the parcel in at Baghdad the way the station does: the plan chooses, the parcel moves, the plan counts it.</summary>
    private static ShelfPlacement Receive(HubSortPlan plan, Shipment parcel)
    {
        var before = plan.ShelfAtThisHub(parcel);
        var placement = plan.Place(parcel)!;
        parcel.ReceiveAtHub(Baghdad, placement.Shelf, Clerk, Now);
        plan.Moved(before, plan.ShelfAtThisHub(parcel));
        return placement;
    }

    private static Shipment Order(string destination, string area, MerchantSnapshot? shop = null)
    {
        var seller = shop ?? Shop;
        return Shipment.Book(
            Tenants.Default,
            TrackingNumber.Create(26, Interlocked.Increment(ref sequence)),
            new CreateShipmentRequest
            {
                ServiceType = ServiceType.Domestic,
                MerchantId = seller.Id,
                ConsigneeName = "زينب كاظم",
                ConsigneePhone = "0770 123 4567",
                OriginCode = "IQ-BG",
                DestinationCode = destination,
                DestinationCity = area,
                DeliveryAddress = "قرب الجامع",
                CodAmount = 65_000,
                CodCurrency = "IQD",
            },
            seller,
            "seed",
            Now,
            IraqHubs.Default);
    }
}
