namespace Logistico.Optimization.Routing;

/// <summary>
/// Today's practice, as the yardstick: parcels due today first, sorted by governorate and area (the way a dispatcher
/// splits the shelf), each given to the first courier who can still take it, visited in that order.
/// </summary>
public static class CourierRunBaseline
{
    public const string SolverName = "baseline";

    public static CourierRunPlan Plan(CourierRunProblem problem)
    {
        var matrix = TravelMatrix.Build(problem);
        var runs = problem.Couriers.Select(_ => new List<int>()).ToList();
        var kg = new double[problem.Couriers.Count];
        var cash = new decimal[problem.Couriers.Count];
        var order = problem.Stops
            .Select((stop, i) => (stop, node: i + 1))
            .OrderByDescending(x => x.stop.MustGo)
            .ThenBy(x => x.stop.Governorate, StringComparer.Ordinal)
            .ThenBy(x => x.stop.Area, StringComparer.Ordinal)
            .ThenBy(x => matrix.Km(0, x.node))
            .ThenBy(x => x.stop.TrackingNumber, StringComparer.Ordinal);

        foreach (var (stop, node) in order)
        {
            for (var v = 0; v < problem.Couriers.Count; v++)
            {
                var courier = problem.Couriers[v];
                var trial = runs[v].Append(node).ToList();
                if (trial.Count <= courier.MaxStops && kg[v] + stop.WeightKg <= courier.MaxKg + 1e-9 && cash[v] + stop.Cod <= courier.MaxCash
                    && CourierRuns.Seconds(problem, matrix, trial) <= courier.ShiftMinutes * 60L)
                {
                    runs[v].Add(node);
                    kg[v] += stop.WeightKg;
                    cash[v] += stop.Cod;
                    break;
                }
            }
        }

        var plan = CourierRuns.Compose(problem, matrix, runs.Select((nodes, v) => (v, (IReadOnlyList<int>)nodes)).ToList(), SolveReport.Nothing(SolverName));
        return plan with { Report = new SolveReport(SolveStatus.Feasible, plan.Kpis.Km, null, 0, SolverName, 0, []) };
    }
}
