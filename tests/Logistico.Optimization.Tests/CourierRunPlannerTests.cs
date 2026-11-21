using Logistico.Optimization.Routing;

namespace Logistico.Optimization.Tests;

/// <summary>Courier runs keep every limit, and on small cases match the shortest run found by trying every order.</summary>
public class CourierRunPlannerTests
{
    private static readonly GeoPoint Hub = new(33.3152, 44.3661); // Baghdad
    private static readonly DateTimeOffset Morning = new(2026, 11, 23, 6, 0, 0, TimeSpan.Zero); // 09:00 Baghdad
    private static readonly SolveOptions Fast = new(TimeLimitSeconds: 2, Seed: 3, Workers: 1);

    private static Guid Id(int n) => new(n, 0, 0, new byte[8]);

    private static RouteStop Stop(int n, double north, double east, decimal cod = 0, bool mustGo = true, double kg = 1,
        PointSource source = PointSource.Gps, string area = "", int daysLate = 0) =>
        new(Id(n), $"LX{n:D11}", new GeoPoint(Hub.Latitude + north, Hub.Longitude + east), source, area, "IQ-BG", kg, cod, mustGo, daysLate);

    private static RouteCourier Courier(string label, int stops = 40, double kg = 60, decimal cash = 5_000_000, int minutes = 480) =>
        new(label, stops, kg, cash, minutes);

    private static CourierRunProblem Problem(IReadOnlyList<RouteStop> stops, params RouteCourier[] couriers) =>
        new(Hub, Morning, couriers, stops, new TravelModel(), ServiceMinutes: 5, BalanceWeight: 0);

    private static CourierRunPlan Solve(CourierRunProblem problem)
    {
        var plan = CourierRunPlanner.Solve(problem, Fast);
        Assert.Empty(CourierRunChecker.Check(problem, plan));
        return plan;
    }

