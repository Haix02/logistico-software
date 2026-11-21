using System.Diagnostics;
using System.Globalization;
using Google.OrTools.Sat;

namespace Logistico.Optimization.LineHaul;

/// <summary>
/// Line-haul planning as capacitated service network design (Crainic 2024; docs/20 §3.1), solved exactly with
/// OR-Tools CP-SAT. Parcels with the same lane, deadline and size form one commodity; each commodity picks routes
/// (a departure on the direct lane, or two legs through a third hub when allowed) and trucks are bought per departure.
/// The objectives are lexicographic, solved in three stages so that no stage can trade away the one before it:
/// 1. least lateness (parcel-hours, deliveries weighted twice a return);
/// 2. least cost (trucks × trip cost) without more lateness;
/// 3. earliest arrivals without more lateness or cost.
/// </summary>
public static class LineHaulPlanner
{
    public const string SolverName = "OR-Tools CP-SAT";

    /// <summary>Weights are counted in 100 g and volumes in litres; sizes are rounded up, so a plan that fits the model fits the truck.</summary>
    internal const double WeightUnitKg = 0.1;
    internal const double VolumeUnitM3 = 0.001;

    public static LineHaulPlan Solve(LineHaulProblem problem, SolveOptions? options = null)
    {
        options ??= SolveOptions.Default;
        var clock = Stopwatch.StartNew();
        if (problem.Parcels.Count == 0)
        {
            return LineHaulPlans.Compose(problem, new Dictionary<Guid, IReadOnlyList<Hop>>(), new Dictionary<Slot, int>(), SolveReport.Nothing(SolverName));
        }

        var commodities = Commodities(problem);
        var model = new CpModel();
        var choices = new List<(Commodity Commodity, RouteOption Option, IntVar Count, long LateMinutes, long ArriveMinutes)>();
        var waits = new List<(Commodity Commodity, IntVar Count, long Minutes)>();
        var slots = new Dictionary<Slot, (HaulLane Lane, IntVar Trucks, LinearExprBuilder Weight, LinearExprBuilder Volume)>();

        foreach (var commodity in commodities)
        {
            var size = commodity.Parcels.Count;
            var assigned = LinearExpr.NewBuilder();
            foreach (var option in RouteOptions(problem, commodity))
            {
                var count = model.NewIntVar(0, size, $"n_{choices.Count}");
                assigned.Add(count);
                choices.Add((commodity, option, count, LateMinutes(option.ArriveAt, commodity.DueAt), Minutes(problem.Now, option.ArriveAt)));
                foreach (var hop in option.Hops)
                {
                    if (!slots.TryGetValue(hop.Slot, out var slot))
                    {
                        slot = (hop.Lane, model.NewIntVar(0, problem.MaxTrucksPerDeparture, $"y_{slots.Count}"), LinearExpr.NewBuilder(), LinearExpr.NewBuilder());
                        slots[hop.Slot] = slot;
                    }

                    slot.Weight.AddTerm(count, commodity.WeightUnits);
                    slot.Volume.AddTerm(count, commodity.VolumeUnits);
                }
            }

            var wait = model.NewIntVar(0, size, $"u_{waits.Count}");
            assigned.Add(wait);
            waits.Add((commodity, wait, UnshippedMinutes(problem, commodity)));
            model.Add(assigned == size);
        }

        var capacityWeight = (long)Math.Floor(problem.Truck.CapacityKg / WeightUnitKg);
        var capacityVolume = (long)Math.Floor(problem.Truck.CapacityM3 / VolumeUnitM3);
        foreach (var (_, (_, trucks, weight, volume)) in slots)
        {
            model.Add(weight <= LinearExpr.Term(trucks, capacityWeight));
            model.Add(volume <= LinearExpr.Term(trucks, capacityVolume));
        }

        if (problem.MaxTrucksPerHubPerDeparture > 0)
        {
            foreach (var group in slots.GroupBy(s => (s.Key.From, s.Key.DepartAt)))
            {
                model.Add(LinearExpr.Sum(group.Select(s => s.Value.Trucks)) <= problem.MaxTrucksPerHubPerDeparture);
            }
        }

        var lateness = LinearExpr.NewBuilder();
        foreach (var c in choices.Where(c => c.LateMinutes > 0))
        {
            lateness.AddTerm(c.Count, c.LateMinutes * c.Commodity.Priority);
        }

        foreach (var w in waits)
        {
            lateness.AddTerm(w.Count, w.Minutes * w.Commodity.Priority);
        }

        var cost = LinearExpr.NewBuilder();
        foreach (var (_, (lane, trucks, _, _)) in slots)
        {
            cost.AddTerm(trucks, (long)problem.Truck.TripCost(lane.Km));
        }

        var earliness = LinearExpr.NewBuilder();
        foreach (var c in choices)
        {
            earliness.AddTerm(c.Count, c.ArriveMinutes);
        }

        var notes = new List<string>();
        var allOptimal = true;
        double? costBound = null;
        double costValue = 0;
        CpSolver? last = null;
        foreach (var (stage, objective, share) in new[] { ("lateness", lateness, 0.45), ("cost", cost, 0.45), ("earliness", earliness, 0.10) })
        {
            model.Minimize(objective);
            var solver = new CpSolver
            {
                StringParameters = string.Create(CultureInfo.InvariantCulture,
                    $"max_time_in_seconds:{Math.Max(0.5, options.TimeLimitSeconds * share):0.###} num_workers:{Math.Max(1, options.Workers)} random_seed:{options.Seed}"),
            };
            var status = solver.Solve(model);
            if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
            {
                if (last is null)
                {
                    // Not even the first stage found a plan in time: fall back to today's practice, clearly labelled.
                    var fallback = LineHaulBaseline.Plan(problem);
                    return fallback with { Report = fallback.Report with { Status = SolveStatus.NoSolution, Solver = SolverName, Seconds = clock.Elapsed.TotalSeconds, Notes = [$"{stage}: {status}"] } };
                }

                notes.Add($"{stage}: {status}, earlier stage kept");
                allOptimal = false;
                break;
            }

            var value = (long)Math.Round(solver.ObjectiveValue);
            allOptimal &= status == CpSolverStatus.Optimal;
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"{stage}: {value} ({status})"));
            if (stage == "cost")
            {
                costValue = value;
                costBound = solver.BestObjectiveBound;
            }

