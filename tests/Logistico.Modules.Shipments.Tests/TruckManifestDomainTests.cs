using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Tests;

/// <summary>A line-haul truck's manifest says exactly what is on the truck, from loading to the last parcel off it.</summary>
public class TruckManifestDomainTests
{
    private static readonly DateTimeOffset Opened = Orders.Monday.AddHours(2);

    private static string Code(Action act) => Assert.Throws<DomainException>(act).Code;

    /// <summary>A Basra order waiting on a Baghdad shelf for its truck.</summary>
    private static Shipment Waiting(string tracking = "LX26000000017")
    {
        var parcel = Orders.Book("IQ-BA", tracking: tracking);
        parcel.ReceiveAtHub(Orders.Baghdad, null, Orders.Clerk, Orders.Monday.AddHours(1));
        return parcel;
    }

    private static HubTransfer Truck(string to = IraqHubs.Basra) =>
        HubTransfer.Open(Tenants.Default, "TR-2026-000001", IraqHubs.Baghdad, to, " BG 1234 ", " سلام ", "clerk", Opened);

    [Theory]
    [InlineData(IraqHubs.Baghdad, IraqHubs.Baghdad)]
    [InlineData(IraqHubs.Baghdad, "hub-bgw")]
    [InlineData("", IraqHubs.Basra)]
    public void A_truck_runs_between_two_different_hubs(string from, string to) =>
        Assert.Equal("transfer.invalid_lane", Code(() => HubTransfer.Open(Tenants.Default, "TR-2026-000001", from, to, null, null, null, Opened)));

    [Fact]
    public void A_new_truck_is_loading_with_its_plate_and_driver()
    {
        var truck = Truck();

        Assert.Equal(TransferStatus.Loading, truck.Status);
        Assert.Equal(("BG 1234", "سلام"), (truck.VehiclePlate, truck.DriverName));
        Assert.Empty(truck.Items);
    }

    [Fact]
    public void Only_parcels_waiting_at_the_origin_for_this_lane_are_planned_and_only_once()
    {
        var notReceived = Orders.Book("IQ-BA", tracking: "LX26000000033");
        Assert.Equal("transfer.wrong_lane", Code(() => Truck().Plan(notReceived, Opened)));
        Assert.Equal("transfer.wrong_lane", Code(() => Truck(IraqHubs.Erbil).Plan(Waiting(), Opened)));

        var truck = Truck();
        var parcel = Waiting();
        var line = truck.Plan(parcel, Opened);
        Assert.Same(line, truck.Plan(parcel, Opened.AddMinutes(5)));

        Assert.Equal(TransferItemState.Planned, Assert.Single(truck.Items).State);
        Assert.Equal(1, truck.ManifestRevision);
        Assert.Equal(truck.Id, parcel.TransferId);
        Assert.False(line.IsReturn);
    }

    [Fact]
    public void Taking_a_parcel_off_before_departure_clears_its_link_and_an_unknown_parcel_changes_nothing()
    {
        var truck = Truck();
        var parcel = Waiting();
        truck.Plan(parcel, Opened);

        truck.Unplan(Waiting("LX26000000041"), Opened.AddMinutes(1));
        Assert.Single(truck.Items);

        truck.Unplan(parcel, Opened.AddMinutes(2));
        Assert.Empty(truck.Items);
        Assert.Null(parcel.TransferId);
        Assert.Equal(2, truck.ManifestRevision);
    }

    [Fact]
    public void Loading_a_parcel_that_was_not_planned_plans_it_and_a_second_scan_keeps_the_first_time()
    {
        var truck = Truck();
        var parcel = Waiting();

        truck.Load(parcel, Opened);
        truck.Load(parcel, Opened.AddMinutes(10));

        var line = Assert.Single(truck.Items);
        Assert.Equal((TransferItemState.Loaded, Opened), (line.State, line.LoadedAt));
    }

