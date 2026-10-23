using System.Globalization;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Iraq;

namespace Logistico.Modules.Shipments.Domain;

/// <summary>Details captured with a milestone.</summary>
internal sealed record MilestoneDetails(
    string? Location = null,
    string? Notes = null,
    string? WarehouseBin = null,
    string? CourierName = null,
    DeliveryFailureReason? FailureReason = null);

/// <summary>Seller data copied onto an order when it is booked.</summary>
internal sealed record MerchantSnapshot(Guid Id, string Name, string Governorate);

/// <summary>Who did something and how (a person, a scan, the automation engine).</summary>
internal sealed record Actor(string? Name, string Source = "user")
{
    public static Actor System { get; } = new("automation", "system");

    public static Actor Scan(string? name) => new(name, "scan");
}

/// <summary>
/// The customer-facing unit of work: one tracking number. Covers international freight and domestic deliveries
/// (e.g. an Instagram shop's parcel) across Iraq's hub network. Status only changes through <see cref="AdvanceTo"/>,
/// which appends an event; physical position is tracked by <see cref="CurrentHubCode"/>.
/// </summary>
internal sealed class Shipment
{
    private readonly List<ShipmentEvent> _events = [];

    private Shipment()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string TrackingNumber { get; private set; } = "";
    public ServiceType ServiceType { get; private set; }
    public string? HouseBillNumber { get; private set; }
    public TransportMode Mode { get; private set; }
    public ShipmentStatus Status { get; private set; }
    public string? Incoterm { get; private set; }
    public Guid? MerchantId { get; private set; }
    public string? MerchantName { get; private set; }
    public string? ExternalReference { get; private set; }
    public string ShipperName { get; private set; } = "";
    public string ConsigneeName { get; private set; } = "";
    public string? ConsigneePhone { get; private set; }
    public string OriginCode { get; private set; } = "";
    public string DestinationCode { get; private set; } = "";
    public string? DestinationCity { get; private set; }
    public string? DeliveryAddress { get; private set; }
    public string? Landmark { get; private set; }
    public string? ItemsDescription { get; private set; }
    public bool PickupRequired { get; private set; }
    public int Pieces { get; private set; }
    public decimal GrossWeightKg { get; private set; }
    public decimal VolumeCbm { get; private set; }
    public decimal ChargeableWeightKg { get; private set; }
    public decimal? DeclaredValue { get; private set; }
    public string? DeclaredCurrency { get; private set; }
    public decimal? CodAmount { get; private set; }
    public string? CodCurrency { get; private set; }
    public DateTimeOffset? CodCollectedAt { get; private set; }
    public Guid? CodSettlementId { get; private set; }
    public DateTimeOffset? CodSettledAt { get; private set; }

    /// <summary>When the courier handed this parcel's collected cash in at the office (see <see cref="CashHandover"/>).</summary>
    public DateTimeOffset? CodHandedOverAt { get; private set; }
    public Guid? CashHandoverId { get; private set; }
    public decimal? DeliveryFee { get; private set; }
    public string? WarehouseBin { get; private set; }
    public string? CourierName { get; private set; }
    public int DeliveryAttempts { get; private set; }
    public DeliveryFailureReason? LastFailureReason { get; private set; }
    public DateTimeOffset? PromisedAt { get; private set; }
    public DateTimeOffset? EtaAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? CreatedBy { get; private set; }

    /// <summary>Hub serving the seller / Iraqi origin (null for origins abroad).</summary>
    public string? OriginHubCode { get; private set; }

    /// <summary>Hub that delivers to the customer's governorate.</summary>
    public string DestinationHubCode { get; private set; } = IraqHubs.Baghdad;

    /// <summary>Hub where the parcel physically is now (null while with a courier, on a truck, or not yet received).</summary>
    public string? CurrentHubCode { get; private set; }

    /// <summary>Line-haul transfer the parcel is planned on or travelling with.</summary>
    public Guid? TransferId { get; private set; }

    public ReturnReason? ReturnReason { get; private set; }
    public DateTimeOffset? ReturnRequestedAt { get; private set; }
    public DateTimeOffset? ReturnedAt { get; private set; }
    public string? ReturnManifestNumber { get; private set; }

