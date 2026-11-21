namespace Logistico.Optimization;

/// <summary>A point on the earth, in degrees (WGS 84).</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    private const double EarthRadiusKm = 6371.0088;

    /// <summary>Great-circle distance (haversine), in kilometres.</summary>
    public double KmTo(GeoPoint other)
    {
        static double Rad(double degrees) => degrees * Math.PI / 180;
        var dLat = Rad(other.Latitude - Latitude);
        var dLon = Rad(other.Longitude - Longitude);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Rad(Latitude)) * Math.Cos(Rad(other.Latitude)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}

/// <summary>How sure a plan can be of where a stop is.</summary>
public enum PointSource
{
    /// <summary>GPS of an earlier delivery to the same customer (or a pin they dropped).</summary>
    Gps,

    /// <summary>The middle of the customer's area, from other deliveries there.</summary>
    Area,

    /// <summary>Only the governorate is known: its capital stands in.</summary>
    Governorate,
}

/// <summary>What the solver could prove about a plan.</summary>
public enum SolveStatus
{
    /// <summary>Nothing to plan.</summary>
    Empty,

    /// <summary>Proven best under the model.</summary>
    Optimal,

    /// <summary>A valid plan; the time limit ended the search before it was proven best (see the gap).</summary>
    Feasible,

    /// <summary>No plan satisfies the rules.</summary>
    Infeasible,

    /// <summary>The time limit ended before any plan was found.</summary>
    NoSolution,
}

/// <summary>
/// How a plan was found: by which solver, how good it is proven to be (<see cref="Bound"/> is a value no plan can beat,
/// so <see cref="Gap"/> = 0 means proven optimal) and how long it took. Reproducible: same inputs and seed, same plan.
/// </summary>
public sealed record SolveReport(SolveStatus Status, double Objective, double? Bound, double Seconds, string Solver, int Seed, IReadOnlyList<string> Notes)
{
    /// <summary>Relative distance between the plan and the best possible plan (0 when proven optimal; null without a bound).</summary>
    public double? Gap => Status == SolveStatus.Optimal ? 0
        : Bound is { } bound && Objective > 0 ? Math.Max(0, (Objective - bound) / Objective)
        : null;

    public static SolveReport Nothing(string solver) => new(SolveStatus.Empty, 0, 0, 0, solver, 0, []);
}

/// <summary>A rule a plan breaks, found by a checker that recomputes everything itself instead of trusting the solver.</summary>
public sealed record Violation(string Code, string Subject, string Detail)
{
    public override string ToString() => $"{Code} [{Subject}] {Detail}";
}

/// <summary>Search limits shared by all planners.</summary>
public sealed record SolveOptions(double TimeLimitSeconds = 10, int Seed = 1, int Workers = 8)
{
    public static SolveOptions Default { get; } = new();
}