            // Keep what this stage reached while the next one improves its own objective; hint the solution found.
            model.Add(objective <= value);
            model.ClearHints();
            foreach (var c in choices)
            {
                model.AddHint(c.Count, solver.Value(c.Count));
            }

            foreach (var w in waits)
            {
                model.AddHint(w.Count, solver.Value(w.Count));
            }

            foreach (var s in slots.Values)
            {
                model.AddHint(s.Trucks, solver.Value(s.Trucks));
            }

            last = solver;
        }

        // Turn commodity counts into parcels: within a commodity parcels are interchangeable (same lane, deadline, rounded size).
        var routes = new Dictionary<Guid, IReadOnlyList<Hop>>();
        foreach (var group in choices.GroupBy(c => c.Commodity))
        {
            var queue = new Queue<HaulParcel>(group.Key.Parcels);
            foreach (var choice in group.OrderBy(c => c.Option.ArriveAt))
            {
                for (var i = last!.Value(choice.Count); i > 0; i--)
                {
                    routes[queue.Dequeue().Id] = choice.Option.Hops;
                }
            }
        }

        var truckCounts = slots.ToDictionary(s => s.Key, s => (int)last!.Value(s.Value.Trucks));
        var report = new SolveReport(allOptimal ? SolveStatus.Optimal : SolveStatus.Feasible, costValue, costBound, clock.Elapsed.TotalSeconds, SolverName, options.Seed, notes);
        return LineHaulPlans.Compose(problem, routes, truckCounts, report);
    }

    internal static long LateMinutes(DateTimeOffset arrive, DateTimeOffset due) => Math.Max(0, (long)Math.Ceiling((arrive - due).TotalMinutes));

    private static long Minutes(DateTimeOffset from, DateTimeOffset to) => Math.Max(0, (long)Math.Ceiling((to - from).TotalMinutes));

    private static long UnshippedMinutes(LineHaulProblem problem, Commodity commodity) =>
        WaitingMinutes(problem, commodity.AtHub, commodity.ToHub, commodity.DueAt);

    /// <summary>
    /// What leaving a parcel for a later plan costs, in lateness minutes: it can only take the next departure after the
    /// horizon, so it arrives then on the direct lane; waiting costs a day minus the slack it still has, and never less
    /// than an hour. The parcels with the most time to spare are therefore the ones that wait, and waiting is never free.
    /// </summary>
    internal static long WaitingMinutes(LineHaulProblem problem, string atHub, string toHub, DateTimeOffset due)
    {
        var direct = problem.Lane(atHub, toHub)?.Hours ?? 0;
        var departures = problem.Departures.Order().ToList();
        var spacing = departures.Zip(departures.Skip(1), (a, b) => b - a).Where(g => g > TimeSpan.Zero).DefaultIfEmpty(TimeSpan.FromDays(1)).Min();
        var arrive = problem.HorizonEnd + spacing + TimeSpan.FromHours(direct);
        var signed = (long)Math.Ceiling((arrive - due).TotalMinutes);
        return Math.Max(60, (long)(LineHaulProblem.UnshippedPenaltyHours * 60) + signed);
    }

    internal static long WeightUnits(double kg) => Math.Max(1, (long)Math.Ceiling(kg / WeightUnitKg - 1e-9));

    internal static long VolumeUnits(double m3) => Math.Max(0, (long)Math.Ceiling(m3 / VolumeUnitM3 - 1e-9));

    private static List<Commodity> Commodities(LineHaulProblem problem) => problem.Parcels
        .GroupBy(p => (p.AtHub, p.ToHub, p.DueAt, p.IsReturn, W: WeightUnits(p.WeightKg), V: VolumeUnits(p.VolumeM3)))
        .OrderBy(g => g.Key.AtHub, StringComparer.Ordinal).ThenBy(g => g.Key.ToHub, StringComparer.Ordinal).ThenBy(g => g.Key.DueAt).ThenBy(g => g.Key.W).ThenBy(g => g.Key.V)
        .Select(g => new Commodity(g.Key.AtHub, g.Key.ToHub, g.Key.DueAt, g.Key.IsReturn, g.Key.W, g.Key.V, g.OrderBy(p => p.TrackingNumber, StringComparer.Ordinal).ToList()))
        .ToList();

    /// <summary>Every way the commodity can travel inside the horizon: direct at any departure, or through one third hub.</summary>
    internal static IEnumerable<RouteOption> RouteOptions(LineHaulProblem problem, Commodity commodity)
    {
        var departures = problem.Departures.Where(d => d >= problem.Now).Order().ToList();
        var direct = problem.Lane(commodity.AtHub, commodity.ToHub);
        if (direct is not null)
        {
            foreach (var departure in departures)
            {
                yield return new RouteOption([new Hop(direct, departure)]);
            }
        }

        if (!problem.AllowVia || commodity.AtHub == commodity.ToHub)
        {
            yield break;
        }

        foreach (var first in problem.Lanes.Where(l => l.From == commodity.AtHub && l.To != commodity.ToHub))
        {
            var second = problem.Lane(first.To, commodity.ToHub);
            if (second is null || (direct is not null && first.Km + second.Km > direct.Km * problem.MaxDetour))
            {
                continue;
            }

            foreach (var leave in departures)
            {
                var ready = leave.AddHours(first.Hours + problem.HandlingHours);

                // The first two connections after arriving: waiting longer never helps the same commodity.
                foreach (var onward in departures.Where(d => d >= ready).Take(2))
                {
                    yield return new RouteOption([new Hop(first, leave), new Hop(second, onward)]);
                }
            }
        }
    }
}

