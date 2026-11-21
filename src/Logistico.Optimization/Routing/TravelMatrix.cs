namespace Logistico.Optimization.Routing;

/// <summary>Kilometres and seconds between the hub (index 0) and every stop (index 1…n), from the <see cref="TravelModel"/>.</summary>
public sealed class TravelMatrix
{
    private readonly double[,] km;

    private TravelMatrix(double[,] km, double speedKmh)
    {
        this.km = km;
        SpeedKmh = speedKmh;
    }

    public double SpeedKmh { get; }

    public int Size => km.GetLength(0);

    public double Km(int from, int to) => km[from, to];

    public int Seconds(int from, int to) => (int)Math.Round(km[from, to] / SpeedKmh * 3600, MidpointRounding.AwayFromZero);

    public static TravelMatrix Build(CourierRunProblem problem)
    {
        var n = problem.Stops.Count + 1;
        var km = new double[n, n];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                km[i, j] = i == j ? 0 : Distance(problem, i, j);
            }
        }

        return new TravelMatrix(km, problem.Travel.SpeedKmh);
    }

    /// <summary>Road estimate between two places, never shorter than what their positions' precision allows.</summary>
    public static double Distance(CourierRunProblem problem, int from, int to)
    {
        var travel = problem.Travel;
        var a = from == 0 ? null : problem.Stops[from - 1];
        var b = to == 0 ? null : problem.Stops[to - 1];
        var pa = a?.Point ?? problem.Depot;
        var pb = b?.Point ?? problem.Depot;
        var road = pa.KmTo(pb) * travel.RoadFactor;
        if (a is null || b is null)
        {
            // The hub's position is known; a stop known only roughly is still at least an area (or a city) away from it.
            return (a ?? b) is not { } stop ? road
                : stop.Source switch
                {
                    PointSource.Gps => road,
                    PointSource.Area => Math.Max(road, travel.SameAreaKm),
                    _ => Math.Max(road, travel.SameGovernorateKm),
                };
        }

        if (a.Source == PointSource.Gps && b.Source == PointSource.Gps)
        {
            return road;
        }

        if (a.Source == PointSource.Governorate || b.Source == PointSource.Governorate)
        {
            return a.Governorate == b.Governorate ? Math.Max(road, travel.SameGovernorateKm) : road;
        }

        // At least one position is only an area's middle.
        return a.Governorate == b.Governorate && string.Equals(a.Area, b.Area, StringComparison.Ordinal) ? Math.Max(road, travel.SameAreaKm) : road;
    }
}
