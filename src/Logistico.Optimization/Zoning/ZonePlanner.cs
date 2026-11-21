using System.Diagnostics;
using System.Globalization;
using Google.OrTools.Sat;

namespace Logistico.Optimization.Zoning;

/// <summary>An area a hub delivers to, with its workload (deliveries per week) and where it is.</summary>
public sealed record ZoneUnit(string Key, string Label, string Governorate, GeoPoint Point, PointSource Source, double Workload);

/// <summary>Split a hub's areas into <see cref="Zones"/> courier zones, each within ±<see cref="Tolerance"/> of the average workload.</summary>
public sealed record ZoningProblem(IReadOnlyList<ZoneUnit> Units, int Zones, double Tolerance = 0.15, double RoadFactor = 1.3);

public sealed record PlannedZone(int Number, string CenterKey, IReadOnlyList<string> UnitKeys, double Workload, double Share, double AverageKm);

/// <summary>The zones, the balance tolerance that could be met (it is widened when one area alone outweighs a zone), and the travel they imply.</summary>
public sealed record ZoningPlan(IReadOnlyList<PlannedZone> Zones, double Tolerance, double MaxDeviation, double WeightedKm, SolveReport Report);

/// <summary>
/// Courier zones as balanced districting (Kalcsics &amp; Ríos-Mercado 2019; docs/20 §3.3): a capacitated p-median on
/// OR-Tools CP-SAT. Each area joins one zone whose centre is one of the areas; every zone's workload stays within the
/// tolerance of the average; and the workload-weighted distance from areas to their centre (compactness) is minimised.
/// </summary>
public static class ZonePlanner
{
    public const string SolverName = "OR-Tools CP-SAT";

    /// <summary>Centres are chosen among the busiest areas when a hub has very many (keeps the model small).</summary>
    public const int MaxCandidates = 120;

    /// <summary>The total workload in the solver's integers (see the balance constraints).</summary>
    internal const double BalanceUnits = 10_000_000;

    public static ZoningPlan Solve(ZoningProblem problem, SolveOptions? options = null)
    {
        options ??= SolveOptions.Default;
        var clock = Stopwatch.StartNew();
        var units = problem.Units.OrderBy(u => u.Key, StringComparer.Ordinal).ToList();
        var zones = Math.Min(problem.Zones, units.Count);
        if (units.Count == 0 || zones <= 0)
        {
            return new ZoningPlan([], problem.Tolerance, 0, 0, SolveReport.Nothing(SolverName));
        }

        var tolerance = problem.Tolerance;
        var notes = new List<string>();
        for (var attempt = 0; attempt < 6; attempt++, tolerance = Math.Min(1, tolerance * 1.5 + 0.05))
        {
            var result = TrySolve(problem, units, zones, tolerance, options, clock);
            if (result is not null)
            {
                if (attempt > 0)
                {
                    notes.Add(string.Create(CultureInfo.InvariantCulture, $"tolerance widened from {problem.Tolerance:P0} to {tolerance:P0}"));
                }

                return result with { Report = result.Report with { Notes = notes, Seconds = clock.Elapsed.TotalSeconds } };
            }

            if (tolerance >= 1)
            {
                break;
            }
        }

        return new ZoningPlan([], tolerance, 0, 0, new SolveReport(SolveStatus.Infeasible, 0, null, clock.Elapsed.TotalSeconds, SolverName, options.Seed, notes));
    }

