namespace Logistico.Modules.Shipments.Contracts.Rules;

/// <summary>Maps detailed statuses to the few stages a customer understands (the tracking progress bar).</summary>
public static class TrackingProgress
{
    private static readonly TrackingStage[] DomesticStages =
        [TrackingStage.OrderPlaced, TrackingStage.PickedUp, TrackingStage.AtWarehouse, TrackingStage.OutForDelivery, TrackingStage.Delivered];

    /// <summary>Domestic parcels that cross Iraq between two hubs show a "between cities" stage.</summary>
    private static readonly TrackingStage[] DomesticLineHaulStages =
        [TrackingStage.OrderPlaced, TrackingStage.PickedUp, TrackingStage.AtWarehouse, TrackingStage.InTransit, TrackingStage.OutForDelivery, TrackingStage.Delivered];

    private static readonly TrackingStage[] InternationalStages =
        [TrackingStage.OrderPlaced, TrackingStage.AtOrigin, TrackingStage.InTransit, TrackingStage.Customs, TrackingStage.OutForDelivery, TrackingStage.Delivered];

    public static IReadOnlyList<TrackingStage> Stages(ServiceType service, bool crossesHubs = false) =>
        service == ServiceType.Domestic ? (crossesHubs ? DomesticLineHaulStages : DomesticStages) : InternationalStages;

    public static TrackingStage StageOf(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Draft or ShipmentStatus.Booked or ShipmentStatus.Cancelled => TrackingStage.OrderPlaced,
        ShipmentStatus.PickedUp => TrackingStage.PickedUp,
        ShipmentStatus.ReceivedAtOrigin or ShipmentStatus.Consolidated => TrackingStage.AtOrigin,
        ShipmentStatus.DepartedOrigin or ShipmentStatus.InTransit or ShipmentStatus.InTransitToHub => TrackingStage.InTransit,
        ShipmentStatus.ArrivedPortOfEntry or ShipmentStatus.CustomsPending or ShipmentStatus.CustomsHold or ShipmentStatus.Released => TrackingStage.Customs,
        ShipmentStatus.AtWarehouse or ShipmentStatus.ReadyForDispatch or ShipmentStatus.AtDestinationWarehouse => TrackingStage.AtWarehouse,
        ShipmentStatus.OutForDelivery or ShipmentStatus.DeliveryFailed => TrackingStage.OutForDelivery,
        ShipmentStatus.Delivered or ShipmentStatus.ReturnInitiated or ShipmentStatus.ReturnInTransit or ShipmentStatus.ReturnAtHub or ShipmentStatus.Returned => TrackingStage.Delivered,
        _ => TrackingStage.OrderPlaced,
    };

    /// <summary>
    /// Index of the current stage in <see cref="Stages"/>. A domestic parcel that has reached its destination hub
    /// after a line-haul counts as "between cities" done, so the bar never moves backwards.
    /// </summary>
    public static int CurrentIndex(ServiceType service, ShipmentStatus status, bool crossesHubs = false, bool atDestinationHub = false)
    {
        var stages = Stages(service, crossesHubs);
        var stage = StageOf(status);
        if (service == ServiceType.International && stage is TrackingStage.PickedUp)
        {
            stage = TrackingStage.AtOrigin;
        }

        if (service == ServiceType.International && stage is TrackingStage.AtWarehouse)
        {
            stage = TrackingStage.OutForDelivery;
        }

        if (service == ServiceType.Domestic && crossesHubs && stage is TrackingStage.AtWarehouse && atDestinationHub)
        {
            stage = TrackingStage.InTransit;
        }

        var index = stages.ToList().IndexOf(stage);
        return index < 0 ? 0 : index;
    }

    /// <summary>Parcels whose journey ended without delivery (returned or cancelled) are shown differently.</summary>
    public static bool IsAborted(ShipmentStatus status) =>
        status is ShipmentStatus.Cancelled or ShipmentStatus.ReturnInitiated or ShipmentStatus.ReturnInTransit or ShipmentStatus.ReturnAtHub or ShipmentStatus.Returned;
}
