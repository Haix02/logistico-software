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
        [ShipmentStatus.ReturnAtHub] = [ShipmentStatus.Returned, ShipmentStatus.ReturnInTransit, ShipmentStatus.ReturnInitiated],
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

        // Back to ReturnInitiated when a return waiting at the seller's hub is scanned in at another hub: it needs a truck back.
        [ShipmentStatus.ReturnAtHub] = [ShipmentStatus.Returned, ShipmentStatus.ReturnInTransit, ShipmentStatus.ReturnInitiated],
        [ShipmentStatus.Returned] = [],
        [ShipmentStatus.Cancelled] = [],
    };

    /// <summary>
    /// Statuses only a workflow may set, never a person on the details page: truck departures and arrivals (they keep
    /// the manifest right), the return hand-over (signed manifest and return fee) and customs (the declaration
    /// workbench, which refuses a release without the mandatory e-verification).
    /// </summary>
    public static readonly ShipmentStatus[] WorkflowStatuses =
    [
        ShipmentStatus.InTransitToHub, ShipmentStatus.ReturnInTransit, ShipmentStatus.ReturnAtHub, ShipmentStatus.Returned,
        ShipmentStatus.CustomsPending, ShipmentStatus.CustomsHold, ShipmentStatus.Released,
    ];

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

    /// <summary>
    /// Every status in the order a parcel lives through them; lists sorted by status follow it (booked before on the
    /// way before delivered) instead of the alphabet.
    /// </summary>
    public static readonly ShipmentStatus[] Order =
    [
        ShipmentStatus.Draft, ShipmentStatus.Booked, ShipmentStatus.PickedUp, ShipmentStatus.ReceivedAtOrigin, ShipmentStatus.AtWarehouse,
        ShipmentStatus.Consolidated, ShipmentStatus.DepartedOrigin, ShipmentStatus.InTransit, ShipmentStatus.ArrivedPortOfEntry,
        ShipmentStatus.CustomsPending, ShipmentStatus.CustomsHold, ShipmentStatus.Released, ShipmentStatus.InTransitToHub,
        ShipmentStatus.AtDestinationWarehouse, ShipmentStatus.ReadyForDispatch, ShipmentStatus.OutForDelivery, ShipmentStatus.DeliveryFailed,
        ShipmentStatus.Delivered, ShipmentStatus.ReturnInitiated, ShipmentStatus.ReturnInTransit, ShipmentStatus.ReturnAtHub,
        ShipmentStatus.Returned, ShipmentStatus.Cancelled,
    ];

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
    /// (<see cref="WorkflowStatuses"/>) are done from the warehouse station, transfers, returns or customs. A parcel
    /// comes off a truck by a scan at the hub (it completes its manifest line), and a return is started only once.
    /// The server enforces this list, not only the browser.
    /// </summary>
    public static IReadOnlyList<ShipmentStatus> ManualNext(ServiceType service, ShipmentStatus current) =>
        AllowedNext(service, current)
            .Where(s => !WorkflowStatuses.Contains(s))
            .Where(s => !(IsOnLineHaul(current) && IsInWarehouse(s)))
            .Where(s => !(IsReturning(current) && s == ShipmentStatus.ReturnInitiated))
            .ToList();

    /// <summary>Whether a person may record <paramref name="to"/> by hand from <paramref name="from"/> (see <see cref="ManualNext"/>).</summary>
    public static bool CanRecordByHand(ServiceType service, ShipmentStatus from, ShipmentStatus to) =>
        ManualNext(service, from).Contains(to);
}