    /// <summary>Next delivery attempt agreed with the customer or set automatically after a failed attempt.</summary>
    public DateTimeOffset? RescheduledTo { get; private set; }

    /// <summary>Whether the customer still has to confirm the order before it is collected from the seller.</summary>
    public ConfirmationStatus Confirmation { get; private set; }
    public DateTimeOffset? ConfirmationRequestedAt { get; private set; }
    public DateTimeOffset? ConfirmationDecidedAt { get; private set; }
    public string? ConfirmationBy { get; private set; }

    /// <summary>Calls or messages that got no answer.</summary>
    public int ConfirmationAttempts { get; private set; }

    /// <summary>The customer's rating of the delivery (1–5 stars) and optional comment.</summary>
    public int? Rating { get; private set; }
    public string? RatingComment { get; private set; }
    public DateTimeOffset? RatedAt { get; private set; }

    /// <summary>Customers can rate a delivery once, within two weeks.</summary>
    public bool CanBeRated(DateTimeOffset now) => Status == ShipmentStatus.Delivered && Rating is null && DeliveredAt is { } at && now - at <= TimeSpan.FromDays(14);

    /// <summary>When the current status began; drives dwell-time and overdue rules.</summary>
    public DateTimeOffset LastStatusAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<ShipmentEvent> Events => _events;

    public bool IsReturning => ShipmentLifecycle.IsReturning(Status);

    /// <summary>Hub where the parcel must end up: the destination hub, or the seller's hub for a return.</summary>
    public string TargetHubCode => IsReturning ? OriginHubCode ?? DestinationHubCode : DestinationHubCode;

    /// <summary>The parcel sits at a hub that is not its target hub, so it needs a line-haul truck.</summary>
    public bool NeedsTransfer =>
        CurrentHubCode is not null && CurrentHubCode != TargetHubCode
        && (ShipmentLifecycle.IsInWarehouse(Status) || Status == ShipmentStatus.ReturnInitiated);

