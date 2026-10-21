namespace Logistico.Modules.Shipments.Contracts.Rules;

/// <summary>
/// The shipment state machines (docs/02-domain-model.md §3): one for international freight, one for domestic
/// deliveries. The server enforces them; the browser uses them to offer only valid next steps.
/// Domestic parcels may cross Iraq on line-haul trucks between hubs (InTransitToHub) and, when they cannot be
/// delivered, travel back to the seller's hub (ReturnInitiated → ReturnInTransit → ReturnAtHub → Returned).
/// </summary>
public static class ShipmentLifecycle
{
    private static readonly Dictionary<ShipmentStatus, ShipmentStatus[]> International = new()
    {
        [ShipmentStatus.Draft] = [ShipmentStatus.Booked, ShipmentStatus.Cancelled],
        [ShipmentStatus.Booked] = [ShipmentStatus.PickedUp, ShipmentStatus.ReceivedAtOrigin, ShipmentStatus.Cancelled],
        [ShipmentStatus.PickedUp] = [ShipmentStatus.ReceivedAtOrigin],
        [ShipmentStatus.ReceivedAtOrigin] = [ShipmentStatus.Consolidated, ShipmentStatus.DepartedOrigin],
        [ShipmentStatus.Consolidated] = [ShipmentStatus.DepartedOrigin],
        [ShipmentStatus.DepartedOrigin] = [ShipmentStatus.InTransit, ShipmentStatus.ArrivedPortOfEntry],
        [ShipmentStatus.InTransit] = [ShipmentStatus.ArrivedPortOfEntry],
        [ShipmentStatus.ArrivedPortOfEntry] = [ShipmentStatus.CustomsPending],
        [ShipmentStatus.CustomsPending] = [ShipmentStatus.CustomsHold, ShipmentStatus.Released],
        [ShipmentStatus.CustomsHold] = [ShipmentStatus.CustomsPending],
        [ShipmentStatus.Released] = [ShipmentStatus.AtDestinationWarehouse, ShipmentStatus.OutForDelivery],
        [ShipmentStatus.AtDestinationWarehouse] = [ShipmentStatus.ReadyForDispatch, ShipmentStatus.OutForDelivery, ShipmentStatus.InTransitToHub, ShipmentStatus.ReturnInitiated],
        [ShipmentStatus.InTransitToHub] = [ShipmentStatus.AtDestinationWarehouse],
        [ShipmentStatus.ReadyForDispatch] = [ShipmentStatus.OutForDelivery, ShipmentStatus.AtDestinationWarehouse, ShipmentStatus.InTransitToHub],
        [ShipmentStatus.OutForDelivery] = [ShipmentStatus.Delivered, ShipmentStatus.DeliveryFailed],
        [ShipmentStatus.DeliveryFailed] = [ShipmentStatus.OutForDelivery, ShipmentStatus.AtDestinationWarehouse, ShipmentStatus.ReturnInitiated],
        [ShipmentStatus.Delivered] = [ShipmentStatus.ReturnInitiated],
        [ShipmentStatus.ReturnInitiated] = [ShipmentStatus.ReturnInTransit, ShipmentStatus.ReturnAtHub, ShipmentStatus.Returned],
        [ShipmentStatus.ReturnInTransit] = [ShipmentStatus.ReturnAtHub, ShipmentStatus.ReturnInitiated],
        [ShipmentStatus.ReturnAtHub] = [ShipmentStatus.Returned, ShipmentStatus.ReturnInTransit],
        [ShipmentStatus.Returned] = [],
        [ShipmentStatus.Cancelled] = [],
    };

