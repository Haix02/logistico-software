using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Application;

/// <summary>Loads a hub's sort plan with what is on its shelves now, so receive scans can choose a shelf.</summary>
internal sealed class SortPlanner(ShipmentsDbContext db)
{
    public async Task<HubSortPlan> ForHubAsync(string hubCode, CancellationToken ct)
    {
        var rules = await db.ShelfRules.AsNoTracking().Where(r => r.HubCode == hubCode).ToListAsync(ct);
        if (rules.Count == 0)
        {
            return new HubSortPlan(hubCode, [], []);
        }

        var shelves = rules.SelectMany(r => r.Shelves).Distinct().ToList();
        var load = await db.Shipments.AsNoTracking()
            .Where(s => s.CurrentHubCode == hubCode && s.WarehouseBin != null && shelves.Contains(s.WarehouseBin))
            .GroupBy(s => s.WarehouseBin!)
            .Select(g => new { Shelf = g.Key, Parcels = g.Count() })
            .ToDictionaryAsync(x => x.Shelf, x => x.Parcels, StringComparer.Ordinal, ct);
        return new HubSortPlan(hubCode, rules, load);
    }
}

/// <summary>Where the sort plan puts a parcel: the shelf, the rule that chose it, and whether every shelf it could use is full.</summary>
internal sealed record ShelfPlacement(string Shelf, ShelfRule Rule, ShelfPurpose Purpose, bool Full, bool AlreadyThere);

/// <summary>
/// One hub's sort plan during a batch of scans. Rules are tried most specific first (older first among equals); a rule's
/// shelves fill in order and a full shelf overflows to the next one, then to the next matching rule. When every shelf the
/// parcel could use is full it goes on the emptiest of them and the scan says so. Loads are kept up to date across the
/// batch, so a bulk receive fills shelves the same way single scans do.
/// </summary>
internal sealed class HubSortPlan
{
    private readonly List<ShelfRule> rules;
    private readonly Dictionary<string, int> load;

    public HubSortPlan(string hubCode, IEnumerable<ShelfRule> rules, Dictionary<string, int> load)
    {
        HubCode = hubCode;
        this.rules = [.. rules.OrderByDescending(r => r.Specificity).ThenBy(r => r.CreatedAt).ThenBy(r => r.Id)];
        this.load = load;
    }

    public string HubCode { get; }

    /// <summary>The rules in the order they are tried.</summary>
    public IReadOnlyList<ShelfRule> Rules => rules;

    public int Load(string shelf) => load.GetValueOrDefault(shelf);

    /// <summary>The shelf for <paramref name="shipment"/> if it is received here now, or null when no rule matches.</summary>
    public ShelfPlacement? Place(Shipment shipment)
    {
        if (rules.Count == 0)
        {
            return null;
        }

        var parcel = ShelfParcel.Of(shipment, HubCode);
        var matching = rules.Where(r => r.Matches(parcel)).ToList();
        if (matching.Count == 0)
        {
            return null;
        }

        // Scanned again while on a shelf the plan allows: it stays where it is.
        if (ShelfAtThisHub(shipment) is { } current && matching.FirstOrDefault(r => r.Shelves.Contains(current)) is { } keeping)
        {
            return new ShelfPlacement(current, keeping, parcel.Purpose, false, true);
        }

        foreach (var rule in matching)
        {
            foreach (var shelf in rule.Shelves)
            {
                if (rule.CapacityPerShelf is not { } capacity || Load(shelf) < capacity)
                {
                    return new ShelfPlacement(shelf, rule, parcel.Purpose, false, false);
                }
            }
        }

        var emptiest = matching
            .SelectMany(r => r.Shelves.Select(shelf => (Rule: r, Shelf: shelf)))
            .MinBy(x => (double)Load(x.Shelf) / x.Rule.CapacityPerShelf!.Value);
        return new ShelfPlacement(emptiest.Shelf, emptiest.Rule, parcel.Purpose, true, false);
    }

    /// <summary>Records that a parcel left <paramref name="from"/> (a shelf at this hub, if any) for <paramref name="to"/>.</summary>
    public void Moved(string? from, string? to)
    {
        if (from == to)
        {
            return;
        }

        if (from is not null && load.TryGetValue(from, out var before))
        {
            load[from] = Math.Max(0, before - 1);
        }

        if (to is not null && rules.Any(r => r.Shelves.Contains(to)))
        {
            load[to] = Load(to) + 1;
        }
    }

    /// <summary>The shelf the parcel sits on at this hub, if it is on one here (leaving a hub clears both).</summary>
    public string? ShelfAtThisHub(Shipment shipment) => shipment.CurrentHubCode == HubCode ? shipment.WarehouseBin : null;
}