    public static Shipment Book(
        Guid tenantId,
        string trackingNumber,
        CreateShipmentRequest request,
        MerchantSnapshot? merchant,
        string? bookedBy,
        DateTimeOffset now,
        HubNetwork network)
    {
        var service = request.ServiceType ?? ServiceType.International;
        var mode = request.Mode ?? (service == ServiceType.Domestic ? TransportMode.Road : throw Rule("shipment.mode_required", "Transport mode is required."));

        var codAmount = request.CodAmount is > 0 ? request.CodAmount : null;
        if (codAmount is not null && string.IsNullOrWhiteSpace(request.CodCurrency))
        {
            throw Rule("shipment.cod_currency", "Cash-on-delivery amount and currency must be given together.");
        }
        if (codAmount is not null && !string.Equals(request.CodCurrency, "IQD", StringComparison.Ordinal))
        {
            throw Rule("shipment.cod_currency", "Cash-on-delivery is settled in IQD only.");
        }

        if (request.DeclaredValue is > 0 && string.IsNullOrWhiteSpace(request.DeclaredCurrency))
        {
            throw Rule("shipment.declared_currency", "Declared value needs a currency.");
        }

        var origin = request.OriginCode.Trim().ToUpperInvariant();
        var destination = request.DestinationCode.Trim().ToUpperInvariant();
        string? phone;
        if (service == ServiceType.Domestic)
        {
            phone = IraqPhone.Normalize(request.ConsigneePhone);
            if (!IraqGovernorates.IsKnown(destination) || phone is null || string.IsNullOrWhiteSpace(request.DeliveryAddress))
            {
                throw Rule("shipment.domestic_address", "Domestic deliveries need governorate, address and mobile number.");
            }
        }
        else
        {
            phone = Clean(request.ConsigneePhone);
            if (origin == destination)
            {
                throw Rule("shipment.same_origin_destination", "Origin and destination must differ.");
            }
        }

        var promised = request.PromisedAt?.ToUniversalTime()
            ?? (service == ServiceType.Domestic ? DeliveryPromise.Estimate(origin, destination, now) : null);

        var shipment = new Shipment
        {
            Id = Guid.CreateVersion7(now),
            TenantId = tenantId,
            TrackingNumber = trackingNumber,
            ServiceType = service,
            HouseBillNumber = Clean(request.HouseBillNumber),
            Mode = mode,
            Status = ShipmentStatus.Booked,
            Incoterm = Clean(request.Incoterm)?.ToUpperInvariant(),
            MerchantId = merchant?.Id,
            MerchantName = merchant?.Name,
            ExternalReference = Clean(request.ExternalReference),
            ShipperName = Clean(request.ShipperName) ?? merchant?.Name ?? "",
            ConsigneeName = request.ConsigneeName.Trim(),
            ConsigneePhone = phone,
            OriginCode = origin,
            DestinationCode = destination,
            DestinationCity = Clean(request.DestinationCity),
            DeliveryAddress = Clean(request.DeliveryAddress),
            Landmark = Clean(request.Landmark),
            ItemsDescription = Clean(request.ItemsDescription),
            PickupRequired = service == ServiceType.Domestic && request.PickupRequired,
            Pieces = request.Pieces,
            GrossWeightKg = request.GrossWeightKg,
            VolumeCbm = request.VolumeCbm,
            ChargeableWeightKg = ChargeableWeight.Calculate(mode, request.GrossWeightKg, request.VolumeCbm),
            DeclaredValue = request.DeclaredValue,
            DeclaredCurrency = request.DeclaredValue is null ? null : Clean(request.DeclaredCurrency),
            CodAmount = codAmount,
            CodCurrency = codAmount is null ? null : Clean(request.CodCurrency),
            DeliveryFee = request.DeliveryFee is > 0 ? request.DeliveryFee : null,
            PromisedAt = promised,
            EtaAt = promised,
            Notes = Clean(request.Notes),
            OriginHubCode = network.OriginHubFor(origin)?.Code,
            DestinationHubCode = network.ResolveDestinationHub(destination).Code,
            CreatedAt = now,
            UpdatedAt = now,
            LastStatusAt = now,
            CreatedBy = bookedBy,
        };

        shipment._events.Add(ShipmentEvent.Create(shipment, ShipmentStatus.Booked, now, new MilestoneDetails(Location: origin), "user", bookedBy, now));
        return shipment;
    }

    public void AdvanceTo(ShipmentStatus next, DateTimeOffset occurredAt, MilestoneDetails details, string source, string? recordedBy, DateTimeOffset now)
    {
        if (!ShipmentLifecycle.CanTransition(ServiceType, Status, next))
        {
            throw Rule(
                "shipment.invalid_transition",
                $"A shipment that is '{Status}' cannot move to '{next}'.",
                ShipmentTexts.Of(Status),
                ShipmentTexts.Of(next));
        }

        if (occurredAt > now.AddMinutes(5))
        {
            throw Rule("shipment.event_in_future", "A milestone cannot be recorded in the future.");
        }

        // An order waiting for the customer's confirmation stays with the seller.
        if (Confirmation == ConfirmationStatus.Pending && Status is ShipmentStatus.Booked or ShipmentStatus.Draft && next != ShipmentStatus.Cancelled)
        {
            throw Rule("shipment.awaiting_confirmation", "The customer has not confirmed this order yet.");
        }

        // Events are not loaded for scans and sweeps; the start of the current status is the reference then.
        var lastOccurred = _events.Count == 0 ? LastStatusAt : _events.Max(e => e.OccurredAt);
        if (occurredAt < lastOccurred)
        {
            throw Rule("shipment.event_out_of_order", "A milestone cannot be earlier than the previous one.");
        }

        if (next == ShipmentStatus.DeliveryFailed && details.FailureReason is null)
        {
            throw Rule("shipment.failure_reason_required", "A failed delivery needs a reason.");
        }

        var at = occurredAt.ToUniversalTime();
        Status = next;
        UpdatedAt = now;
        LastStatusAt = at;
        switch (next)
        {
            case ShipmentStatus.AtWarehouse or ShipmentStatus.AtDestinationWarehouse or ShipmentStatus.ReadyForDispatch or ShipmentStatus.ReturnAtHub:
                WarehouseBin = Bin(details.WarehouseBin) ?? WarehouseBin;
                break;
            case ShipmentStatus.OutForDelivery:
                WarehouseBin = null;
                CurrentHubCode = null;
                CourierName = Clean(details.CourierName) ?? CourierName;
                RescheduledTo = null;
                DeliveryAttempts++;
                break;
            case ShipmentStatus.DeliveryFailed:
                LastFailureReason = details.FailureReason;
                break;
            case ShipmentStatus.Delivered:
                DeliveredAt = at;
                CodCollectedAt = CodAmount is > 0 ? at : null;
                RescheduledTo = null;
                break;
            case ShipmentStatus.InTransitToHub or ShipmentStatus.ReturnInTransit:
                WarehouseBin = null;
                CurrentHubCode = null;
                break;
            case ShipmentStatus.Returned:
                WarehouseBin = null;
                CurrentHubCode = null;
                ReturnedAt = at;
                break;
            case ShipmentStatus.Cancelled:
                WarehouseBin = null;
                CurrentHubCode = null;
                break;
        }

        _events.Add(ShipmentEvent.Create(this, next, occurredAt, details with { WarehouseBin = Bin(details.WarehouseBin) }, source, recordedBy, now));
    }