    private static readonly Dictionary<ShipmentStatus, ShipmentStatus[]> Domestic = new()
    {
        [ShipmentStatus.Draft] = [ShipmentStatus.Booked, ShipmentStatus.Cancelled],
        [ShipmentStatus.Booked] = [ShipmentStatus.PickedUp, ShipmentStatus.AtWarehouse, ShipmentStatus.Cancelled],
        [ShipmentStatus.PickedUp] = [ShipmentStatus.AtWarehouse, ShipmentStatus.ReturnInitiated],
        [ShipmentStatus.AtWarehouse] = [ShipmentStatus.ReadyForDispatch, ShipmentStatus.OutForDelivery, ShipmentStatus.InTransitToHub, ShipmentStatus.ReturnInitiated],
        [ShipmentStatus.InTransitToHub] = [ShipmentStatus.AtWarehouse],
        [ShipmentStatus.ReadyForDispatch] = [ShipmentStatus.OutForDelivery, ShipmentStatus.AtWarehouse, ShipmentStatus.InTransitToHub, ShipmentStatus.ReturnInitiated],
        [ShipmentStatus.OutForDelivery] = [ShipmentStatus.Delivered, ShipmentStatus.DeliveryFailed],
        [ShipmentStatus.DeliveryFailed] = [ShipmentStatus.OutForDelivery, ShipmentStatus.AtWarehouse, ShipmentStatus.ReturnInitiated],
        [ShipmentStatus.Delivered] = [ShipmentStatus.ReturnInitiated],
        [ShipmentStatus.ReturnInitiated] = [ShipmentStatus.ReturnInTransit, ShipmentStatus.ReturnAtHub, ShipmentStatus.Returned],
        [ShipmentStatus.ReturnInTransit] = [ShipmentStatus.ReturnAtHub, ShipmentStatus.ReturnInitiated],
        [ShipmentStatus.ReturnAtHub] = [ShipmentStatus.Returned, ShipmentStatus.ReturnInTransit],
        [ShipmentStatus.Returned] = [],
        [ShipmentStatus.Cancelled] = [],
    };

    /// <summary>Statuses in which a parcel sits on a hub shelf on its way to the customer.</summary>
    public static readonly ShipmentStatus[] WarehouseStatuses =
        [ShipmentStatus.AtWarehouse, ShipmentStatus.ReadyForDispatch, ShipmentStatus.AtDestinationWarehouse];

    /// <summary>Statuses of a parcel travelling back to the seller.</summary>
    public static readonly ShipmentStatus[] ReturnStatuses =
        [ShipmentStatus.ReturnInitiated, ShipmentStatus.ReturnInTransit, ShipmentStatus.ReturnAtHub];

    /// <summary>Statuses in which a parcel is on a line-haul truck between hubs.</summary>
    public static readonly ShipmentStatus[] LineHaulStatuses = [ShipmentStatus.InTransitToHub, ShipmentStatus.ReturnInTransit];

    /// <summary>Statuses that mean the journey is over.</summary>
    public static readonly ShipmentStatus[] ClosedStatuses = [ShipmentStatus.Delivered, ShipmentStatus.Returned, ShipmentStatus.Cancelled];

    public static IReadOnlyDictionary<ShipmentStatus, ShipmentStatus[]> For(ServiceType service) =>
        service == ServiceType.Domestic ? Domestic : International;

    public static IReadOnlyList<ShipmentStatus> AllowedNext(ServiceType service, ShipmentStatus current) =>
        For(service).TryGetValue(current, out var next) ? next : [];

    public static bool CanTransition(ServiceType service, ShipmentStatus from, ShipmentStatus to) =>
        AllowedNext(service, from).Contains(to);

    public static bool IsTerminal(ServiceType service, ShipmentStatus status) => AllowedNext(service, status).Count == 0;

    public static bool IsInWarehouse(ShipmentStatus status) => WarehouseStatuses.Contains(status);

    public static bool IsReturning(ShipmentStatus status) => ReturnStatuses.Contains(status);

    public static bool IsOnLineHaul(ShipmentStatus status) => LineHaulStatuses.Contains(status);

    /// <summary>The status a warehouse "receive" scan moves a parcel to.</summary>
    public static ShipmentStatus WarehouseStatusFor(ServiceType service) =>
        service == ServiceType.Domestic ? ShipmentStatus.AtWarehouse : ShipmentStatus.AtDestinationWarehouse;

    /// <summary>Statuses that count as open work for operations screens.</summary>
    public static bool IsActive(ShipmentStatus status) => !ClosedStatuses.Contains(status);

    /// <summary>
    /// Steps a person may record by hand on the details page. Steps that belong to a scan or a workflow
    /// (line-haul departure/arrival, return hand-over) are done from the warehouse station, transfers or returns.
    /// </summary>
    public static IReadOnlyList<ShipmentStatus> ManualNext(ServiceType service, ShipmentStatus current) =>
        AllowedNext(service, current)
            .Where(s => s is not (ShipmentStatus.InTransitToHub or ShipmentStatus.ReturnInTransit or ShipmentStatus.ReturnAtHub or ShipmentStatus.Returned))
            .Where(s => !(IsOnLineHaul(current) && IsInWarehouse(s)))
            .ToList();
}
