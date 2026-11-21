namespace Logistico.Optimization.Routing;

/// <summary>
/// Re-checks courier runs from the problem alone: every parcel on one run or deferred (never both, never twice), each
/// courier within their parcels, kilograms, cash and working time, and the times and distances the plan reports.
/// </summary>
public static class CourierRunChecker
{
    public static IReadOnlyList<Violation> Check(CourierRunProblem problem, CourierRunPlan plan)
    {
        var problems = new List<Violation>();
        var matrix = TravelMatrix.Build(problem);
        var nodeOf = problem.Stops.Select((stop, i) => (stop, node: i + 1)).ToDictionary(x => x.stop.Id, x => x.node);
        var seen = new Dictionary<Guid, int>();

        foreach (var route in plan.Routes)
        {
            var courier = problem.Couriers.FirstOrDefault(c => c.Label == route.Courier);
            if (courier is null)
            {
                problems.Add(new("route.unknown_courier", route.Courier, "not in the problem"));
                continue;
            }

            var nodes = new List<int>();
            foreach (var stop in route.Stops)
            {
                seen[stop.StopId] = seen.GetValueOrDefault(stop.StopId) + 1;
                if (nodeOf.TryGetValue(stop.StopId, out var node))
                {
                    nodes.Add(node);
                }
                else
                {
                    problems.Add(new("stop.unknown", stop.TrackingNumber, route.Courier));
                }
            }

            var recomputed = CourierRuns.Route(problem, matrix, courier, nodes);
            if (nodes.Count > courier.MaxStops)
            {
                problems.Add(new("route.stops", route.Courier, $"{nodes.Count} > {courier.MaxStops}"));
            }

            if (recomputed.Kg > courier.MaxKg + 1e-9)
            {
                problems.Add(new("route.weight", route.Courier, $"{recomputed.Kg:0.#} kg > {courier.MaxKg:0.#}"));
            }

            if (recomputed.Cash > courier.MaxCash)
            {
                problems.Add(new("route.cash", route.Courier, $"{recomputed.Cash:0} > {courier.MaxCash:0}"));
            }

            if (CourierRuns.Seconds(problem, matrix, nodes) > courier.ShiftMinutes * 60L)
            {
                problems.Add(new("route.time", route.Courier, $"{recomputed.Minutes} min > {courier.ShiftMinutes}"));
            }

            if (Math.Abs(recomputed.Km - route.Km) > 0.01 || recomputed.Minutes != route.Minutes || recomputed.Cash != route.Cash
                || route.Stops.Select((s, i) => s.Sequence != i + 1 || s.ArriveAt != recomputed.Stops[i].ArriveAt).Any(bad => bad))
            {
                problems.Add(new("route.figures", route.Courier, "reported times or distances differ"));
            }
        }

        foreach (var deferred in plan.Deferred)
        {
            seen[deferred.StopId] = seen.GetValueOrDefault(deferred.StopId) + 1;
        }

        foreach (var stop in problem.Stops)
        {
            var count = seen.GetValueOrDefault(stop.Id);
            if (count != 1)
            {
                problems.Add(new(count == 0 ? "stop.missing" : "stop.duplicate", stop.TrackingNumber, $"{count} times"));
            }
        }

        if (plan.Kpis.Planned != plan.Routes.Sum(r => r.Stops.Count) || plan.Kpis.Deferred != plan.Deferred.Count
            || plan.Kpis.MustGoDeferred != plan.Deferred.Count(d => d.MustGo))
        {
            problems.Add(new("plan.figures", "plan", "reported figures differ from the plan"));
        }

        return problems;
    }
}