    private static ZoningPlan? TrySolve(ZoningProblem problem, List<ZoneUnit> units, int zones, double tolerance, SolveOptions options, Stopwatch clock)
    {
        var n = units.Count;

        // Balance is exact in real workloads, not only in the solver's integers: workloads are scaled so the total is ten
        // million, and the bounds are pulled in by the worst rounding a zone can collect (half a unit per area). Rounding
        // to tenths instead let zones of small areas (0.1–0.8 deliveries a week) pass the model and break the tolerance.
        var total = units.Sum(u => u.Workload);
        var scale = BalanceUnits / total;
        var load = units.Select(u => (long)Math.Round(u.Workload * scale)).ToArray();
        var average = BalanceUnits / zones;
        var slack = (long)Math.Ceiling(n / 2.0) + 1;
        var low = (long)Math.Ceiling((1 - tolerance) * average) + slack;
        var high = (long)Math.Floor((1 + tolerance) * average) - slack;
        if (low > high || load.Max() > high)
        {
            return null; // one area alone is more than a zone may carry
        }

        // Compactness weighs each area by its share of the work (in ten-thousandths, at least one).
        var weight = units.Select(u => Math.Max(1, (long)Math.Round(u.Workload / total * 10_000))).ToArray();

        var candidates = Enumerable.Range(0, n).OrderByDescending(i => load[i]).ThenBy(i => units[i].Key, StringComparer.Ordinal).Take(MaxCandidates).Order().ToList();
        var distance = new long[n, n];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                distance[i, j] = (long)Math.Round(units[i].Point.KmTo(units[j].Point) * problem.RoadFactor * 10); // 100 m
            }
        }

        var model = new CpModel();
        var open = candidates.ToDictionary(m => m, m => model.NewBoolVar($"y_{m}"));
        var assign = new Dictionary<(int Unit, int Centre), BoolVar>();
        for (var u = 0; u < n; u++)
        {
            foreach (var m in candidates)
            {
                var x = model.NewBoolVar($"x_{u}_{m}");
                assign[(u, m)] = x;
                model.AddImplication(x, open[m]);
            }

            model.AddExactlyOne(candidates.Select(m => (ILiteral)assign[(u, m)]));
        }

        model.Add(LinearExpr.Sum(open.Values) == zones);
        foreach (var m in candidates)
        {
            model.AddImplication(open[m], assign[(m, m)]); // a centre belongs to its own zone
            var zoneLoad = LinearExpr.NewBuilder();
            for (var u = 0; u < n; u++)
            {
                zoneLoad.AddTerm(assign[(u, m)], load[u]);
            }

            model.Add(zoneLoad >= LinearExpr.Term(open[m], low));
            model.Add(zoneLoad <= LinearExpr.Term(open[m], high));
        }

        var objective = LinearExpr.NewBuilder();
        foreach (var ((u, m), x) in assign)
        {
            objective.AddTerm(x, weight[u] * distance[u, m]);
        }

        model.Minimize(objective);
        var solver = new CpSolver
        {
            StringParameters = string.Create(CultureInfo.InvariantCulture,
                $"max_time_in_seconds:{Math.Max(0.5, options.TimeLimitSeconds - clock.Elapsed.TotalSeconds):0.###} num_workers:{Math.Max(1, options.Workers)} random_seed:{options.Seed}"),
        };
        var status = solver.Solve(model);
        if (status is CpSolverStatus.Infeasible or CpSolverStatus.ModelInvalid)
        {
            return null;
        }

        if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
        {
            return new ZoningPlan([], tolerance, 0, 0, new SolveReport(SolveStatus.NoSolution, 0, null, 0, SolverName, options.Seed, []));
        }

        var planned = candidates.Where(m => solver.BooleanValue(open[m]))
            .Select(m =>
            {
                var members = Enumerable.Range(0, n).Where(u => solver.BooleanValue(assign[(u, m)])).ToList();
                var workload = members.Sum(u => units[u].Workload);
                var km = workload <= 0 ? 0 : members.Sum(u => units[u].Workload * units[u].Point.KmTo(units[m].Point) * problem.RoadFactor) / workload;
                return (Centre: units[m], Members: members.Select(u => units[u].Key).ToList(), Workload: workload, Km: km);
            })
            .OrderByDescending(z => z.Workload).ThenBy(z => z.Centre.Key, StringComparer.Ordinal)
            .ToList();
        var mean = units.Sum(u => u.Workload) / zones;
        var result = planned.Select((z, i) => new PlannedZone(i + 1, z.Centre.Key, z.Members, z.Workload, mean <= 0 ? 0 : z.Workload / mean, z.Km)).ToList();
        var deviation = result.Count == 0 ? 0 : result.Max(z => Math.Abs(z.Share - 1));
        var weightedKm = result.Sum(z => z.AverageKm * z.Workload);
        var report = new SolveReport(status == CpSolverStatus.Optimal ? SolveStatus.Optimal : SolveStatus.Feasible,
            solver.ObjectiveValue, solver.BestObjectiveBound, clock.Elapsed.TotalSeconds, SolverName, options.Seed, []);
        return new ZoningPlan(result, tolerance, deviation, weightedKm, report);
    }
}

/// <summary>Re-checks zones from the problem alone: every area in exactly one zone, the zone count, and the balance claimed.</summary>
public static class ZoningChecker
{
    public static IReadOnlyList<Violation> Check(ZoningProblem problem, ZoningPlan plan)
    {
        var problems = new List<Violation>();
        if (plan.Report.Status is SolveStatus.Infeasible or SolveStatus.NoSolution or SolveStatus.Empty)
        {
            return problems;
        }

        var units = problem.Units.ToDictionary(u => u.Key);
        var counts = plan.Zones.SelectMany(z => z.UnitKeys).GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        foreach (var unit in problem.Units.Where(u => counts.GetValueOrDefault(u.Key) != 1))
        {
            problems.Add(new(counts.ContainsKey(unit.Key) ? "unit.duplicate" : "unit.missing", unit.Key, unit.Label));
        }

        foreach (var key in counts.Keys.Where(k => !units.ContainsKey(k)))
        {
            problems.Add(new("unit.unknown", key, "not in the problem"));
        }

        var expected = Math.Min(problem.Zones, problem.Units.Count);
        if (plan.Zones.Count != expected)
        {
            problems.Add(new("plan.zone_count", "plan", $"{plan.Zones.Count} zones, {expected} asked"));
        }

        var mean = problem.Units.Sum(u => u.Workload) / Math.Max(1, expected);
        foreach (var zone in plan.Zones)
        {
            var workload = zone.UnitKeys.Where(units.ContainsKey).Sum(k => units[k].Workload);
            if (!zone.UnitKeys.Contains(zone.CenterKey))
            {
                problems.Add(new("zone.centre", $"zone {zone.Number}", "its centre is not one of its areas"));
            }

            if (Math.Abs(workload - zone.Workload) > 1e-6)
            {
                problems.Add(new("zone.workload", $"zone {zone.Number}", "reported workload differs"));
            }

            if (mean > 0 && Math.Abs(workload / mean - 1) > plan.Tolerance + 1e-9)
            {
                problems.Add(new("zone.balance", $"zone {zone.Number}", $"{workload / mean:P0} of the average"));
            }
        }

        return problems;
    }
}
