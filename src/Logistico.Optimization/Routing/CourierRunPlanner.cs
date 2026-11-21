using System.Diagnostics;
using Google.OrTools.ConstraintSolver;
using Google.Protobuf.WellKnownTypes;

namespace Logistico.Optimization.Routing;

/// <summary>
/// Morning courier runs as a rich vehicle routing problem (Toth &amp; Vigo 2014; docs/20 §3.2), on the OR-Tools routing
/// library: a heterogeneous fleet with limits on parcels, kilograms, cash carried (a safety limit particular to
/// cash-on-delivery in Iraq) and working time; runs start and end at the hub, where the cash is handed in. Parcels due
/// today are dropped only when nobody can take them; parcels due later go only where they cost little. The first plan
/// comes from Clarke &amp; Wright's savings (1964) and is improved by guided local search within the time limit.
/// </summary>
public static class CourierRunPlanner
{
    public const string SolverName = "OR-Tools routing (savings + guided local search)";

    /// <summary>Metres of driving a dropped parcel costs: due today is worth a thousand kilometres, due later twenty.</summary>
    private const long MustGoPenalty = 1_000_000_000;
    private const long LateDayPenalty = 100_000_000;
    private const long OptionalPenalty = 20_000;

    public static CourierRunPlan Solve(CourierRunProblem problem, SolveOptions? options = null)
    {
        options ??= SolveOptions.Default;
        var clock = Stopwatch.StartNew();
        var matrix = TravelMatrix.Build(problem);
        if (problem.Stops.Count == 0)
        {
            return CourierRuns.Compose(problem, matrix, [], SolveReport.Nothing(SolverName));
        }

        if (problem.Couriers.Count == 0)
        {
            return CourierRuns.Compose(problem, matrix, [], new SolveReport(SolveStatus.Infeasible, 0, null, 0, SolverName, options.Seed, ["no courier"]));
        }

        var n = problem.Stops.Count + 1;
        var vehicles = problem.Couriers.Count;
        var manager = new RoutingIndexManager(n, vehicles, 0);
        var routing = new RoutingModel(manager);

        var metres = new long[n, n];
        var seconds = new long[n, n];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                metres[i, j] = (long)Math.Round(matrix.Km(i, j) * 1000);
                seconds[i, j] = matrix.Seconds(i, j) + (i == 0 ? 0 : problem.ServiceMinutes * 60L);
            }
        }

        // The delegates must outlive the search: OR-Tools calls back into them from native code.
        LongLongToLong distance = (from, to) => metres[manager.IndexToNode(from), manager.IndexToNode(to)];
        LongLongToLong time = (from, to) => seconds[manager.IndexToNode(from), manager.IndexToNode(to)];
        LongToLong one = from => manager.IndexToNode(from) == 0 ? 0 : 1;
        LongToLong weight = from => manager.IndexToNode(from) is var node and > 0 ? WeightUnits(problem.Stops[node - 1].WeightKg) : 0;
        LongToLong cash = from => manager.IndexToNode(from) is var node and > 0 ? CashUnits(problem.Stops[node - 1].Cod) : 0;

        var distanceIndex = routing.RegisterTransitCallback(distance);
        routing.SetArcCostEvaluatorOfAllVehicles(distanceIndex);
        routing.AddDimensionWithVehicleCapacity(routing.RegisterTransitCallback(time), 0, problem.Couriers.Select(c => c.ShiftMinutes * 60L).ToArray(), true, "Time");
        routing.AddDimensionWithVehicleCapacity(routing.RegisterUnaryTransitCallback(one), 0, problem.Couriers.Select(c => (long)c.MaxStops).ToArray(), true, "Stops");
        routing.AddDimensionWithVehicleCapacity(routing.RegisterUnaryTransitCallback(weight), 0, problem.Couriers.Select(c => WeightCapacity(c.MaxKg)).ToArray(), true, "Weight");
        routing.AddDimensionWithVehicleCapacity(routing.RegisterUnaryTransitCallback(cash), 0, problem.Couriers.Select(c => CashCapacity(c.MaxCash)).ToArray(), true, "Cash");

        // Balance: the longest run costs a little extra, so work is spread instead of piled on one courier.
        if (problem.BalanceWeight > 0)
        {
            routing.GetMutableDimension("Time").SetGlobalSpanCostCoefficient((long)Math.Round(problem.BalanceWeight));
        }

        for (var node = 1; node < n; node++)
        {
            var stop = problem.Stops[node - 1];
            var penalty = stop.MustGo ? MustGoPenalty + Math.Max(0, stop.DaysLate) * LateDayPenalty : OptionalPenalty;
            routing.AddDisjunction([manager.NodeToIndex(node)], penalty);
        }

        var search = operations_research_constraint_solver.DefaultRoutingSearchParameters();
        search.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.Savings;
        search.LocalSearchMetaheuristic = LocalSearchMetaheuristic.Types.Value.GuidedLocalSearch;
        search.TimeLimit = Duration.FromTimeSpan(TimeSpan.FromSeconds(Math.Max(0.5, options.TimeLimitSeconds)));

        var solution = routing.SolveWithParameters(search);
        GC.KeepAlive(distance);
        GC.KeepAlive(time);
        GC.KeepAlive(one);
        GC.KeepAlive(weight);
        GC.KeepAlive(cash);

        if (solution is null)
        {
            var fallback = CourierRunBaseline.Plan(problem);
            return fallback with { Report = fallback.Report with { Status = SolveStatus.NoSolution, Solver = SolverName, Seconds = clock.Elapsed.TotalSeconds } };
        }

        var routes = new List<(int Courier, IReadOnlyList<int> Nodes)>();
        for (var v = 0; v < vehicles; v++)
        {
            var nodes = new List<int>();
            for (var index = solution.Value(routing.NextVar(routing.Start(v))); !routing.IsEnd(index); index = solution.Value(routing.NextVar(index)))
            {
                nodes.Add(manager.IndexToNode(index));
            }

            routes.Add((v, nodes));
        }

        var plan = CourierRuns.Compose(problem, matrix, routes, SolveReport.Nothing(SolverName));
        return plan with { Report = new SolveReport(SolveStatus.Feasible, plan.Kpis.Km, null, clock.Elapsed.TotalSeconds, SolverName, options.Seed, []) };
    }

    internal static long WeightUnits(double kg) => Math.Max(0, (long)Math.Ceiling(kg * 10 - 1e-9));

    internal static long WeightCapacity(double kg) => (long)Math.Floor(kg * 10 + 1e-9);

    internal static long CashUnits(decimal iqd) => (long)Math.Ceiling(Math.Max(0, iqd));

    internal static long CashCapacity(decimal iqd) => (long)Math.Floor(Math.Max(0, iqd));
}

