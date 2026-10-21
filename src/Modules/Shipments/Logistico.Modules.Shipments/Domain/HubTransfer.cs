using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Domain;

/// <summary>
/// A line-haul truck between two hubs (e.g. Baghdad → Basra) and its manifest.
/// Loading: parcels are planned (automatically) and confirmed by scan → InTransit: the truck left with the loaded
/// parcels → Received: everything was scanned off at the destination hub, or the manifest was closed with the
/// missing parcels flagged.
/// </summary>
internal sealed class HubTransfer
{
    private readonly List<TransferItem> _items = [];

    private HubTransfer()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Number { get; private set; } = "";
    public string FromHubCode { get; private set; } = "";
    public string ToHubCode { get; private set; } = "";
    public TransferStatus Status { get; private set; }
    public string? VehiclePlate { get; private set; }
    public string? DriverName { get; private set; }
    public string? SealNumber { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DepartedAt { get; private set; }
    public DateTimeOffset? ExpectedAt { get; private set; }
    public DateTimeOffset? ArrivedAt { get; private set; }
    public string? CreatedBy { get; private set; }
    public string? DepartedBy { get; private set; }
    public string? ReceivedBy { get; private set; }
    public uint Version { get; private set; }

    /// <summary>
    /// Counts manifest changes. Every change updates the truck's own row, so its version check also catches a parcel
    /// being added while someone else sends the truck off.
    /// </summary>
    public int ManifestRevision { get; private set; }

    public IReadOnlyList<TransferItem> Items => _items;

    public static HubTransfer Open(Guid tenantId, string number, string fromHub, string toHub, string? vehiclePlate, string? driverName, string? by, DateTimeOffset now)
    {
        // Callers check both hubs against the company's network; the truck itself only refuses a lane to nowhere.
        if (string.IsNullOrWhiteSpace(fromHub) || string.IsNullOrWhiteSpace(toHub) || string.Equals(fromHub, toHub, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("transfer.invalid_lane", "Choose two different hubs.");
        }

        return new HubTransfer
        {
            Id = Guid.CreateVersion7(now),
            TenantId = tenantId,
            Number = number,
            FromHubCode = fromHub,
            ToHubCode = toHub,
            Status = TransferStatus.Loading,
            VehiclePlate = Clean(vehiclePlate),
            DriverName = Clean(driverName),
            CreatedAt = now,
            CreatedBy = by,
        };
    }

    /// <summary>Adds a parcel that is waiting at the origin hub for this lane (automation or staff).</summary>
    public TransferItem Plan(Shipment shipment, DateTimeOffset now)
    {
        EnsureLoading();
        if (shipment.CurrentHubCode != FromHubCode || shipment.TargetHubCode != ToHubCode)
        {
            throw new DomainException("transfer.wrong_lane", "This parcel is not waiting for this truck.", shipment.TrackingNumber);
        }

        var existing = _items.FirstOrDefault(i => i.ShipmentId == shipment.Id);
        if (existing is not null)
        {
            return existing;
        }

        var item = TransferItem.Create(this, shipment, now);
        _items.Add(item);
        ManifestRevision++;
        shipment.AssignToTransfer(Id, now);
        return item;
    }

    /// <summary>Throws unless the parcel may be loaded now: the truck is still loading and runs the parcel's lane.</summary>
    public void EnsureCanTake(Shipment shipment)
    {
        EnsureLoading();
        if (shipment.CurrentHubCode != FromHubCode || shipment.TargetHubCode != ToHubCode)
        {
            throw new DomainException("transfer.wrong_lane", "This parcel is not waiting for this truck.", shipment.TrackingNumber);
        }
    }

    /// <summary>Scan while loading: confirms the parcel is physically on the truck.</summary>
    public TransferItem Load(Shipment shipment, DateTimeOffset now)
    {
        var item = _items.FirstOrDefault(i => i.ShipmentId == shipment.Id) ?? Plan(shipment, now);
        item.MarkLoaded(now);
        ManifestRevision++;
        return item;
    }

    /// <summary>Takes a planned or loaded parcel off the truck before it leaves.</summary>
    public void Unplan(Shipment shipment, DateTimeOffset now)
    {
        EnsureLoading();
        var item = _items.FirstOrDefault(i => i.ShipmentId == shipment.Id);
        if (item is not null)
        {
            _items.Remove(item);
            ManifestRevision++;
            shipment.RemoveFromTransfer(Id, now);
        }
    }

    /// <summary>
    /// Takes manifest lines off the truck whose parcels can no longer travel on it (returned, delivered, moved to
    /// another truck), so one stale line cannot keep the truck from leaving.
    /// </summary>
    public IReadOnlyList<TransferItem> DropStale(IReadOnlyDictionary<Guid, Shipment> shipments, DateTimeOffset now)
    {
        EnsureLoading();
        var stale = _items.Where(i => !shipments.TryGetValue(i.ShipmentId, out var s)
                || s.TransferId != Id
                || !s.NeedsTransfer
                || s.CurrentHubCode != FromHubCode
                || s.TargetHubCode != ToHubCode)
            .ToList();
        foreach (var item in stale)
        {
            _items.Remove(item);
            ManifestRevision++;
            if (shipments.TryGetValue(item.ShipmentId, out var shipment))
            {
                shipment.RemoveFromTransfer(Id, now);
            }
        }

        return stale;
    }

    /// <summary>
    /// The truck leaves. Only scanned (loaded) parcels travel; planned parcels that were not loaded stay at the hub.
    /// Returns the parcels that were left behind so the caller can release them.
    /// </summary>
    public IReadOnlyList<TransferItem> Depart(string? vehiclePlate, string? driverName, string? sealNumber, int transitHours, string? by, DateTimeOffset now)
    {
        EnsureLoading();
        if (!_items.Any(i => i.State == TransferItemState.Loaded))
        {
            throw new DomainException("transfer.empty", "Scan at least one parcel onto the truck before it leaves.");
        }

        var leftBehind = _items.Where(i => i.State == TransferItemState.Planned).ToList();
        foreach (var item in leftBehind)
        {
            _items.Remove(item);
        }

        VehiclePlate = Clean(vehiclePlate) ?? VehiclePlate;
        DriverName = Clean(driverName) ?? DriverName;
        SealNumber = Clean(sealNumber) ?? SealNumber;
        Status = TransferStatus.InTransit;
        DepartedAt = now;
        DepartedBy = by;
        ExpectedAt = now.AddHours(transitHours);
        return leftBehind;
    }

    /// <summary>Scan at the destination hub: the parcel came off the truck. Closes the manifest when everything arrived.</summary>
    public TransferItem Receive(Shipment shipment, string? by, DateTimeOffset now)
    {
        if (Status != TransferStatus.InTransit)
        {
            throw new DomainException("transfer.not_in_transit", "This truck is not on the road.", Number);
        }

        var item = _items.FirstOrDefault(i => i.ShipmentId == shipment.Id)
            ?? throw new DomainException("transfer.not_on_manifest", "This parcel is not on this truck's manifest.", shipment.TrackingNumber, Number);
        item.MarkReceived(now);
        ArrivedAt ??= now;
        ReceivedBy ??= by;
        if (_items.All(i => i.State == TransferItemState.Received))
        {
            Status = TransferStatus.Received;
        }

        return item;
    }

    /// <summary>Closes the manifest at the destination; parcels that never came off the truck are marked missing.</summary>
    public IReadOnlyList<TransferItem> Close(string? notes, string? by, DateTimeOffset now)
    {
        if (Status != TransferStatus.InTransit)
        {
            throw new DomainException("transfer.not_in_transit", "This truck is not on the road.", Number);
        }

        var missing = _items.Where(i => i.State == TransferItemState.Loaded).ToList();
        foreach (var item in missing)
        {
            item.MarkMissing();
        }

        Status = TransferStatus.Received;
        ArrivedAt ??= now;
        ReceivedBy ??= by;
        Notes = Clean(notes) ?? Notes;
        return missing;
    }

    public bool IsOverdue(DateTimeOffset now, int graceHours) =>
        Status == TransferStatus.InTransit && ExpectedAt is { } expected && now > expected.AddHours(graceHours);

    private void EnsureLoading()
    {
        if (Status != TransferStatus.Loading)
        {
            throw new DomainException("transfer.not_loading", "This truck has already left.", Number);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class TransferItem
{
    private TransferItem()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TransferId { get; private set; }
    public Guid ShipmentId { get; private set; }
    public string TrackingNumber { get; private set; } = "";
    public TransferItemState State { get; private set; }
    public bool IsReturn { get; private set; }
    public DateTimeOffset PlannedAt { get; private set; }
    public DateTimeOffset? LoadedAt { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }

    internal static TransferItem Create(HubTransfer transfer, Shipment shipment, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        TenantId = transfer.TenantId,
        TransferId = transfer.Id,
        ShipmentId = shipment.Id,
        TrackingNumber = shipment.TrackingNumber,
        State = TransferItemState.Planned,
        IsReturn = shipment.IsReturning,
        PlannedAt = now,
    };

    internal void MarkLoaded(DateTimeOffset now)
    {
        State = TransferItemState.Loaded;
        LoadedAt ??= now;
    }

    internal void MarkReceived(DateTimeOffset now)
    {
        State = TransferItemState.Received;
        ReceivedAt = now;
    }

    internal void MarkMissing() => State = TransferItemState.Missing;
}