    /// <summary>
    /// A receive scan at a hub: puts the parcel on a shelf there. Handles first arrival, arrival after a truck,
    /// a courier bringing back an undelivered parcel, re-shelving, and returns reaching the seller's hub.
    /// </summary>
    public void ReceiveAtHub(HubInfo hub, string? bin, Actor actor, DateTimeOffset now)
    {
        var shelf = Bin(bin);
        var details = new MilestoneDetails(Location: hub.Code, WarehouseBin: shelf);
        var arrivedByTruck = ShipmentLifecycle.IsOnLineHaul(Status);

        if (IsReturning)
        {
            if (hub.Code == TargetHubCode)
            {
                if (Status == ShipmentStatus.ReturnAtHub)
                {
                    Reshelve(hub, shelf, actor, now);
                }
                else
                {
                    AdvanceTo(ShipmentStatus.ReturnAtHub, now, details, actor.Source, actor.Name, now);
                }
            }
            else if (Status == ShipmentStatus.ReturnInTransit)
            {
                AdvanceTo(ShipmentStatus.ReturnInitiated, now, details with { Notes = "Return arrived at an intermediate hub" }, actor.Source, actor.Name, now);
            }
            else
            {
                Reshelve(hub, shelf, actor, now);
            }
        }
        else if (ShipmentLifecycle.IsInWarehouse(Status))
        {
            Reshelve(hub, shelf, actor, now);
        }
        else if (Status == ShipmentStatus.OutForDelivery)
        {
            throw Rule("shipment.with_courier", "This parcel is out for delivery. Record the delivery result first.");
        }
        else
        {
            AdvanceTo(ShipmentLifecycle.WarehouseStatusFor(ServiceType), now, details, actor.Source, actor.Name, now);
        }

        CurrentHubCode = hub.Code;
        WarehouseBin = shelf ?? WarehouseBin;
        if (arrivedByTruck)
        {
            TransferId = null;
        }
    }

    /// <summary>Hands the parcel to a courier at its destination hub.</summary>
    public void DispatchToCourier(HubInfo hub, string? courierName, Actor actor, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(courierName))
        {
            throw Rule("shipment.courier_required", "Choose the courier taking the parcel.");
        }

        // Couriers deliver from the hub that serves the customer's governorate.
        if ((CurrentHubCode ?? hub.Code) != DestinationHubCode)
        {
            throw Rule("shipment.wrong_hub_for_dispatch", "This parcel must first travel to its destination hub.", IraqHubs.TagOf(DestinationHubCode));
        }