    [Fact]
    public void A_truck_does_not_leave_empty()
    {
        var truck = Truck();
        truck.Plan(Waiting(), Opened);

        Assert.Equal("transfer.empty", Code(() => truck.Depart(null, null, null, 7, "clerk", Opened.AddHours(1))));
        Assert.Equal(TransferStatus.Loading, truck.Status);
    }

    [Fact]
    public void A_departing_truck_takes_only_scanned_parcels_and_then_takes_no_changes()
    {
        var truck = Truck();
        var loaded = Waiting();
        var planned = Waiting("LX26000000041");
        truck.Load(loaded, Opened);
        truck.Plan(planned, Opened);
        var leaves = Opened.AddHours(1);

        var leftBehind = truck.Depart(null, " ", "SEAL-7", 7, "dispatcher", leaves);

        Assert.Equal(planned.Id, Assert.Single(leftBehind).ShipmentId);
        Assert.Equal(loaded.Id, Assert.Single(truck.Items).ShipmentId);
        Assert.Equal(TransferStatus.InTransit, truck.Status);
        Assert.Equal((leaves, leaves.AddHours(7), "dispatcher"), (truck.DepartedAt, truck.ExpectedAt, truck.DepartedBy));
        Assert.Equal(("BG 1234", "سلام", "SEAL-7"), (truck.VehiclePlate, truck.DriverName, truck.SealNumber));

        var later = leaves.AddMinutes(30);
        Assert.Equal("transfer.not_loading", Code(() => truck.Plan(Waiting("LX26000000058"), later)));
        Assert.Equal("transfer.not_loading", Code(() => truck.Unplan(loaded, later)));
        Assert.Equal("transfer.not_loading", Code(() => truck.Depart(null, null, null, 7, null, later)));
        Assert.Equal("transfer.not_loading", Code(() => truck.Load(loaded, later)));
    }

    /// <summary>Regression: a load scan on a truck that had left (or arrived) silently turned a received line back into "loaded".</summary>
    [Fact]
    public void A_load_scan_cannot_put_a_received_parcel_back_on_an_arrived_truck()
    {
        var truck = Truck();
        var parcel = Waiting();
        truck.Load(parcel, Opened);
        truck.Depart(null, null, null, 7, "clerk", Opened.AddHours(1));
        truck.Receive(parcel, "basra clerk", Opened.AddHours(8));
        Assert.Equal(TransferStatus.Received, truck.Status);

        Assert.Equal("transfer.not_loading", Code(() => truck.Load(parcel, Opened.AddHours(9))));
        Assert.Equal(TransferItemState.Received, Assert.Single(truck.Items).State);
    }

    [Fact]
    public void Parcels_come_off_a_truck_on_the_road_and_the_manifest_closes_itself_when_all_are_off()
    {
        var truck = Truck();
        var a = Waiting();
        var b = Waiting("LX26000000041");
        Assert.Equal("transfer.not_in_transit", Code(() => truck.Receive(a, "clerk", Opened)));
        truck.Load(a, Opened);
        truck.Load(b, Opened);
        truck.Depart(null, null, null, 7, "clerk", Opened.AddHours(1));
        Assert.Equal("transfer.not_on_manifest", Code(() => truck.Receive(Waiting("LX26000000058"), "clerk", Opened.AddHours(8))));

        var first = Opened.AddHours(8);
        truck.Receive(a, "first clerk", first);
        Assert.Equal(TransferStatus.InTransit, truck.Status);
        Assert.Equal((first, "first clerk"), (truck.ArrivedAt, truck.ReceivedBy));

        truck.Receive(b, "second clerk", first.AddMinutes(5));
        Assert.Equal(TransferStatus.Received, truck.Status);
        Assert.Equal((first, "first clerk"), (truck.ArrivedAt, truck.ReceivedBy));
        Assert.All(truck.Items, i => Assert.Equal(TransferItemState.Received, i.State));
    }

