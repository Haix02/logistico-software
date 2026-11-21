using Logistico.Optimization.LineHaul;

namespace Logistico.Optimization.Tests;

/// <summary>
/// The truck planner is proven, not trusted: small instances are solved by brute force and must match exactly, and every
/// plan passes the independent checker.
/// </summary>
public class LineHaulPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 5, 0, 0, TimeSpan.Zero); // Monday 08:00 in Baghdad
    private static readonly DateTimeOffset[] Departures = [Now.AddHours(1), Now.AddHours(9), Now.AddHours(17)];
    private static readonly TruckType Truck = new(CapacityKg: 100, CapacityM3: 10, FixedCost: 50_000, CostPerKm: 500);
    private static readonly SolveOptions Fast = new(TimeLimitSeconds: 10, Seed: 7, Workers: 4);

    private static Guid Id(int n) => new(n, 0, 0, new byte[8]);

    private static HaulParcel Parcel(int n, string at, string to, double kg, double dueHours, bool isReturn = false) =>
        new(Id(n), $"LX{n:D11}", at, to, kg, 0, Now.AddHours(dueHours), isReturn);

    private static LineHaulProblem Problem(IReadOnlyList<HaulParcel> parcels, IReadOnlyList<HaulLane>? lanes = null, TruckType? truck = null,
        int maxTrucks = 3, int hubLimit = 0, bool via = false, IReadOnlyList<DateTimeOffset>? departures = null) =>
        new(Now, departures ?? Departures, lanes ?? [new HaulLane("A", "B", 300, 5)], parcels, truck ?? Truck, maxTrucks, hubLimit, via, HandlingHours: 2);

    private static LineHaulPlan Solve(LineHaulProblem problem)
    {
        var plan = LineHaulPlanner.Solve(problem, Fast);
        Assert.Empty(LineHaulChecker.Check(problem, plan));
        return plan;
    }

    /// <summary>The lexicographic objective, recomputed from a plan exactly as the model counts it.</summary>
    private static (long Lateness, decimal Cost) Score(LineHaulProblem problem, LineHaulPlan plan)
    {
        long lateness = 0;
        foreach (var route in plan.Routes)
        {
            var parcel = problem.Parcels.Single(p => p.Id == route.ParcelId);
            var weight = parcel.IsReturn ? 1 : 2;
            lateness += weight * (route.Shipped
                ? LineHaulPlanner.LateMinutes(route.ArriveAt!.Value, parcel.DueAt)
                : LineHaulPlanner.WaitingMinutes(problem, parcel.AtHub, parcel.ToHub, parcel.DueAt));
        }

        return (lateness, plan.Kpis.Cost);
    }

    [Fact]
    public void Nothing_waiting_means_nothing_to_plan()
    {
        var plan = Solve(Problem([]));

        Assert.Equal(SolveStatus.Empty, plan.Report.Status);
        Assert.Empty(plan.Departures);
    }

    [Fact]
    public void Parcels_for_one_lane_share_one_truck_on_the_earliest_departure()
    {
        var plan = Solve(Problem([Parcel(1, "A", "B", 10, 48), Parcel(2, "A", "B", 10, 48), Parcel(3, "A", "B", 10, 48)]));

        var truck = Assert.Single(plan.Departures);
        Assert.Equal((Departures[0], 1, 3), (truck.DepartAt, truck.Trucks, truck.ParcelIds.Count));
        Assert.Equal(Truck.TripCost(300), plan.Kpis.Cost);
        Assert.Equal(0.3, truck.WeightFill, 6);
        Assert.Equal(SolveStatus.Optimal, plan.Report.Status);
        Assert.Equal(0, plan.Report.Gap);
        Assert.Equal(3, plan.Kpis.OnTime);
    }

    [Fact]
    public void A_parcel_whose_promise_needs_a_truck_gets_one_even_when_alone()
    {
        var plan = Solve(Problem([Parcel(1, "A", "C", 2, 7)], [new HaulLane("A", "C", 500, 5)]));

        var route = Assert.Single(plan.Routes);
        Assert.Equal(Departures[0], Assert.Single(route.Legs).DepartAt);
        Assert.Equal(0, route.LateHours);
        Assert.Equal(1, plan.Kpis.Trucks);
    }

    [Fact]
    public void When_the_hub_has_one_truck_a_customer_parcel_goes_before_a_return()
    {
        var problem = Problem(
            [Parcel(1, "A", "B", 5, 7), Parcel(2, "A", "C", 5, 7, isReturn: true)],
            [new HaulLane("A", "B", 300, 5), new HaulLane("A", "C", 300, 5)],
            hubLimit: 1);

        var plan = Solve(problem);

        var delivery = plan.Routes.Single(r => r.ParcelId == Id(1));
        var back = plan.Routes.Single(r => r.ParcelId == Id(2));
        Assert.Equal(Departures[0], delivery.Legs[0].DepartAt);
        Assert.Equal(0, delivery.LateHours);
        Assert.True(back.LateHours > 0);
        Assert.All(plan.Departures.GroupBy(d => d.DepartAt), g => Assert.True(g.Sum(d => d.Trucks) <= 1));
    }

    [Fact]
    public void Changing_trucks_at_a_third_hub_saves_a_whole_truck_and_beats_today_s_practice()
    {
        HaulLane[] lanes = [new("A", "B", 200, 3), new("B", "C", 200, 3), new("A", "C", 380, 5)];
        var parcels = Enumerable.Range(1, 5).Select(i => Parcel(i, "A", "B", 10, 48))
            .Concat(Enumerable.Range(6, 5).Select(i => Parcel(i, "B", "C", 10, 48)))
            .Append(Parcel(11, "A", "C", 3, 48))
            .ToList();
        var problem = Problem(parcels, lanes, via: true);

        var plan = Solve(problem);
        var baseline = LineHaulBaseline.Plan(problem);

        Assert.Empty(LineHaulChecker.Check(problem, baseline));
        Assert.Equal(2 * Truck.TripCost(200), plan.Kpis.Cost);
        Assert.Equal(3, baseline.Kpis.Trucks);
        Assert.True(plan.Kpis.Cost < baseline.Kpis.Cost);
        var through = plan.Routes.Single(r => r.ParcelId == Id(11));
        Assert.Equal(new[] { "A>B", "B>C" }, through.Legs.Select(l => $"{l.From}>{l.To}"));
        Assert.Equal(0, plan.Kpis.Late);
    }

    [Fact]
    public void Without_room_for_everything_the_parcels_with_the_most_time_wait()
    {
        var parcels = Enumerable.Range(0, 30).Select(i => Parcel(i + 1, "A", "B", 10, 10 + i)).ToList();
        var problem = Problem(parcels, maxTrucks: 1, departures: [Departures[0], Departures[1]]);

        var plan = Solve(problem);

        var waiting = plan.Routes.Where(r => !r.Shipped).Select(r => r.ParcelId).Order().ToList();
        Assert.Equal(Enumerable.Range(21, 10).Select(Id).Order(), waiting);
        Assert.All(plan.Departures, d => Assert.True(d.LoadKg <= 100));
        Assert.Equal(10, plan.Kpis.Unshipped);
    }

    [Fact]
    public void Volume_fills_a_truck_before_weight_when_parcels_are_bulky()
    {
        var bulky = Enumerable.Range(1, 4).Select(i => new HaulParcel(Id(i), $"LX{i:D11}", "A", "B", 5, 4, Now.AddHours(48))).ToList();

        var plan = Solve(Problem(bulky));

        var departure = Assert.Single(plan.Departures);
        Assert.Equal(2, departure.Trucks);
        Assert.True(departure.FullByVolume);
        Assert.Equal(0.8, departure.VolumeFill, 6);
    }

    [Fact]
    public void A_small_plan_matches_the_best_plan_found_by_trying_every_possibility()
    {
        double[] kg = [10, 10, 10, 5, 5, 5];
        double[] due = [7, 7, 20, 20, 40, 40];
        var parcels = kg.Select((w, i) => Parcel(i + 1, "A", "B", w, due[i])).ToList();
        var problem = Problem(parcels, truck: Truck with { CapacityKg = 25 }, maxTrucks: 2, departures: [Departures[0], Departures[1]]);

        var plan = Solve(problem);

        // Every parcel on departure 0, 1 or left waiting (2): 3^6 plans.
        (long Lateness, decimal Cost) best = (long.MaxValue, decimal.MaxValue);
        var lane = problem.Lanes[0];
        for (var code = 0; code < 729; code++)
        {
            var choice = new int[6];
            for (int i = 0, c = code; i < 6; i++, c /= 3)
            {
                choice[i] = c % 3;
            }

            var feasible = true;
            decimal cost = 0;
            long lateness = 0;
            for (var d = 0; d < 2 && feasible; d++)
            {
                var load = Enumerable.Range(0, 6).Where(i => choice[i] == d).Sum(i => kg[i]);
                var trucks = (int)Math.Ceiling(load / 25 - 1e-9);
                feasible = trucks <= 2;
                cost += trucks * Truck.TripCost(lane.Km);
            }

            if (!feasible)
            {
                continue;
            }

            for (var i = 0; i < 6; i++)
            {
                var dueAt = Now.AddHours(due[i]);
                lateness += 2 * (choice[i] < 2
                    ? LineHaulPlanner.LateMinutes(problem.Departures[choice[i]].AddHours(lane.Hours), dueAt)
                    : LineHaulPlanner.WaitingMinutes(problem, "A", "B", dueAt));
            }

            if (lateness < best.Lateness || (lateness == best.Lateness && cost < best.Cost))
            {
                best = (lateness, cost);
            }
        }

        Assert.Equal(best, Score(problem, plan));
        Assert.Equal(SolveStatus.Optimal, plan.Report.Status);
    }

    [Fact]
    public void Today_s_practice_is_never_better_than_the_plan_on_lateness_and_then_cost()
    {
        var random = new Random(42);
        HaulLane[] lanes = [new("A", "B", 300, 5), new("A", "C", 450, 7), new("B", "C", 250, 4), new("B", "A", 300, 5)];
        var parcels = Enumerable.Range(1, 60).Select(i =>
        {
            var lane = lanes[random.Next(lanes.Length)];
            return Parcel(i, lane.From, lane.To, 1 + random.Next(30), 6 + random.Next(40), isReturn: random.Next(5) == 0);
        }).ToList();
        var problem = Problem(parcels, lanes, maxTrucks: 2, hubLimit: 2);

        var plan = Solve(problem);
        var baseline = LineHaulBaseline.Plan(problem);

        Assert.Empty(LineHaulChecker.Check(problem, baseline));
        var (planLate, planCost) = Score(problem, plan);
        var (baseLate, baseCost) = Score(problem, baseline);
        Assert.True(planLate < baseLate || (planLate == baseLate && planCost <= baseCost), $"plan {planLate}/{planCost}, baseline {baseLate}/{baseCost}");
    }

    [Fact]
    public void The_same_problem_and_seed_give_the_same_plan()
    {
        var parcels = Enumerable.Range(1, 12).Select(i => Parcel(i, "A", "B", 9, 10 + (i % 3) * 12)).ToList();
        var problem = Problem(parcels, maxTrucks: 1);
        var single = Fast with { Workers = 1 };

        var first = LineHaulPlanner.Solve(problem, single);
        var second = LineHaulPlanner.Solve(problem, single);

        Assert.Equal(first.Departures.Select(d => (d.DepartAt, d.Trucks, string.Join(',', d.ParcelIds))), second.Departures.Select(d => (d.DepartAt, d.Trucks, string.Join(',', d.ParcelIds))));
    }

    [Fact]
    public void The_checker_catches_a_plan_that_was_tampered_with()
    {
        var problem = Problem(Enumerable.Range(1, 15).Select(i => Parcel(i, "A", "B", 10, 48)).ToList());
        var plan = Solve(problem);
        var departure = plan.Departures[0];

        var overloaded = plan with { Departures = [departure with { Trucks = 1 }, .. plan.Departures.Skip(1)] };
        var dropped = plan with { Departures = [departure with { ParcelIds = departure.ParcelIds.Skip(1).ToList() }, .. plan.Departures.Skip(1)] };
        var cheaper = plan with { Departures = [departure with { Cost = departure.Cost - 1 }, .. plan.Departures.Skip(1)] };
        var twice = plan with { Routes = [.. plan.Routes, plan.Routes[0]] };
        var lost = plan with { Routes = plan.Routes.Skip(1).ToList() };

        Assert.Contains(LineHaulChecker.Check(problem, overloaded), v => v.Code == "departure.overloaded");
        Assert.Contains(LineHaulChecker.Check(problem, dropped), v => v.Code == "departure.manifest");
        Assert.Contains(LineHaulChecker.Check(problem, cheaper), v => v.Code == "departure.cost");
        Assert.Contains(LineHaulChecker.Check(problem, twice), v => v.Code == "parcel.duplicate");
        Assert.Contains(LineHaulChecker.Check(problem, lost), v => v.Code == "parcel.missing");
    }

    [Fact]
    public void A_parcel_with_no_road_to_its_hub_waits_and_is_reported()
    {
        var plan = Solve(Problem([Parcel(1, "A", "Z", 5, 24)]));

        Assert.False(Assert.Single(plan.Routes).Shipped);
        Assert.Equal(1, plan.Kpis.Unshipped);
        Assert.Empty(plan.Departures);
    }
}