        AdvanceTo(ShipmentStatus.OutForDelivery, now, new MilestoneDetails(Location: hub.Code, CourierName: courierName), actor.Source, actor.Name, now);
    }

    /// <summary>Records a failed attempt and applies the company's automation policy (return or reschedule).</summary>
    public FailureDecision RecordFailedAttempt(DeliveryFailureReason reason, string? courierName, string? notes, AutomationPolicy policy, Actor actor, DateTimeOffset now)
    {
        AdvanceTo(ShipmentStatus.DeliveryFailed, now, new MilestoneDetails(Notes: notes, CourierName: courierName ?? CourierName, FailureReason: reason), actor.Source, actor.Name, now);

        var decision = DeliveryRules.AfterFailure(DeliveryAttempts, reason, policy, now);
        switch (decision.FollowUp)
        {
            case FailureFollowUp.Return:
                StartReturn(decision.ReturnReason!.Value, null, Actor.System, now);
                break;
            case FailureFollowUp.Reschedule:
                RescheduledTo = decision.NextAttempt;
                EtaAt = decision.NextAttempt;
                break;
        }

        return decision;
    }

    /// <summary>Starts sending the parcel back to the seller. COD is not collected for returned parcels.</summary>
    public void StartReturn(ReturnReason reason, string? notes, Actor actor, DateTimeOffset now)
    {
        if (IsReturning || Status is ShipmentStatus.Returned or ShipmentStatus.Cancelled)
        {
            throw Rule("shipment.already_returning", "This parcel is already being returned.");
        }

        if (Status == ShipmentStatus.OutForDelivery)
        {
            throw Rule("shipment.with_courier", "This parcel is out for delivery. Record the delivery result first.");
        }

        AdvanceTo(ShipmentStatus.ReturnInitiated, now, new MilestoneDetails(Location: CurrentHubCode, Notes: notes ?? ShipmentTexts.Of(reason)), actor.Source, actor.Name, now);
        ReturnReason = reason;
        ReturnRequestedAt = now;
        RescheduledTo = null;
        // A loading truck it was planned on keeps it until TransferPlanner.ReconcileAsync moves it to the return lane.

        // Already sitting at the seller's hub: ready to hand back straight away.
        if (CurrentHubCode is not null && CurrentHubCode == TargetHubCode)
        {
            AdvanceTo(ShipmentStatus.ReturnAtHub, now, new MilestoneDetails(Location: CurrentHubCode, WarehouseBin: WarehouseBin), actor.Source, actor.Name, now);
        }
    }

    /// <summary>The seller (or their representative) collects the returned parcel at the hub.</summary>
    public void HandOverToSeller(string manifestNumber, string? receivedBy, Actor actor, DateTimeOffset now)
    {
        if (Status != ShipmentStatus.ReturnAtHub)
        {
            throw Rule("shipment.return_not_at_hub", "Only returns waiting at the hub can be handed over.");
        }

        var location = CurrentHubCode;
        AdvanceTo(ShipmentStatus.Returned, now, new MilestoneDetails(Location: location, Notes: receivedBy is null ? manifestNumber : $"{manifestNumber} · {receivedBy}"), actor.Source, actor.Name, now);
        ReturnManifestNumber = manifestNumber;
    }

    /// <summary>
    /// The hub this parcel was heading for (or returning to) was retired: route it to the hub that now covers the
    /// same governorate. Returns whether anything changed.
    /// </summary>
    public bool RerouteAround(string retiredHubCode, HubNetwork network, DateTimeOffset now)
    {
        var changed = false;
        if (string.Equals(DestinationHubCode, retiredHubCode, StringComparison.OrdinalIgnoreCase))
        {
            DestinationHubCode = network.ResolveDestinationHub(DestinationCode).Code;
            changed = true;
        }

        if (string.Equals(OriginHubCode, retiredHubCode, StringComparison.OrdinalIgnoreCase))
        {
            OriginHubCode = network.OriginHubFor(OriginCode)?.Code ?? network.Primary.Code;
            changed = true;
        }

        if (changed)
        {
            UpdatedAt = now;
        }

        return changed;
    }

    /// <summary>Loaded onto a line-haul truck (planned or confirmed by scan); no status change until it departs.</summary>
    public void AssignToTransfer(Guid transferId, DateTimeOffset now)
    {
        TransferId = transferId;
        UpdatedAt = now;
    }

    /// <summary>Taken off truck <paramref name="transferId"/> (a link to another truck is left alone).</summary>
    public void RemoveFromTransfer(Guid transferId, DateTimeOffset now)
    {
        if (TransferId == transferId)
        {
            TransferId = null;
            UpdatedAt = now;
        }
    }

    /// <summary>The truck leaves: the parcel is now between hubs, expected after <paramref name="transitHours"/>.</summary>
    public void DepartOnTransfer(string transferNumber, string fromHub, string toHub, int transitHours, Actor actor, DateTimeOffset now)
    {
        var next = IsReturning ? ShipmentStatus.ReturnInTransit : ShipmentStatus.InTransitToHub;
        AdvanceTo(next, now, new MilestoneDetails(Location: fromHub, Notes: $"{transferNumber} → {IraqHubs.TagOf(toHub)}"), actor.Source, actor.Name, now);
        EtaAt = IsReturning ? EtaAt : Max(EtaAt, now.AddHours(transitHours));
    }

    /// <summary>Re-shelves a parcel that is already at a hub; recorded as a location event without a status change.</summary>
    public void MoveToBin(string bin, string? recordedBy, DateTimeOffset now, string? location = null)
    {
        if (!ShipmentLifecycle.IsInWarehouse(Status) && Status != ShipmentStatus.ReturnAtHub)
        {
            throw Rule("shipment.not_in_warehouse", "This parcel is not in the warehouse.");
        }

        WarehouseBin = Bin(bin);
        UpdatedAt = now;
        _events.Add(ShipmentEvent.Create(this, Status, now, new MilestoneDetails(Location: Clean(location), WarehouseBin: WarehouseBin), "scan", recordedBy, now));
    }

    /// <summary>New delivery date agreed with the customer.</summary>
    public void Reschedule(DateTimeOffset date, Actor actor, DateTimeOffset now)
    {
        EnsureChangeable();
        var when = DeliveryPromise.EndOfDay(date);
        RescheduledTo = when;
        EtaAt = when;
        UpdatedAt = now;
        Note("→ " + when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), actor, now);
    }

    /// <summary>Corrects the delivery address. A different governorate changes the destination hub (and maybe the route).</summary>
    public void ChangeAddress(string? governorate, string? city, string address, string? landmark, HubNetwork network, Actor actor, DateTimeOffset now)
    {
        EnsureChangeable();
        if (governorate is not null)
        {
            if (!IraqGovernorates.IsKnown(governorate))
            {
                throw Rule("shipment.domestic_address", "Domestic deliveries need governorate, address and mobile number.");
            }

            DestinationCode = governorate.ToUpperInvariant();
            DestinationHubCode = network.ResolveDestinationHub(DestinationCode).Code;
        }

        DestinationCity = Clean(city) ?? DestinationCity;
        DeliveryAddress = address.Trim();
        Landmark = Clean(landmark) ?? Landmark;
        UpdatedAt = now;
        Note(IraqGovernorates.NameOrCode(DestinationCode) + " — " + DeliveryAddress, actor, now);
    }

    public void ChangePhone(string phone, Actor actor, DateTimeOffset now)
    {
        EnsureChangeable();
        ConsigneePhone = IraqPhone.Normalize(phone) ?? throw Rule("shipment.domestic_address", "Domestic deliveries need governorate, address and mobile number.");
        UpdatedAt = now;
        Note(IraqPhone.Display(ConsigneePhone), actor, now);
    }

    /// <summary>
    /// Corrects a domestic order before it is out with a courier. A new governorate moves the destination hub; amounts can
    /// change until cash is collected. The history records what changed and who changed it.
    /// </summary>
    public IReadOnlyList<string> EditOrder(UpdateOrderRequest request, HubNetwork network, Actor actor, DateTimeOffset now)
    {
        EnsureChangeable();
        if (ServiceType != ServiceType.Domestic)
        {
            throw Rule("shipment.not_changeable", "This order can no longer be changed.", ShipmentTexts.Of(Status));
        }

        var phone = IraqPhone.Normalize(request.ConsigneePhone)
            ?? throw Rule("shipment.domestic_address", "Domestic deliveries need governorate, address and mobile number.");
        var governorate = request.DestinationCode.Trim().ToUpperInvariant();
        if (!IraqGovernorates.IsKnown(governorate) || string.IsNullOrWhiteSpace(request.DeliveryAddress))
        {
            throw Rule("shipment.domestic_address", "Domestic deliveries need governorate, address and mobile number.");
        }

        var changes = new List<string>();
        void Track<T>(string key, T before, T after, Func<T, string>? show = null)
        {
            if (!EqualityComparer<T>.Default.Equals(before, after))
            {
                changes.Add(show is null ? ShipmentTexts.Get("Edit_" + key) : $"{ShipmentTexts.Get("Edit_" + key)} {show(before)} → {show(after)}");
            }
        }

        static string Money(decimal? amount) => (amount ?? 0).ToString("N0", CultureInfo.InvariantCulture);

        var cod = request.CodAmount is > 0 ? request.CodAmount : null;
        var fee = request.DeliveryFee is > 0 ? request.DeliveryFee : null;
        Track("Name", ConsigneeName, request.ConsigneeName.Trim());
        Track("Phone", ConsigneePhone, phone, p => IraqPhone.Display(p));
        Track("Governorate", DestinationCode, governorate, IraqGovernorates.NameOrCode);
        Track("Address", (DestinationCity, DeliveryAddress, Landmark), (Clean(request.DestinationCity), request.DeliveryAddress.Trim(), Clean(request.Landmark)));
        Track("Items", (ItemsDescription, Pieces, GrossWeightKg), (Clean(request.ItemsDescription), request.Pieces, request.GrossWeightKg));
        Track("Cod", CodAmount, cod, Money);
        Track("Fee", DeliveryFee, fee, Money);
        Track("Reference", ExternalReference, Clean(request.ExternalReference));
        Track("Notes", Notes, Clean(request.Notes));
        if (changes.Count == 0)
        {
            return changes;
        }

        ConsigneeName = request.ConsigneeName.Trim();
        ConsigneePhone = phone;
        if (governorate != DestinationCode)
        {
            DestinationCode = governorate;
            DestinationHubCode = network.ResolveDestinationHub(governorate).Code;
        }

        DestinationCity = Clean(request.DestinationCity);
        DeliveryAddress = request.DeliveryAddress.Trim();
        Landmark = Clean(request.Landmark);
        ItemsDescription = Clean(request.ItemsDescription);
        Pieces = request.Pieces;
        GrossWeightKg = request.GrossWeightKg;
        ChargeableWeightKg = ChargeableWeight.Calculate(Mode, request.GrossWeightKg, VolumeCbm);
        CodAmount = cod;
        CodCurrency = cod is null ? null : CodCurrency ?? "IQD";
        DeliveryFee = fee;
        ExternalReference = Clean(request.ExternalReference);
        Notes = Clean(request.Notes);
        UpdatedAt = now;
        Note("✎ " + string.Join("، ", changes), actor, now);
        return changes;
    }

    /// <summary>The customer rates the delivery on the tracking page.</summary>
    public void Rate(int stars, string? comment, DateTimeOffset now)
    {
        if (!CanBeRated(now))
        {
            throw Rule("shipment.rating_not_possible", "This delivery can no longer be rated.");
        }

        Rating = Math.Clamp(stars, 1, 5);
        RatingComment = Clean(comment) is { } text ? text[..Math.Min(text.Length, 500)] : null;
        RatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Holds the order with the seller until the customer confirms it.</summary>
    public void RequireConfirmation(DateTimeOffset now)
    {
        if (Status is not (ShipmentStatus.Booked or ShipmentStatus.Draft) || Confirmation != ConfirmationStatus.NotRequired)
        {
            return;
        }

        Confirmation = ConfirmationStatus.Pending;
        ConfirmationRequestedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records the result of a confirmation call or the customer's answer on the tracking page. A declined order is
    /// cancelled before collection; "no answer" keeps it on hold and counts the attempt.
    /// </summary>
    public void RecordConfirmation(ConfirmationOutcome outcome, string? note, Actor actor, DateTimeOffset now)
    {
        if (Confirmation != ConfirmationStatus.Pending)
        {
            throw Rule("shipment.confirmation_not_pending", "This order is not waiting for confirmation.");
        }

        switch (outcome)
        {
            case ConfirmationOutcome.Confirmed:
                Confirmation = ConfirmationStatus.Confirmed;
                ConfirmationDecidedAt = now;
                ConfirmationBy = actor.Name;
                Note("✔ " + ShipmentTexts.Of(ConfirmationStatus.Confirmed) + (Clean(note) is { } n ? " — " + n : ""), actor, now);
                break;
            case ConfirmationOutcome.NoAnswer:
                ConfirmationAttempts++;
                Note("☎ " + ShipmentTexts.Of(ConfirmationOutcome.NoAnswer) + $" ({ConfirmationAttempts})" + (Clean(note) is { } m ? " — " + m : ""), actor, now);
                break;
            case ConfirmationOutcome.Declined:
                Confirmation = ConfirmationStatus.Declined;
                ConfirmationDecidedAt = now;
                ConfirmationBy = actor.Name;
                Cancel(Clean(note) ?? ShipmentTexts.Of(ConfirmationStatus.Declined), actor, now);
                break;
        }

        UpdatedAt = now;
    }

    /// <summary>Cancels an order that has not been collected; later on, cancelling means returning it to the seller.</summary>
    public void Cancel(string? reason, Actor actor, DateTimeOffset now)
    {
        if (Status is ShipmentStatus.Draft or ShipmentStatus.Booked)
        {
            AdvanceTo(ShipmentStatus.Cancelled, now, new MilestoneDetails(Notes: reason), actor.Source, actor.Name, now);
            return;
        }

        StartReturn(Contracts.ReturnReason.SellerCancelled, reason, actor, now);
    }

    /// <summary>The courier handed this parcel's collected cash in at the office.</summary>
    public void MarkCashHandedOver(Guid handoverId, DateTimeOffset now)
    {
        if (CodCollectedAt is null || CodHandedOverAt is not null)
        {
            throw Rule("cash.not_with_courier", "This parcel's cash is not with the courier.", TrackingNumber);
        }

        CashHandoverId = handoverId;
        CodHandedOverAt = now;
        UpdatedAt = now;
    }

    /// <summary>Marks a delivered COD shipment as remitted to the seller in a settlement batch.</summary>
    public void MarkCodSettled(Guid settlementId, DateTimeOffset now)
    {
        CodSettlementId = settlementId;
        CodSettledAt = now;
        UpdatedAt = now;
    }

    private void Reshelve(HubInfo hub, string? shelf, Actor actor, DateTimeOffset now)
    {
        WarehouseBin = shelf ?? WarehouseBin;
        UpdatedAt = now;
        _events.Add(ShipmentEvent.Create(this, Status, now, new MilestoneDetails(Location: hub.Code, WarehouseBin: WarehouseBin), actor.Source, actor.Name, now));
    }

    private void Note(string text, Actor actor, DateTimeOffset now) =>
        _events.Add(ShipmentEvent.Create(this, Status, now, new MilestoneDetails(Location: CurrentHubCode, Notes: text), actor.Source, actor.Name, now));

    private void EnsureChangeable()
    {
        if (Status is ShipmentStatus.OutForDelivery or ShipmentStatus.Delivered or ShipmentStatus.Returned or ShipmentStatus.Cancelled || IsReturning)
        {
            throw Rule("shipment.not_changeable", "This order can no longer be changed.", ShipmentTexts.Of(Status));
        }
    }

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset b) => a is null || a < b ? b : a;

    private static DomainException Rule(string code, string message, params object?[] args) => new(code, message, args);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Shelf codes are stored upper-case so "a-01-2" and "A-01-2" are the same shelf.</summary>
    private static string? Bin(string? value) => Clean(value)?.ToUpperInvariant();
}