internal readonly record struct Slot(string From, string To, DateTimeOffset DepartAt);

internal sealed record Hop(HaulLane Lane, DateTimeOffset DepartAt)
{
    public DateTimeOffset ArriveAt => DepartAt.AddHours(Lane.Hours);

    public Slot Slot => new(Lane.From, Lane.To, DepartAt);
}

internal sealed record RouteOption(IReadOnlyList<Hop> Hops)
{
    public DateTimeOffset ArriveAt => Hops[^1].ArriveAt;
}

internal sealed record Commodity(string AtHub, string ToHub, DateTimeOffset DueAt, bool IsReturn, long WeightUnits, long VolumeUnits, List<HaulParcel> Parcels)
{
    /// <summary>A customer's parcel counts twice a parcel going back to its seller.</summary>
    public long Priority => IsReturn ? 1 : 2;
}

/// <summary>Builds a plan's departures, routes and figures from which parcel takes which route and how many trucks leave.</summary>
internal static class LineHaulPlans
{
    public static LineHaulPlan Compose(LineHaulProblem problem, IReadOnlyDictionary<Guid, IReadOnlyList<Hop>> routes, IReadOnlyDictionary<Slot, int> trucks, SolveReport report)
    {
        var parcelRoutes = problem.Parcels.Select(p =>
        {
            if (!routes.TryGetValue(p.Id, out var hops) || hops.Count == 0)
            {
                return new ParcelRoute(p.Id, p.TrackingNumber, [], null, 0);
            }

            var arrive = hops[^1].ArriveAt;
            return new ParcelRoute(p.Id, p.TrackingNumber, hops.Select(h => new HaulLeg(h.Lane.From, h.Lane.To, h.DepartAt, h.ArriveAt)).ToList(), arrive,
                Math.Max(0, (arrive - p.DueAt).TotalHours));
        }).ToList();

        var byId = problem.Parcels.ToDictionary(p => p.Id);
        var departures = routes
            .SelectMany(r => r.Value.Select(h => (Hop: h, Parcel: byId[r.Key])))
            .GroupBy(x => x.Hop.Slot)
            .Select(g =>
            {
                var lane = g.First().Hop.Lane;
                var kg = g.Sum(x => x.Parcel.WeightKg);
                var m3 = g.Sum(x => x.Parcel.VolumeM3);
                var count = trucks.TryGetValue(g.Key, out var n) && n > 0
                    ? n
                    : Math.Max(1, (int)Math.Ceiling(Math.Max(kg / problem.Truck.CapacityKg, m3 / problem.Truck.CapacityM3) - 1e-9));
                return new PlannedDeparture(lane.From, lane.To, g.Key.DepartAt, g.Key.DepartAt.AddHours(lane.Hours), count, lane.Km, kg, m3,
                    kg / (count * problem.Truck.CapacityKg), problem.Truck.CapacityM3 > 0 ? m3 / (count * problem.Truck.CapacityM3) : 0,
                    count * problem.Truck.TripCost(lane.Km), g.Select(x => x.Parcel.Id).OrderBy(id => id).ToList());
            })
            .OrderBy(d => d.DepartAt).ThenBy(d => d.From, StringComparer.Ordinal).ThenBy(d => d.To, StringComparer.Ordinal)
            .ToList();

        var shipped = parcelRoutes.Where(r => r.Shipped).ToList();
        var kpis = new LineHaulKpis(
            departures.Sum(d => d.Trucks),
            departures.Sum(d => d.Trucks * d.Km),
            departures.Sum(d => d.Cost),
            parcelRoutes.Count,
            shipped.Count(r => r.LateHours <= 0),
            shipped.Count(r => r.LateHours > 0),
            shipped.Sum(r => r.LateHours),
            parcelRoutes.Count - shipped.Count,
            departures.Count == 0 ? 0 : departures.Average(d => d.Fill));
        return new LineHaulPlan(departures, parcelRoutes, kpis, report);
    }
}
