namespace Logistico.Optimization.Routing;

/// <summary>
/// A parcel to deliver from the hub. <see cref="MustGo"/> parcels are due today (or late): leaving one behind is only
/// done when the couriers cannot take it. <see cref="DaysLate"/> raises the priority of late parcels.
/// </summary>
public sealed record RouteStop(
    Guid Id,
    string TrackingNumber,
    GeoPoint Point,
    PointSource Source,
    string Area,
    string Governorate,
    double WeightKg,
    decimal Cod,
    bool MustGo,
    int DaysLate = 0);

/// <summary>A courier and what they can carry on one run: parcels, kilograms, cash and working time.</summary>
public sealed record RouteCourier(string Label, int MaxStops, double MaxKg, decimal MaxCash, int ShiftMinutes);

/// <summary>
/// How travel is estimated. Between two known positions: great-circle × <see cref="RoadFactor"/> at
/// <see cref="SpeedKmh"/>. When a position is only an area or a governorate, the distance is at least
/// <see cref="SameAreaKm"/> / <see cref="SameGovernorateKm"/>, so unknown addresses are never treated as next door.
/// </summary>
public sealed record TravelModel(double RoadFactor = 1.3, double SpeedKmh = 25, double SameAreaKm = 1.5, double SameGovernorateKm = 6);

/// <summary>One hub's morning: the couriers, the parcels on its shelves for its own customers, and how travel is estimated.</summary>
public sealed record CourierRunProblem(
    GeoPoint Depot,
    DateTimeOffset StartAt,
    IReadOnlyList<RouteCourier> Couriers,
    IReadOnlyList<RouteStop> Stops,
    TravelModel Travel,
    int ServiceMinutes = 6,
    double BalanceWeight = 1);

public sealed record RoutedStop(Guid StopId, string TrackingNumber, int Sequence, DateTimeOffset ArriveAt, double KmSoFar, decimal CashAfter, double KgAfter);

public sealed record CourierRoute(string Courier, IReadOnlyList<RoutedStop> Stops, double Km, int Minutes, decimal Cash, double Kg)
{
    public DateTimeOffset? BackAt(DateTimeOffset start) => Stops.Count == 0 ? null : start.AddMinutes(Minutes);
}

/// <summary>Why a parcel stays at the hub today: the binding limit (stops, weight, cash, working time) or no courier at all.</summary>
public enum DeferReason
{
    NoCourier,
    Stops,
    Weight,
    Cash,
    Time,
    NotNeededToday,
}

public sealed record DeferredStop(Guid StopId, string TrackingNumber, bool MustGo, DeferReason Reason);

public sealed record CourierRunKpis(int Planned, int Deferred, int MustGoDeferred, double Km, int LongestMinutes, int CouriersUsed, double StopsSpread);

public sealed record CourierRunPlan(IReadOnlyList<CourierRoute> Routes, IReadOnlyList<DeferredStop> Deferred, CourierRunKpis Kpis, SolveReport Report);
