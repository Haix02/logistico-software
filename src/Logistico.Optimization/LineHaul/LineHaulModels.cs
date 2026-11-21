namespace Logistico.Optimization.LineHaul;

/// <summary>
/// A parcel waiting at <see cref="AtHub"/> that must reach <see cref="ToHub"/> by <see cref="DueAt"/> (the latest arrival
/// that still lets the destination's couriers keep the promise).
/// </summary>
public sealed record HaulParcel(Guid Id, string TrackingNumber, string AtHub, string ToHub, double WeightKg, double VolumeM3, DateTimeOffset DueAt, bool IsReturn = false);

/// <summary>A road between two hubs a truck can drive in one leg.</summary>
public sealed record HaulLane(string From, string To, double Km, double Hours);

/// <summary>The company's line-haul truck: what it carries and what one trip costs.</summary>
public sealed record TruckType(double CapacityKg, double CapacityM3, decimal FixedCost, decimal CostPerKm)
{
    public decimal TripCost(double km) => decimal.Round(FixedCost + CostPerKm * (decimal)km, 0, MidpointRounding.AwayFromZero);
}

/// <summary>
/// One day-ahead line-haul planning problem (docs/20 §3.1): parcels waiting at hubs for other hubs, the departure times
/// trucks may leave at, the lanes, and the truck. With <see cref="AllowVia"/> a parcel may change trucks at a third hub.
/// </summary>
public sealed record LineHaulProblem(
    DateTimeOffset Now,
    IReadOnlyList<DateTimeOffset> Departures,
    IReadOnlyList<HaulLane> Lanes,
    IReadOnlyList<HaulParcel> Parcels,
    TruckType Truck,
    int MaxTrucksPerDeparture = 3,
    int MaxTrucksPerHubPerDeparture = 0,
    bool AllowVia = false,
    double HandlingHours = 2,
    double MaxDetour = 1.6)
{
    /// <summary>What leaving a parcel for a later plan costs at most, in hours of lateness, besides its own delay (see <c>LineHaulPlanner.WaitingMinutes</c>).</summary>
    public const double UnshippedPenaltyHours = 24;

    public HaulLane? Lane(string from, string to) => Lanes.FirstOrDefault(l => l.From == from && l.To == to);

    /// <summary>The last departure the plan may use; parcels not moved by then wait for the next plan.</summary>
    public DateTimeOffset HorizonEnd => Departures.Count == 0 ? Now : Departures.Max();
}

public sealed record HaulLeg(string From, string To, DateTimeOffset DepartAt, DateTimeOffset ArriveAt);

/// <summary>Where one parcel goes: its legs (none when it waits for a later plan), when it arrives and how late.</summary>
public sealed record ParcelRoute(Guid ParcelId, string TrackingNumber, IReadOnlyList<HaulLeg> Legs, DateTimeOffset? ArriveAt, double LateHours)
{
    public bool Shipped => Legs.Count > 0;
}

/// <summary>Trucks leaving one hub for another at one time, and what they carry.</summary>
public sealed record PlannedDeparture(
    string From,
    string To,
    DateTimeOffset DepartAt,
    DateTimeOffset ArriveAt,
    int Trucks,
    double Km,
    double LoadKg,
    double LoadM3,
    double WeightFill,
    double VolumeFill,
    decimal Cost,
    IReadOnlyList<Guid> ParcelIds)
{
    /// <summary>The tighter of the two fills: what limits this departure.</summary>
    public double Fill => Math.Max(WeightFill, VolumeFill);

    public bool FullByVolume => VolumeFill >= WeightFill;
}

public sealed record LineHaulKpis(int Trucks, double TruckKm, decimal Cost, int Parcels, int OnTime, int Late, double LateParcelHours, int Unshipped, double AverageFill);

public sealed record LineHaulPlan(IReadOnlyList<PlannedDeparture> Departures, IReadOnlyList<ParcelRoute> Routes, LineHaulKpis Kpis, SolveReport Report);