/// <summary>Builds runs, figures and the reasons parcels stay at the hub, from which courier visits which stops in which order.</summary>
internal static class CourierRuns
{
    public static CourierRunPlan Compose(CourierRunProblem problem, TravelMatrix matrix, IReadOnlyList<(int Courier, IReadOnlyList<int> Nodes)> routes, SolveReport report)
    {
        var built = problem.Couriers.Select((courier, v) =>
        {
            var nodes = routes.FirstOrDefault(r => r.Courier == v).Nodes ?? [];
            return Route(problem, matrix, courier, nodes);
        }).ToList();

        var served = routes.SelectMany(r => r.Nodes).ToHashSet();
        var nodeOf = problem.Stops.Select((stop, i) => (stop.Id, Node: i + 1)).ToDictionary(x => x.Id, x => x.Node);
        var deferred = problem.Stops
            .Select((stop, i) => (stop, node: i + 1))
            .Where(x => !served.Contains(x.node))
            .Select(x => new DeferredStop(x.stop.Id, x.stop.TrackingNumber, x.stop.MustGo, ReasonFor(problem, matrix, built, nodeOf, x.node)))
            .ToList();

        var used = built.Where(r => r.Stops.Count > 0).ToList();
        var kpis = new CourierRunKpis(
            used.Sum(r => r.Stops.Count),
            deferred.Count,
            deferred.Count(d => d.MustGo),
            built.Sum(r => r.Km),
            built.Count == 0 ? 0 : built.Max(r => r.Minutes),
            used.Count,
            used.Count == 0 ? 0 : used.Max(r => r.Stops.Count) - used.Min(r => r.Stops.Count));
        return new CourierRunPlan(built, deferred, kpis, report);
    }