    private static IEnumerable<int[]> Permutations(int[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (var i = 0; i < items.Length; i++)
        {
            foreach (var rest in Permutations([.. items[..i], .. items[(i + 1)..]]))
            {
                yield return [items[i], .. rest];
            }
        }
    }

    [Fact]
    public void One_courier_s_run_is_the_shortest_possible_order()
    {
        RouteStop[] stops =
        [
            Stop(1, 0.020, 0.010), Stop(2, -0.015, 0.030), Stop(3, 0.030, -0.020),
            Stop(4, -0.025, -0.025), Stop(5, 0.005, 0.040), Stop(6, 0.040, 0.020),
        ];
        var problem = Problem(stops, Courier("علي (BG-01)"));

        var plan = Solve(problem);

        var matrix = TravelMatrix.Build(problem);
        var shortest = Permutations([1, 2, 3, 4, 5, 6]).Min(order =>
        {
            var km = matrix.Km(0, order[0]) + matrix.Km(order[^1], 0);
            for (var i = 1; i < order.Length; i++)
            {
                km += matrix.Km(order[i - 1], order[i]);
            }

            return km;
        });
        Assert.Equal(shortest, plan.Kpis.Km, 6);
        Assert.Equal(6, Assert.Single(plan.Routes).Stops.Count);
        Assert.Equal(Enumerable.Range(1, 6), plan.Routes[0].Stops.Select(s => s.Sequence));
    }

    [Fact]
    public void Cash_carried_never_passes_the_safety_limit()
    {
        var stops = Enumerable.Range(1, 8).Select(i => Stop(i, 0.01 * i, 0.005 * i, cod: 400_000)).ToList();
        var problem = Problem(stops, Courier("علي (BG-01)", cash: 1_500_000), Courier("حسن (BG-02)", cash: 1_500_000), Courier("زيد (BG-03)", cash: 1_500_000));

        var plan = Solve(problem);

        Assert.All(plan.Routes, r => Assert.True(r.Cash <= 1_500_000));
        Assert.Equal(8, plan.Kpis.Planned);
        Assert.True(plan.Kpis.CouriersUsed >= 3); // 3.2 million in cash needs three couriers
        Assert.All(plan.Routes.SelectMany(r => r.Stops), s => Assert.True(s.CashAfter <= 1_500_000));
    }

    [Fact]
    public void When_the_day_is_too_short_parcels_due_today_go_first()
    {
        // Three parcels due today a few streets away; three due later on the far side of the city (an hour each way).
        var stops = Enumerable.Range(1, 3).Select(i => Stop(i, 0.01 * i, 0, mustGo: true))
            .Concat(Enumerable.Range(4, 3).Select(i => Stop(i, 0, 0.2 + 0.01 * i, mustGo: false)))
            .ToList();
        var problem = Problem(stops, Courier("علي (BG-01)", minutes: 60));

        var plan = Solve(problem);

        Assert.Equal(new[] { Id(1), Id(2), Id(3) }, plan.Routes[0].Stops.Select(s => s.StopId).Order());
        Assert.True(plan.Routes[0].Minutes <= 60);
        Assert.Equal(0, plan.Kpis.MustGoDeferred);
        Assert.All(plan.Deferred, d => Assert.Equal((false, DeferReason.Time), (d.MustGo, d.Reason)));
    }

    [Fact]
    public void Limits_explain_why_a_parcel_stays_at_the_hub()
    {
        var heavy = Stop(1, 0.01, 0.01, kg: 80);
        var plan = Solve(Problem([heavy, Stop(2, 0.02, 0.01)], Courier("علي (BG-01)", kg: 60)));

        var deferred = Assert.Single(plan.Deferred);
        Assert.Equal((Id(1), DeferReason.Weight, true), (deferred.StopId, deferred.Reason, deferred.MustGo));

        var noCourier = CourierRunPlanner.Solve(Problem([Stop(3, 0.01, 0.01)]), Fast);
        Assert.Equal(SolveStatus.Infeasible, noCourier.Report.Status);
        Assert.Equal(DeferReason.NoCourier, Assert.Single(noCourier.Deferred).Reason);
    }

    [Fact]
    public void Positions_known_only_roughly_are_never_treated_as_next_door()
    {
        var problem = Problem(
        [
            Stop(1, 0, 0, source: PointSource.Area, area: "المنصور"),
            Stop(2, 0, 0, source: PointSource.Area, area: "المنصور"),
            Stop(3, 0, 0, source: PointSource.Governorate),
            Stop(4, 0, 0, source: PointSource.Gps),
            Stop(5, 0, 0, source: PointSource.Gps),
        ], Courier("علي (BG-01)"));

        var matrix = TravelMatrix.Build(problem);

        Assert.Equal(1.5, matrix.Km(1, 2), 6);  // same area, both from its middle
        Assert.Equal(1.5, matrix.Km(0, 1), 6);  // the hub is known; the stop is somewhere in its area
        Assert.Equal(6, matrix.Km(3, 0), 6);    // …or somewhere in the city
        Assert.Equal(0, matrix.Km(0, 4), 6);    // a GPS stop at the hub's own door
        Assert.Equal(6, matrix.Km(1, 3), 6);    // only the governorate is known
        Assert.Equal(0, matrix.Km(4, 5), 6);    // the same GPS point
        Assert.Equal(matrix.Km(2, 1), matrix.Km(1, 2));
        Assert.Equal(0, matrix.Seconds(4, 5));
    }

    [Fact]
    public void Planned_runs_are_shorter_than_today_s_practice_on_a_spread_out_day()
    {
        var random = new Random(11);
        string[] areas = ["المنصور", "الكرادة", "الدورة", "الشعب", "الأعظمية"];
        var stops = Enumerable.Range(1, 40).Select(i =>
        {
            var area = random.Next(areas.Length);
            return Stop(i, (area - 2) * 0.03 + random.NextDouble() * 0.02, (random.NextDouble() - 0.5) * 0.08, cod: 25_000 * random.Next(1, 5), area: areas[area]);
        }).ToList();
        var problem = Problem(stops, Courier("علي (BG-01)", stops: 15), Courier("حسن (BG-02)", stops: 15), Courier("زيد (BG-03)", stops: 15));

        var plan = Solve(problem);
        var baseline = CourierRunBaseline.Plan(problem);

        Assert.Empty(CourierRunChecker.Check(problem, baseline));
        Assert.Equal(40, plan.Kpis.Planned);
        Assert.True(plan.Kpis.Km < baseline.Kpis.Km, $"plan {plan.Kpis.Km:0.0} km, baseline {baseline.Kpis.Km:0.0} km");
    }

    [Fact]
    public void Arrival_times_follow_the_road_and_the_minutes_at_each_door()
    {
        var problem = Problem([Stop(1, 0.02, 0), Stop(2, 0.04, 0)], Courier("علي (BG-01)"));

        var plan = Solve(problem);

        var matrix = TravelMatrix.Build(problem);
        var stops = plan.Routes[0].Stops;
        var first = stops[0].StopId == Id(1) ? 1 : 2;
        Assert.Equal(Morning.AddSeconds(matrix.Seconds(0, first)), stops[0].ArriveAt);
        Assert.Equal(stops[0].ArriveAt.AddMinutes(5).AddSeconds(matrix.Seconds(first, 3 - first)), stops[1].ArriveAt);
    }

    [Fact]
    public void The_checker_catches_runs_that_break_a_limit_or_lose_a_parcel()
    {
        var problem = Problem(Enumerable.Range(1, 4).Select(i => Stop(i, 0.01 * i, 0, cod: 100_000)).ToList(), Courier("علي (BG-01)", cash: 400_000));
        var plan = Solve(problem);
        var route = plan.Routes[0];

        var richer = problem with { Couriers = [Courier("علي (BG-01)", cash: 300_000)] };
        var lost = plan with { Routes = [route with { Stops = route.Stops.Skip(1).ToList() }] };
        var twice = plan with { Deferred = [new DeferredStop(route.Stops[0].StopId, route.Stops[0].TrackingNumber, true, DeferReason.Time)] };

        Assert.Contains(CourierRunChecker.Check(richer, plan), v => v.Code == "route.cash");
        Assert.Contains(CourierRunChecker.Check(problem, lost), v => v.Code == "stop.missing");
        Assert.Contains(CourierRunChecker.Check(problem, twice), v => v.Code == "stop.duplicate");
    }

    [Fact]
    public void An_empty_shelf_means_no_runs()
    {
        var plan = Solve(Problem([], Courier("علي (BG-01)")));

        Assert.Equal(SolveStatus.Empty, plan.Report.Status);
        Assert.Equal(0, plan.Kpis.Planned);
    }
}
