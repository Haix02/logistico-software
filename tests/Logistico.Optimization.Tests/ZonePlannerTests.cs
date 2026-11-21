using Logistico.Optimization.Zoning;

namespace Logistico.Optimization.Tests;

/// <summary>Courier zones are balanced within the tolerance and, on small cases, as compact as the best split found by brute force.</summary>
public class ZonePlannerTests
{
    private static readonly SolveOptions Fast = new(TimeLimitSeconds: 10, Seed: 5, Workers: 4);

    private static ZoneUnit Unit(string key, double north, double east, double workload) =>
        new(key, key, "IQ-BG", new GeoPoint(33.31 + north, 44.36 + east), PointSource.Area, workload);

    private static ZoningPlan Solve(ZoningProblem problem)
    {
        var plan = ZonePlanner.Solve(problem, Fast);
        Assert.Empty(ZoningChecker.Check(problem, plan));
        return plan;
    }

    [Fact]
    public void Two_clusters_of_areas_become_two_balanced_zones()
    {
        ZoneUnit[] units =
        [
            Unit("كرخ-1", 0.00, -0.10, 30), Unit("كرخ-2", 0.01, -0.11, 30), Unit("كرخ-3", -0.01, -0.09, 30),
            Unit("رصافة-1", 0.00, 0.10, 30), Unit("رصافة-2", 0.01, 0.11, 30), Unit("رصافة-3", -0.01, 0.09, 30),
        ];

        var plan = Solve(new ZoningProblem(units, 2));

        Assert.Equal(SolveStatus.Optimal, plan.Report.Status);
        Assert.Equal(2, plan.Zones.Count);
        Assert.All(plan.Zones, z => Assert.Equal(90, z.Workload));
        Assert.All(plan.Zones, z => Assert.Single(z.UnitKeys.Select(k => k[..4]).Distinct()));
        Assert.Equal(0, plan.MaxDeviation, 6);
    }

    [Fact]
    public void A_small_split_is_as_compact_as_the_best_one_found_by_trying_every_split()
    {
        ZoneUnit[] units =
        [
            Unit("a", 0.00, 0.00, 12), Unit("b", 0.02, 0.01, 8), Unit("c", 0.05, 0.03, 10),
            Unit("d", -0.03, 0.04, 14), Unit("e", -0.01, -0.05, 6), Unit("f", 0.04, -0.02, 10),
        ];

        // ±21 % of 30 is 23.7–36.3: no split sits exactly on a bound, so "within" means the same to both sides.
        var problem = new ZoningProblem(units, 2, Tolerance: 0.21);

        var plan = Solve(problem);

        // Exactly as the model counts compactness: share of the work in ten-thousandths × distance in 100 m.
        var total = units.Sum(u => u.Workload);
        long[] weight = units.Select(u => Math.Max(1, (long)Math.Round(u.Workload / total * 10_000))).ToArray();
        long Distance(int i, int j) => (long)Math.Round(units[i].Point.KmTo(units[j].Point) * problem.RoadFactor * 10);
        bool Balanced(IEnumerable<int> zone) => Math.Abs(zone.Sum(u => units[u].Workload) / (total / 2) - 1) <= 0.21;
        var best = long.MaxValue;
        for (var a = 0; a < 6; a++)
        {
            for (var b = a + 1; b < 6; b++)
            {
                for (var mask = 0; mask < 64; mask++)
                {
                    // Bit set: the area belongs to centre b; centres belong to themselves.
                    if ((mask >> a & 1) == 1 || (mask >> b & 1) == 0)
                    {
                        continue;
                    }

                    var zoneB = Enumerable.Range(0, 6).Where(u => (mask >> u & 1) == 1).ToList();
                    var zoneA = Enumerable.Range(0, 6).Except(zoneB).ToList();
                    if (Balanced(zoneA) && Balanced(zoneB))
                    {
                        best = Math.Min(best, zoneA.Sum(u => weight[u] * Distance(u, a)) + zoneB.Sum(u => weight[u] * Distance(u, b)));
                    }
                }
            }
        }

        Assert.Equal(best, (long)Math.Round(plan.Report.Objective));
        Assert.Equal(SolveStatus.Optimal, plan.Report.Status);
    }

    /// <summary>
    /// Regression: workloads were rounded to tenths, so zones made of quiet areas (0.1–0.8 deliveries a week) passed the
    /// model yet broke the balance (a zone at 78 % of the average with ±15 % asked); the checker refused the plan.
    /// </summary>
    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 4)]
    [InlineData(3, 5)]
    [InlineData(4, 3)]
    public void Zones_of_quiet_areas_keep_the_balance_in_real_workloads(int seed, int zones)
    {
        var random = new Random(seed);
        var units = Enumerable.Range(1, 30)
            .Select(i => Unit($"u{i:D2}", (random.NextDouble() - 0.5) * 0.2, (random.NextDouble() - 0.5) * 0.2, Math.Round(0.02 + random.NextDouble() * 0.2, 2)))
            .ToList();

        var plan = Solve(new ZoningProblem(units, zones, Tolerance: 0.15));

        Assert.Equal(zones, plan.Zones.Count);
        Assert.All(plan.Zones, z => Assert.InRange(z.Share, 1 - plan.Tolerance - 1e-9, 1 + plan.Tolerance + 1e-9));
    }

    [Fact]
    public void An_area_bigger_than_a_zone_widens_the_tolerance_and_says_so()
    {
        var problem = new ZoningProblem([Unit("big", 0, 0, 100), Unit("s1", 0.01, 0, 10), Unit("s2", 0.02, 0, 10), Unit("s3", 0.03, 0, 10)], 2);

        var plan = Solve(problem);

        Assert.True(plan.Tolerance > 0.15);
        Assert.NotEmpty(plan.Report.Notes);
        Assert.Contains(plan.Zones, z => z.UnitKeys.Contains("big") && z.Share > 1.15); // two splits are equally compact here
    }

    [Fact]
    public void More_zones_than_areas_gives_each_area_its_own_zone()
    {
        var plan = Solve(new ZoningProblem([Unit("a", 0, 0, 5), Unit("b", 0.1, 0, 7), Unit("c", 0.2, 0, 9)], 5, Tolerance: 1));

        Assert.Equal(3, plan.Zones.Count);
        Assert.All(plan.Zones, z => Assert.Single(z.UnitKeys));
    }

    [Fact]
    public void The_checker_catches_an_area_left_out_or_counted_twice()
    {
        ZoneUnit[] units = [Unit("a", 0, 0, 10), Unit("b", 0.1, 0, 10), Unit("c", 0.2, 0, 10), Unit("d", 0.3, 0, 10)];
        var problem = new ZoningProblem(units, 2);
        var plan = Solve(problem);
        var zone = plan.Zones[0];

        var lost = plan with { Zones = [zone with { UnitKeys = zone.UnitKeys.Skip(1).ToList() }, .. plan.Zones.Skip(1)] };
        var twice = plan with { Zones = [zone with { UnitKeys = [.. zone.UnitKeys, plan.Zones[1].UnitKeys[0]] }, .. plan.Zones.Skip(1)] };

        Assert.Contains(ZoningChecker.Check(problem, lost), v => v.Code == "unit.missing");
        Assert.Contains(ZoningChecker.Check(problem, twice), v => v.Code == "unit.duplicate");
    }
}
