using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Modules.Shipments.Tests;

public class TrackingProgressTests
{
    [Theory]
    [InlineData(ShipmentStatus.Booked, 0)]
    [InlineData(ShipmentStatus.PickedUp, 1)]
    [InlineData(ShipmentStatus.AtWarehouse, 2)]
    [InlineData(ShipmentStatus.ReadyForDispatch, 2)]
    [InlineData(ShipmentStatus.OutForDelivery, 3)]
    [InlineData(ShipmentStatus.DeliveryFailed, 3)]
    [InlineData(ShipmentStatus.Delivered, 4)]
    public void Domestic_statuses_map_to_customer_stages(ShipmentStatus status, int expected)
    {
        Assert.Equal(expected, TrackingProgress.CurrentIndex(ServiceType.Domestic, status));
    }

    [Theory]
    [InlineData(ShipmentStatus.ReceivedAtOrigin, TrackingStage.AtOrigin)]
    [InlineData(ShipmentStatus.InTransit, TrackingStage.InTransit)]
    [InlineData(ShipmentStatus.CustomsHold, TrackingStage.Customs)]
    [InlineData(ShipmentStatus.AtDestinationWarehouse, TrackingStage.OutForDelivery)]
    [InlineData(ShipmentStatus.Delivered, TrackingStage.Delivered)]
    public void International_statuses_map_to_customer_stages(ShipmentStatus status, TrackingStage expected)
    {
        var stages = TrackingProgress.Stages(ServiceType.International);
        Assert.Equal(expected, stages[TrackingProgress.CurrentIndex(ServiceType.International, status)]);
    }
}