    public static CourierRoute Route(CourierRunProblem problem, TravelMatrix matrix, RouteCourier courier, IReadOnlyList<int> nodes)
    {
        var stops = new List<RoutedStop>(nodes.Count);
        long seconds = 0;
        double km = 0;
        decimal cash = 0;
        double kg = 0;
        var previous = 0;
        foreach (var node in nodes)
        {
            seconds += matrix.Seconds(previous, node);
            km += matrix.Km(previous, node);
            var stop = problem.Stops[node - 1];
            cash += stop.Cod;
            kg += stop.WeightKg;
            stops.Add(new RoutedStop(stop.Id, stop.TrackingNumber, stops.Count + 1, problem.StartAt.AddSeconds(seconds), km, cash, kg));
            seconds += problem.ServiceMinutes * 60L;
            previous = node;
        }

        if (nodes.Count > 0)
        {
            seconds += matrix.Seconds(previous, 0);
            km += matrix.Km(previous, 0);
        }

        return new CourierRoute(courier.Label, stops, km, (int)Math.Ceiling(seconds / 60.0), cash, kg);
    }

    /// <summary>Total working seconds of a run that visits <paramref name="nodes"/> in this order.</summary>
    public static long Seconds(CourierRunProblem problem, TravelMatrix matrix, IReadOnlyList<int> nodes)
    {
        long seconds = 0;
        var previous = 0;
        foreach (var node in nodes)
        {
            seconds += matrix.Seconds(previous, node) + problem.ServiceMinutes * 60L;
            previous = node;
        }

        return nodes.Count == 0 ? 0 : seconds + matrix.Seconds(previous, 0);
    }

    /// <summary>
    /// The limit that keeps a parcel off every run: for the run that could take it most easily (cheapest insertion), the
    /// first limit it would break. A parcel due later that would fit is simply not needed today.
    /// </summary>
    private static DeferReason ReasonFor(CourierRunProblem problem, TravelMatrix matrix, IReadOnlyList<CourierRoute> routes, IReadOnlyDictionary<Guid, int> nodeOf, int node)
    {
        if (problem.Couriers.Count == 0)
        {
            return DeferReason.NoCourier;
        }

        var stop = problem.Stops[node - 1];
        var best = DeferReason.Stops;
        var bestBroken = int.MaxValue;
        for (var v = 0; v < problem.Couriers.Count; v++)
        {
            var courier = problem.Couriers[v];
            var route = routes[v];
            var nodes = route.Stops.Select(s => nodeOf[s.StopId]).ToList();
            var cheapest = long.MaxValue;
            for (var at = 0; at <= nodes.Count; at++)
            {
                var trial = nodes.ToList();
                trial.Insert(at, node);
                cheapest = Math.Min(cheapest, Seconds(problem, matrix, trial));
            }

            var broken = new List<DeferReason>();
            if (route.Stops.Count + 1 > courier.MaxStops)
            {
                broken.Add(DeferReason.Stops);
            }

            if (route.Kg + stop.WeightKg > courier.MaxKg + 1e-9)
            {
                broken.Add(DeferReason.Weight);
            }

            if (route.Cash + stop.Cod > courier.MaxCash)
            {
                broken.Add(DeferReason.Cash);
            }

            if (cheapest > courier.ShiftMinutes * 60L)
            {
                broken.Add(DeferReason.Time);
            }

            if (broken.Count == 0)
            {
                return stop.MustGo ? DeferReason.Time : DeferReason.NotNeededToday;
            }

            if (broken.Count < bestBroken)
            {
                bestBroken = broken.Count;
                best = broken[0];
            }
        }

        return best;
    }
}
