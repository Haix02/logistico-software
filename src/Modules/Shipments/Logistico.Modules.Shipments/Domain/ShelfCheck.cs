using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Domain;

/// <summary>
/// A count of what is really on a hub shelf (or in the whole hub), against what the system says should be there — the
/// cycle count of warehouse systems. Starting it takes a snapshot of the parcels expected; each scan marks one found,
/// on the wrong shelf, or not expected here; finishing turns everything not scanned into missing. Kept with who counted.
/// </summary>
internal sealed class ShelfCheck
{
    /// <summary>Statuses in which a parcel sits on a hub shelf (on its way to the customer, or back to the seller).</summary>
    public static readonly ShipmentStatus[] OnShelf = [.. ShipmentLifecycle.WarehouseStatuses, ShipmentStatus.ReturnAtHub];

    private readonly List<ShelfCheckLine> _lines = [];

    private ShelfCheck()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Number { get; private set; } = "";
    public string HubCode { get; private set; } = "";

    /// <summary>The shelf counted; null for the whole hub.</summary>
    public string? Bin { get; private set; }

    public bool Finished { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public string? StartedBy { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public string? FinishedBy { get; private set; }
    public uint Version { get; private set; }
    public IReadOnlyList<ShelfCheckLine> Lines => _lines;

    public static ShelfCheck Start(Guid tenantId, string number, string hubCode, string? bin, IEnumerable<Shipment> expected, string? by, DateTimeOffset now)
    {
        var check = new ShelfCheck
        {
            Id = Guid.CreateVersion7(now),
            TenantId = tenantId,
            Number = number,
            HubCode = hubCode,
            Bin = string.IsNullOrWhiteSpace(bin) ? null : bin.Trim().ToUpperInvariant(),
            StartedAt = now,
            StartedBy = by,
        };
        check._lines.AddRange(expected.Select(s => ShelfCheckLine.Expect(check.Id, s, now)));
        return check;
    }

    /// <summary>
    /// One parcel scanned on the shelf: <paramref name="line"/> is its line in this check if it has one (expected, or
    /// scanned before), <paramref name="shipment"/> the parcel with that tracking number (null when unknown). Scanning the
    /// same parcel again changes nothing. A new line (a parcel not expected on this shelf) is returned for the caller to add.
    /// </summary>
    public ShelfCheckLine Scan(ShelfCheckLine? line, string trackingNumber, Shipment? shipment, DateTimeOffset now)
    {
        if (Finished)
        {
            throw new DomainException("shelf_check.finished", "This shelf check is finished; start a new one.");
        }

        if (line is not null)
        {
            if (line.State == ShelfCheckState.Expected)
            {
                line.Mark(ShelfCheckState.Found, now);
            }

            return line;
        }

        // Not in the snapshot: on another shelf of this hub, or somewhere else entirely as far as the system knows.
        var state = shipment is not null && shipment.CurrentHubCode == HubCode && OnShelf.Contains(shipment.Status)
            ? ShelfCheckState.WrongShelf
            : ShelfCheckState.NotExpected;
        return ShelfCheckLine.Stray(Id, trackingNumber.Trim().ToUpperInvariant(), shipment, state, now);
    }

    /// <summary>Ends the count: the expected parcels not scanned (<paramref name="pending"/>) are missing.</summary>
    public void Finish(IEnumerable<ShelfCheckLine> pending, string? by, DateTimeOffset now)
    {
        if (Finished)
        {
            return;
        }

        foreach (var line in pending.Where(l => l.ShelfCheckId == Id && l.State == ShelfCheckState.Expected))
        {
            line.Mark(ShelfCheckState.Missing, null);
        }

        Finished = true;
        FinishedAt = now;
        FinishedBy = by;
    }
}

/// <summary>One parcel in a shelf check: expected and not yet scanned, found, missing, on the wrong shelf, or not expected here.</summary>
internal sealed class ShelfCheckLine
{
    private ShelfCheckLine()
    {
    }

    public Guid Id { get; private set; }
    public Guid ShelfCheckId { get; private set; }
    public Guid? ShipmentId { get; private set; }
    public string TrackingNumber { get; private set; } = "";
    public ShelfCheckState State { get; private set; }

    /// <summary>Where the system had the parcel when it was expected or scanned (hub, shelf, status).</summary>
    public string? SystemHubCode { get; private set; }

    public string? SystemBin { get; private set; }
    public ShipmentStatus? SystemStatus { get; private set; }
    public DateTimeOffset? ScannedAt { get; private set; }

    public static ShelfCheckLine Expect(Guid checkId, Shipment s, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        ShelfCheckId = checkId,
        ShipmentId = s.Id,
        TrackingNumber = s.TrackingNumber,
        State = ShelfCheckState.Expected,
        SystemHubCode = s.CurrentHubCode,
        SystemBin = s.WarehouseBin,
        SystemStatus = s.Status,
    };

    public static ShelfCheckLine Stray(Guid checkId, string trackingNumber, Shipment? s, ShelfCheckState state, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        ShelfCheckId = checkId,
        ShipmentId = s?.Id,
        TrackingNumber = trackingNumber,
        State = state,
        SystemHubCode = s?.CurrentHubCode,
        SystemBin = s?.WarehouseBin,
        SystemStatus = s?.Status,
        ScannedAt = now,
    };

    public void Mark(ShelfCheckState state, DateTimeOffset? scannedAt)
    {
        State = state;
        ScannedAt = scannedAt ?? ScannedAt;
    }
}
