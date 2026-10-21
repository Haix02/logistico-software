using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Modules.Shipments.Tests;

public class ShipmentLifecycleTests
{
    [Theory]
    [InlineData(ServiceType.International, ShipmentStatus.Booked, ShipmentStatus.ReceivedAtOrigin, true)]
    [InlineData(ServiceType.International, ShipmentStatus.CustomsPending, ShipmentStatus.CustomsHold, true)]
    [InlineData(ServiceType.International, ShipmentStatus.CustomsHold, ShipmentStatus.CustomsPending, true)]
    [InlineData(ServiceType.International, ShipmentStatus.Booked, ShipmentStatus.Delivered, false)]
    [InlineData(ServiceType.International, ShipmentStatus.InTransit, ShipmentStatus.Cancelled, false)]
    [InlineData(ServiceType.Domestic, ShipmentStatus.Booked, ShipmentStatus.PickedUp, true)]
    [InlineData(ServiceType.Domestic, ShipmentStatus.Booked, ShipmentStatus.AtWarehouse, true)]
    [InlineData(ServiceType.Domestic, ShipmentStatus.AtWarehouse, ShipmentStatus.OutForDelivery, true)]
    [InlineData(ServiceType.Domestic, ShipmentStatus.DeliveryFailed, ShipmentStatus.AtWarehouse, true)]
    [InlineData(ServiceType.Domestic, ShipmentStatus.DeliveryFailed, ShipmentStatus.OutForDelivery, true)]
    [InlineData(ServiceType.Domestic, ShipmentStatus.Booked, ShipmentStatus.CustomsPending, false)]
    [InlineData(ServiceType.Domestic, ShipmentStatus.PickedUp, ShipmentStatus.ReceivedAtOrigin, false)]
    [InlineData(ServiceType.Domestic, ShipmentStatus.Delivered, ShipmentStatus.OutForDelivery, false)]
    public void Enforces_the_state_machine_per_service(ServiceType service, ShipmentStatus from, ShipmentStatus to, bool allowed)
    {
        Assert.Equal(allowed, ShipmentLifecycle.CanTransition(service, from, to));
    }

    [Theory]
    [InlineData(ServiceType.International)]
    [InlineData(ServiceType.Domestic)]
    public void Every_reachable_status_can_still_end_delivered_cancelled_or_returned(ServiceType service)
    {
        foreach (var start in ShipmentLifecycle.For(service).Keys)
        {
            var seen = new HashSet<ShipmentStatus>();
            var queue = new Queue<ShipmentStatus>([start]);
            while (queue.TryDequeue(out var s))
            {
                if (seen.Add(s))
                {
                    foreach (var next in ShipmentLifecycle.AllowedNext(service, s))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            Assert.True(
                seen.Overlaps([ShipmentStatus.Delivered, ShipmentStatus.Cancelled, ShipmentStatus.Returned]),
                $"{service}: {start} is a dead end");
        }
    }

    [Theory]
    [InlineData(ServiceType.International)]
    [InlineData(ServiceType.Domestic)]
    public void Every_transition_target_is_itself_a_known_state(ServiceType service)
    {
        var table = ShipmentLifecycle.For(service);
        foreach (var target in table.Values.SelectMany(t => t))
        {
            Assert.True(table.ContainsKey(target), $"{service}: {target} has no outgoing rules");
        }
    }

    [Fact]
    public void Closed_statuses_are_terminal()
    {
        Assert.True(ShipmentLifecycle.IsTerminal(ServiceType.Domestic, ShipmentStatus.Returned));
        Assert.True(ShipmentLifecycle.IsTerminal(ServiceType.International, ShipmentStatus.Cancelled));
    }

    [Fact]
    public void Warehouse_receive_status_depends_on_service()
    {
        Assert.Equal(ShipmentStatus.AtWarehouse, ShipmentLifecycle.WarehouseStatusFor(ServiceType.Domestic));
        Assert.Equal(ShipmentStatus.AtDestinationWarehouse, ShipmentLifecycle.WarehouseStatusFor(ServiceType.International));
    }
}