    [Fact]
    public void Closing_flags_what_never_came_off_and_a_parcel_found_later_is_received_after_all()
    {
        var truck = Truck();
        var arrived = Waiting();
        var lost = Waiting("LX26000000041");
        truck.Load(arrived, Opened);
        truck.Load(lost, Opened);
        truck.Depart(null, null, null, 7, "clerk", Opened.AddHours(1));
        truck.Receive(arrived, "clerk", Opened.AddHours(8));

        var missing = truck.Close(" طرد واحد ناقص ", "supervisor", Opened.AddHours(9));

        Assert.Equal(lost.Id, Assert.Single(missing).ShipmentId);
        Assert.Equal((TransferStatus.Received, "طرد واحد ناقص"), (truck.Status, truck.Notes));
        Assert.Equal(Opened.AddHours(8), truck.ArrivedAt);
        Assert.Equal("transfer.not_in_transit", Code(() => truck.Close(null, "supervisor", Opened.AddHours(10))));

        Assert.False(truck.FoundAfterClosing(arrived, Opened.AddHours(11)));
        Assert.True(truck.FoundAfterClosing(lost, Opened.AddHours(11)));
        Assert.Equal(TransferItemState.Received, truck.Items.Single(i => i.ShipmentId == lost.Id).State);
        Assert.False(truck.FoundAfterClosing(lost, Opened.AddHours(12)));
    }

    [Fact]
    public void Lines_whose_parcels_changed_course_are_dropped_before_the_truck_leaves()
    {
        var truck = Truck();
        var returned = Waiting();
        var travelling = Waiting("LX26000000041");
        var vanished = Waiting("LX26000000058");
        truck.Load(returned, Opened);
        truck.Load(travelling, Opened);
        truck.Plan(vanished, Opened);

        // The seller cancelled: still at the seller's hub, it waits there for hand-over instead of travelling.
        returned.StartReturn(ReturnReason.SellerCancelled, null, Orders.Agent, Opened.AddMinutes(10));
        Assert.Equal(ShipmentStatus.ReturnAtHub, returned.Status);

        var stale = truck.DropStale(new Dictionary<Guid, Shipment> { [returned.Id] = returned, [travelling.Id] = travelling }, Opened.AddMinutes(20));

        Assert.Equal(new[] { returned.Id, vanished.Id }.Order(), stale.Select(i => i.ShipmentId).Order());
        Assert.Equal(travelling.Id, Assert.Single(truck.Items).ShipmentId);
        Assert.Null(returned.TransferId);
        Assert.Equal(truck.Id, travelling.TransferId);
    }

    [Fact]
    public void A_return_on_a_truck_is_marked_as_one_on_the_manifest()
    {
        var parcel = Orders.Book("IQ-BA");
        parcel.ReceiveAtHub(Orders.Basra, null, Orders.Clerk, Orders.Monday.AddHours(1));
        parcel.StartReturn(ReturnReason.CustomerRefused, null, Orders.Agent, Orders.Monday.AddHours(2));
        var truck = HubTransfer.Open(Tenants.Default, "TR-2026-000002", IraqHubs.Basra, IraqHubs.Baghdad, null, null, "clerk", Opened);

        Assert.True(truck.Plan(parcel, Opened).IsReturn);
    }

    [Fact]
    public void A_truck_is_late_only_on_the_road_and_only_after_the_grace_hours()
    {
        var truck = Truck();
        Assert.False(truck.IsOverdue(Opened.AddDays(3), 6));
        truck.Load(Waiting(), Opened);
        truck.Depart(null, null, null, 7, "clerk", Opened);
        var expected = truck.ExpectedAt!.Value;

        Assert.False(truck.IsOverdue(expected.AddHours(6), 6));
        Assert.True(truck.IsOverdue(expected.AddHours(6).AddMinutes(1), 6));
    }
}
