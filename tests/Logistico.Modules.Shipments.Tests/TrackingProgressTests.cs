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

    [Theory]
    [InlineData(ShipmentStatus.ReturnInitiated)]
    [InlineData(ShipmentStatus.ReturnAtHub)]
    [InlineData(ShipmentStatus.Returned)]
    public void A_return_ends_the_journey_at_the_delivery_step_not_at_delivered(ShipmentStatus status)
    {
        var stages = TrackingProgress.Stages(ServiceType.Domestic, crossesHubs: true);
        Assert.Equal(TrackingStage.OutForDelivery, stages[TrackingProgress.CurrentIndex(ServiceType.Domestic, status, crossesHubs: true)]);
        Assert.True(TrackingProgress.IsAborted(status));
    }

    [Fact]
    public void A_parcel_on_a_truck_is_between_cities_not_back_at_order_placed()
    {
        var stages = TrackingProgress.Stages(ServiceType.Domestic, crossesHubs: true);
        Assert.Equal(TrackingStage.InTransit, stages[TrackingProgress.CurrentIndex(ServiceType.Domestic, ShipmentStatus.InTransitToHub, crossesHubs: true)]);
        Assert.True(new PublicTrackingView("LX", ServiceType.Domestic, TransportMode.Road, ShipmentStatus.InTransitToHub, null, "IQ-BG", "IQ-BA", null, null, null,
            0, null, null, null, [], null, null, null, null, [], 0, [], BetweenHubs: true).CrossesHubs);
    }
}
