namespace Logistico.Optimization.LineHaul;

/// <summary>
/// Re-checks a line-haul plan from the problem alone, without trusting the solver that made it: every parcel exactly
/// once, every leg on a real lane and departure, connections that leave time to change trucks, loads within the trucks,
/// truck limits, and the cost and lateness the plan reports.
/// </summary>
public static class LineHaulChecker
{
    private const double Tolerance = 1e-6;

    public static IReadOnlyList<Violation> Check(LineHaulProblem problem, LineHaulPlan plan)
    {
        var problems = new List<Violation>();
        var parcels = problem.Parcels.ToDictionary(p => p.Id);
        var departures = new HashSet<DateTimeOffset>(problem.Departures);

        foreach (var group in plan.Routes.GroupBy(r => r.ParcelId).Where(g => g.Count() > 1))
        {
            problems.Add(new("parcel.duplicate", group.First().TrackingNumber, $"{group.Count()} routes"));
        }

        foreach (var missing in parcels.Values.Where(p => plan.Routes.All(r => r.ParcelId != p.Id)))
        {
            problems.Add(new("parcel.missing", missing.TrackingNumber, "no route"));
        }

        var legsBySlot = new Dictionary<(string, string, DateTimeOffset), HashSet<Guid>>();
        foreach (var route in plan.Routes)
        {
            if (!parcels.TryGetValue(route.ParcelId, out var parcel))
            {
                problems.Add(new("parcel.unknown", route.TrackingNumber, "not in the problem"));
                continue;
            }

            if (!route.Shipped)
            {
                continue;
            }

            if (route.Legs.Count > (problem.AllowVia ? 2 : 1))
            {
                problems.Add(new("route.too_many_legs", parcel.TrackingNumber, $"{route.Legs.Count} legs"));
            }

            if (route.Legs[0].From != parcel.AtHub)
            {
                problems.Add(new("route.start", parcel.TrackingNumber, $"{route.Legs[0].From} ≠ {parcel.AtHub}"));
            }

            if (route.Legs[^1].To != parcel.ToHub)
            {
                problems.Add(new("route.end", parcel.TrackingNumber, $"{route.Legs[^1].To} ≠ {parcel.ToHub}"));
            }

            for (var i = 0; i < route.Legs.Count; i++)
            {
                var leg = route.Legs[i];
                var lane = problem.Lane(leg.From, leg.To);
                if (lane is null)
                {
                    problems.Add(new("route.no_lane", parcel.TrackingNumber, $"{leg.From}>{leg.To}"));
                    continue;
                }

                if (!departures.Contains(leg.DepartAt) || leg.DepartAt < problem.Now)
                {
                    problems.Add(new("route.departure", parcel.TrackingNumber, $"{leg.From}>{leg.To} at {leg.DepartAt:O}"));
                }

                if (Math.Abs((leg.ArriveAt - leg.DepartAt.AddHours(lane.Hours)).TotalMinutes) > 1)
                {
                    problems.Add(new("route.arrival", parcel.TrackingNumber, $"{leg.From}>{leg.To}"));
                }

                if (i > 0 && (leg.From != route.Legs[i - 1].To || leg.DepartAt < route.Legs[i - 1].ArriveAt.AddHours(problem.HandlingHours)))
                {
                    problems.Add(new("route.connection", parcel.TrackingNumber, $"leg {i + 1}"));
                }

                var key = (leg.From, leg.To, leg.DepartAt);
                if (!legsBySlot.TryGetValue(key, out var onSlot))
                {
                    legsBySlot[key] = onSlot = [];
                }

                onSlot.Add(parcel.Id);
            }

            var arrive = route.Legs[^1].ArriveAt;
            if (route.ArriveAt != arrive || Math.Abs(route.LateHours - Math.Max(0, (arrive - parcel.DueAt).TotalHours)) > 0.01)
            {
                problems.Add(new("route.lateness", parcel.TrackingNumber, $"{route.LateHours:0.##} h reported"));
            }
        }

        foreach (var departure in plan.Departures)
        {
            var subject = $"{departure.From}>{departure.To} {departure.DepartAt:O}";
            var lane = problem.Lane(departure.From, departure.To);
            legsBySlot.TryGetValue((departure.From, departure.To, departure.DepartAt), out var expected);
            var carried = departure.ParcelIds.ToHashSet();
            if (expected is null || !carried.SetEquals(expected))
            {
                problems.Add(new("departure.manifest", subject, "parcels differ from the routes"));
            }

            if (departure.Trucks < 1 || departure.Trucks > problem.MaxTrucksPerDeparture)
            {
                problems.Add(new("departure.trucks", subject, $"{departure.Trucks} trucks"));
            }

            var load = carried.Where(parcels.ContainsKey).Select(id => parcels[id]).ToList();
            var kg = load.Sum(p => p.WeightKg);
            var m3 = load.Sum(p => p.VolumeM3);
            if (kg > departure.Trucks * problem.Truck.CapacityKg + Tolerance || m3 > departure.Trucks * problem.Truck.CapacityM3 + Tolerance)
            {
                problems.Add(new("departure.overloaded", subject, $"{kg:0.#} kg, {m3:0.###} m³ on {departure.Trucks} trucks"));
            }

            if (Math.Abs(kg - departure.LoadKg) > 0.001 || Math.Abs(m3 - departure.LoadM3) > 0.000_01)
            {
                problems.Add(new("departure.load", subject, "reported load differs"));
            }

            if (lane is not null && departure.Cost != departure.Trucks * problem.Truck.TripCost(lane.Km))
            {
                problems.Add(new("departure.cost", subject, $"{departure.Cost} reported"));
            }
        }

        foreach (var orphan in legsBySlot.Keys.Where(k => !plan.Departures.Any(d => (d.From, d.To, d.DepartAt) == k)))
        {
            problems.Add(new("departure.missing", $"{orphan.Item1}>{orphan.Item2} {orphan.Item3:O}", "a route uses a departure the plan does not have"));
        }

        if (problem.MaxTrucksPerHubPerDeparture > 0)
        {
            foreach (var hub in plan.Departures.GroupBy(d => (d.From, d.DepartAt)).Where(g => g.Sum(d => d.Trucks) > problem.MaxTrucksPerHubPerDeparture))
            {
                problems.Add(new("hub.fleet", $"{hub.Key.From} {hub.Key.DepartAt:O}", $"{hub.Sum(d => d.Trucks)} trucks"));
            }
        }

        var shipped = plan.Routes.Where(r => r.Shipped).ToList();
        if (plan.Kpis.Cost != plan.Departures.Sum(d => d.Cost) || plan.Kpis.Trucks != plan.Departures.Sum(d => d.Trucks)
            || plan.Kpis.Unshipped != plan.Routes.Count - shipped.Count || plan.Kpis.Late != shipped.Count(r => r.LateHours > 0))
        {
            problems.Add(new("plan.figures", "plan", "reported figures differ from the plan"));
        }

        return problems;
    }
}
