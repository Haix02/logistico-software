namespace Logistico.Optimization.LineHaul;

/// <summary>
/// Today's practice, as the yardstick every optimised plan is compared with: each lane with parcels waiting sends
/// direct trucks at the first departure, as many as the load needs (within the limits), most urgent parcels first;
/// what does not fit waits for the next departure.
/// </summary>
public static class LineHaulBaseline
{
    public const string SolverName = "baseline";

    public static LineHaulPlan Plan(LineHaulProblem problem)
    {
        var routes = new Dictionary<Guid, IReadOnlyList<Hop>>();
        var trucks = new Dictionary<Slot, int>();
        var waiting = problem.Parcels
            .Where(p => problem.Lane(p.AtHub, p.ToHub) is not null)
            .GroupBy(p => (p.AtHub, p.ToHub))
            .ToDictionary(g => g.Key, g => new Queue<HaulParcel>(g.OrderBy(p => p.DueAt).ThenBy(p => p.TrackingNumber, StringComparer.Ordinal)));

        foreach (var departure in problem.Departures.Where(d => d >= problem.Now).Order())
        {
            var usedAtHub = new Dictionary<string, int>();

            // The lane with the most urgent parcel gets the hub's trucks first.
            foreach (var (lane, queue) in waiting.Where(w => w.Value.Count > 0).OrderBy(w => w.Value.Peek().DueAt).ThenBy(w => w.Key.AtHub, StringComparer.Ordinal).ThenBy(w => w.Key.ToHub, StringComparer.Ordinal))
            {
                var hubLeft = problem.MaxTrucksPerHubPerDeparture > 0 ? problem.MaxTrucksPerHubPerDeparture - usedAtHub.GetValueOrDefault(lane.AtHub) : int.MaxValue;
                var allowed = Math.Min(problem.MaxTrucksPerDeparture, hubLeft);
                if (allowed <= 0)
                {
                    continue;
                }

                var road = problem.Lane(lane.AtHub, lane.ToHub)!;
                var hop = new Hop(road, departure);
                double kg = 0, m3 = 0;
                while (queue.Count > 0
                    && kg + queue.Peek().WeightKg <= allowed * problem.Truck.CapacityKg
                    && m3 + queue.Peek().VolumeM3 <= allowed * problem.Truck.CapacityM3)
                {
                    var parcel = queue.Dequeue();
                    kg += parcel.WeightKg;
                    m3 += parcel.VolumeM3;
                    routes[parcel.Id] = [hop];
                }

                if (kg > 0 || m3 > 0)
                {
                    var needed = Math.Max(1, (int)Math.Ceiling(Math.Max(kg / problem.Truck.CapacityKg, problem.Truck.CapacityM3 > 0 ? m3 / problem.Truck.CapacityM3 : 0) - 1e-9));
                    trucks[hop.Slot] = needed;
                    usedAtHub[lane.AtHub] = usedAtHub.GetValueOrDefault(lane.AtHub) + needed;
                }
            }
        }

        var plan = LineHaulPlans.Compose(problem, routes, trucks, SolveReport.Nothing(SolverName));
        return plan with { Report = new SolveReport(SolveStatus.Feasible, (double)plan.Kpis.Cost, null, 0, SolverName, 0, []) };
    }
}
