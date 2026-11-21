using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Contracts;

/// <summary>The three planners (docs/20-optimization-engine.md).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlanKind>))]
public enum PlanKind
{
    /// <summary>Hub-to-hub trucks for the coming days.</summary>
    Trucks,

    /// <summary>This morning's courier runs from one hub.</summary>
    CourierRuns,

    /// <summary>Courier zones of one hub.</summary>
    Zones,
}

[JsonConverter(typeof(JsonStringEnumConverter<PlanRunStatus>))]
public enum PlanRunStatus
{
    /// <summary>Solved and checked; waiting for a person to approve it.</summary>
    Ready,
    Applied,
    Discarded,
}

/// <summary>What the solver could prove (mirrors the engine's status for the browser).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlanQuality>))]
public enum PlanQuality
{
    Empty,
    Optimal,
    Feasible,
    Infeasible,
    NoSolution,
}

/// <summary>How sure the plan is of where a customer is.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StopPrecision>))]
public enum StopPrecision
{
    Gps,
    Area,
    Governorate,
}

[JsonConverter(typeof(JsonStringEnumConverter<DeferredBecause>))]
public enum DeferredBecause
{
    NoCourier,
    Stops,
    Weight,
    Cash,
    Time,
    NotNeededToday,
}

/// <summary>
/// A company's planning settings (Automation → Planning): trucks and their cost, departure times, courier limits and
/// how travel is estimated. Validated the same way in the browser and on the server.
/// </summary>
public sealed class PlanningPolicy : IValidatableObject
{
    // Line-haul trucks
    [Range(100, 40_000, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public double TruckCapacityKg { get; set; } = 8_000;

    [Range(0.5, 120, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public double TruckCapacityM3 { get; set; } = 35;

    /// <summary>Driver and fixed cost of one truck trip (IQD).</summary>
    [Range(typeof(decimal), "0", "100000000", ParseLimitsInInvariantCulture = true, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal TruckTripCostIqd { get; set; } = 150_000;

    [Range(typeof(decimal), "0", "1000000", ParseLimitsInInvariantCulture = true, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal TruckCostPerKmIqd { get; set; } = 1_250;

    /// <summary>Times (Baghdad, "HH:mm") trucks may leave a hub, every day.</summary>
    public List<string> DepartureTimes { get; set; } = ["06:00", "14:00", "22:00"];

    [Range(1, 3, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int PlanningDays { get; set; } = 2;

    [Range(1, 10, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int MaxTrucksPerDeparture { get; set; } = 3;

    /// <summary>Trucks a hub can send at one departure time, over all lanes (0 = as many as needed).</summary>
    [Range(0, 50, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int TrucksPerHub { get; set; }

    /// <summary>A parcel must be at its customer's hub by this time (Baghdad) on its promised day, before the couriers leave.</summary>
    public string HubArrivalCutoff { get; set; } = "09:00";

    /// <summary>Volume assumed per kilogram when a parcel was booked without one (≈ 167 kg per m³, the usual road ratio).</summary>
    [Range(0.0001, 0.1, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public double AssumedM3PerKg { get; set; } = 0.006;

    // Couriers
    [Range(1, 400, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int MotorcycleStops { get; set; } = 35;

    [Range(1, 500, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public double MotorcycleKg { get; set; } = 60;

    [Range(1, 400, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int VanStops { get; set; } = 90;

    [Range(1, 5_000, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public double VanKg { get; set; } = 700;

    /// <summary>Most cash a courier may carry on one run (safety).</summary>
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal MaxCashPerCourierIqd { get; set; } = 3_000_000;

    [Range(60, 840, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int ShiftMinutes { get; set; } = 480;

    [Range(1, 60, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int MinutesPerStop { get; set; } = 6;

    /// <summary>When couriers leave the hub (Baghdad, "HH:mm").</summary>
    public string RunStart { get; set; } = "09:00";

    [Range(5, 80, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public double CitySpeedKmh { get; set; } = 25;

    /// <summary>How much longer roads are than the straight line.</summary>
    [Range(1, 3, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public double RoadFactor { get; set; } = 1.3;

    // Zones
    [Range(5, 100, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int ZoneBalancePercent { get; set; } = 15;

    [Range(7, 180, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int ZoneHistoryDays { get; set; } = 56;

    // Search
    [Range(1, 60, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int TimeLimitSeconds { get; set; } = 10;

    public uint Version { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public static bool TryParseTime(string? value, out TimeSpan time) =>
        TimeSpan.TryParseExact(value?.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out time) && time < TimeSpan.FromDays(1);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DepartureTimes is not { Count: > 0 and <= 8 } || DepartureTimes.Any(t => !TryParseTime(t, out _)) || DepartureTimes.Distinct().Count() != DepartureTimes.Count)
        {
            yield return new(ValidationText.TimeList, [nameof(DepartureTimes)]);
        }

        if (!TryParseTime(HubArrivalCutoff, out _))
        {
            yield return new(ValidationText.TimeList, [nameof(HubArrivalCutoff)]);
        }

        if (!TryParseTime(RunStart, out _))
        {
            yield return new(ValidationText.TimeList, [nameof(RunStart)]);
        }
    }

    public PlanningPolicy Clone() => (PlanningPolicy)MemberwiseClone();
}

/// <summary>Start a planner. Courier runs and zones are per hub; trucks cover the whole network.</summary>
public sealed class StartPlanRequest : IValidatableObject
{
    public PlanKind Kind { get; set; }

    [StringLength(8, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? HubCode { get; set; }

    /// <summary>Zones wanted (default: one per active courier of the hub).</summary>
    [Range(1, 60, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int? Zones { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Kind != PlanKind.Trucks && string.IsNullOrWhiteSpace(HubCode))
        {
            yield return new(ValidationText.Required, [nameof(HubCode)]);
        }
    }
}

public sealed record PlanRunSummary(
    Guid Id,
    PlanKind Kind,
    string? HubCode,
    PlanRunStatus Status,
    PlanQuality Quality,
    double? Gap,
    double Seconds,
    string Solver,
    string? RequestedBy,
    DateTimeOffset RequestedAt,
    string? AppliedBy,
    DateTimeOffset? AppliedAt,
    decimal? CostIqd,
    double? Km,
    int Parcels,
    uint Version);

public sealed record PlanRunDetails(PlanRunSummary Summary, TruckPlanView? Trucks, CourierPlanView? Couriers, ZonePlanView? Zones, IReadOnlyList<string> Violations);

/// <summary>What approving a plan did: parcels put on trucks or handed to couriers, and those skipped because they moved since the plan was made.</summary>
public sealed record PlanApplyResult(PlanRunSummary Summary, int Applied, int Skipped, IReadOnlyList<string> Transfers);

// ── Trucks ─────────────────────────────────────────────────────────────────────────────────────────────────────────

public sealed record TruckPlanKpis(int Trucks, double TruckKm, decimal CostIqd, int Parcels, int OnTime, int Late, double LateHours, int Waiting, double AverageFill);

/// <summary>Trucks leaving one hub for another at one time. <see cref="IsNext"/>: the lane's first departure, the one approving loads.</summary>
public sealed record TruckDepartureView(
    string FromHubCode,
    string ToHubCode,
    DateTimeOffset DepartAt,
    DateTimeOffset ArriveAt,
    int Trucks,
    double Km,
    double LoadKg,
    double LoadM3,
    double WeightFill,
    double VolumeFill,
    decimal CostIqd,
    int Parcels,
    int Late,
    bool IsNext);

/// <summary>A parcel the plan moves late or leaves for the next plan.</summary>
public sealed record PlannedParcelView(Guid ShipmentId, string TrackingNumber, string FromHubCode, string ToHubCode, DateTimeOffset DueAt, DateTimeOffset? ArriveAt, double LateHours, bool ViaHub);

public sealed record TruckPlanView(
    TruckPlanKpis Plan,
    TruckPlanKpis Baseline,
    IReadOnlyList<TruckDepartureView> Departures,
    IReadOnlyList<PlannedParcelView> Attention,
    double TruckCapacityKg,
    double TruckCapacityM3,
    int EstimatedVolumes);

// ── Courier runs ───────────────────────────────────────────────────────────────────────────────────────────────────

public sealed record CourierPlanKpis(int Planned, int Deferred, int MustGoDeferred, double Km, int LongestMinutes, int CouriersUsed, int Spread);

public sealed record RunStopView(
    Guid ShipmentId,
    string TrackingNumber,
    int Sequence,
    DateTimeOffset ArriveAt,
    double Km,
    decimal CashAfter,
    string? Area,
    string Governorate,
    double Latitude,
    double Longitude,
    StopPrecision Precision,
    bool MustGo);

public sealed record CourierRunView(string Courier, IReadOnlyList<RunStopView> Stops, double Km, int Minutes, decimal CashIqd, double Kg, int MaxStops, double MaxKg, decimal MaxCashIqd, int ShiftMinutes);

public sealed record DeferredParcelView(Guid ShipmentId, string TrackingNumber, bool MustGo, DeferredBecause Reason);

public sealed record CourierPlanView(
    string HubCode,
    double HubLatitude,
    double HubLongitude,
    DateTimeOffset StartAt,
    CourierPlanKpis Plan,
    CourierPlanKpis Baseline,
    IReadOnlyList<CourierRunView> Runs,
    IReadOnlyList<DeferredParcelView> Deferred,
    int GpsStops,
    int AreaStops,
    int GovernorateStops);

// ── Zones ──────────────────────────────────────────────────────────────────────────────────────────────────────────

public sealed record ZoneAreaView(string Key, string Label, string Governorate, double Workload, double Latitude, double Longitude, StopPrecision Precision);

public sealed record ZoneView(int Number, string CenterKey, IReadOnlyList<ZoneAreaView> Areas, double Workload, double Share, double AverageKm);

public sealed record ZonePlanView(string HubCode, IReadOnlyList<ZoneView> Zones, double TolerancePercent, double AskedTolerancePercent, double MaxDeviationPercent, int Couriers, int HistoryDays, int Deliveries);
