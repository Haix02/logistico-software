using System.Text.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Domain;

/// <summary>A company's planning settings (one row per company); missing means the defaults of <see cref="PlanningPolicy"/>.</summary>
internal sealed class PlanningSettings
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private PlanningSettings()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>The <see cref="PlanningPolicy"/> as JSON, so new settings need no schema change.</summary>
    public string Policy { get; private set; } = "{}";

    public DateTimeOffset UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }
    public uint Version { get; private set; }

    public static PlanningSettings Create(Guid tenantId, PlanningPolicy policy, string? by, DateTimeOffset now)
    {
        var settings = new PlanningSettings { TenantId = tenantId };
        settings.Update(policy, by, now);
        return settings;
    }

    public void Update(PlanningPolicy policy, string? by, DateTimeOffset now)
    {
        var stored = policy.Clone();
        stored.DepartureTimes = policy.DepartureTimes.Select(t => t.Trim()).Order(StringComparer.Ordinal).ToList();
        stored.Version = 0;
        stored.UpdatedAt = null;
        stored.UpdatedBy = null;
        Policy = JsonSerializer.Serialize(stored, Json);
        UpdatedAt = now;
        UpdatedBy = by;
    }

    public PlanningPolicy ToPolicy()
    {
        var policy = JsonSerializer.Deserialize<PlanningPolicy>(Policy, Json) ?? new PlanningPolicy();
        policy.Version = Version;
        policy.UpdatedAt = UpdatedAt;
        policy.UpdatedBy = UpdatedBy;
        return policy;
    }
}

/// <summary>
/// One run of a planner, kept for audit and reproducibility (docs/20 §4): what was asked, what the solver proved, the
/// plan as people saw it and the checker's verdict. A plan is applied at most once, and only while it is fresh.
/// </summary>
internal sealed class PlanningRun
{
    /// <summary>After this, the parcels have moved on too much for the plan to be trusted.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromHours(6);

    private PlanningRun()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public PlanKind Kind { get; private set; }
    public string? HubCode { get; private set; }
    public PlanRunStatus Status { get; private set; }
    public PlanQuality Quality { get; private set; }
    public double? Gap { get; private set; }
    public double Seconds { get; private set; }
    public string Solver { get; private set; } = "";
    public int Seed { get; private set; }

    /// <summary>SHA-256 of the problem the solver saw: the same hash and seed give the same plan.</summary>
    public string InputHash { get; private set; } = "";

    /// <summary>The settings in force (JSON).</summary>
    public string Parameters { get; private set; } = "{}";

    /// <summary>The plan (JSON): what the screens show and what approving applies.</summary>
    public string Result { get; private set; } = "{}";

    public int Violations { get; private set; }
    public decimal? CostIqd { get; private set; }
    public double? Km { get; private set; }
    public int Parcels { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public string? RequestedBy { get; private set; }
    public DateTimeOffset? AppliedAt { get; private set; }
    public string? AppliedBy { get; private set; }
    public uint Version { get; private set; }

    public static PlanningRun Record(
        Guid tenantId, PlanKind kind, string? hubCode, PlanQuality quality, double? gap, double seconds, string solver, int seed, string inputHash,
        PlanningPolicy policy, PlanPayload payload, decimal? cost, double? km, int parcels, string? by, DateTimeOffset now) => new()
        {
            Id = Guid.CreateVersion7(now),
            TenantId = tenantId,
            Kind = kind,
            HubCode = hubCode,
            Status = PlanRunStatus.Ready,
            Quality = quality,
            Gap = gap is { } g && double.IsFinite(g) ? Math.Round(g, 6) : null,
            Seconds = Math.Round(seconds, 3),
            Solver = solver,
            Seed = seed,
            InputHash = inputHash,
            Parameters = JsonSerializer.Serialize(policy, PlanningSettings.Json),
            Result = JsonSerializer.Serialize(payload, PlanningSettings.Json),
            Violations = payload.Violations.Count,
            CostIqd = cost,
            Km = km is { } k ? Math.Round(k, 3) : null,
            Parcels = parcels,
            RequestedAt = now,
            RequestedBy = by,
        };

    public PlanPayload Payload() => JsonSerializer.Deserialize<PlanPayload>(Result, PlanningSettings.Json) ?? PlanPayload.Empty;

    public void Apply(string? by, DateTimeOffset now)
    {
        EnsureReady(now);
        Status = PlanRunStatus.Applied;
        AppliedAt = now;
        AppliedBy = by;
    }

    public void Discard(string? by, DateTimeOffset now)
    {
        if (Status != PlanRunStatus.Ready)
        {
            throw new DomainException("planning.not_ready", "This plan was already applied or discarded.");
        }

        Status = PlanRunStatus.Discarded;
        AppliedAt = now;
        AppliedBy = by;
    }

    /// <summary>Approving: only a ready, fresh plan the checker found nothing wrong with. Checked before anything is changed.</summary>
    public void EnsureReady(DateTimeOffset now)
    {
        if (Status != PlanRunStatus.Ready)
        {
            throw new DomainException("planning.not_ready", "This plan was already applied or discarded.");
        }

        if (now - RequestedAt > FreshFor)
        {
            throw new DomainException("planning.expired", "This plan is too old to apply; plan again.", (int)FreshFor.TotalHours);
        }

        if (Violations > 0)
        {
            throw new DomainException("planning.not_valid", "The checker found problems in this plan; it cannot be applied.", Violations);
        }
    }

    public PlanRunSummary ToSummary() =>
        new(Id, Kind, HubCode, Status, Quality, Gap, Seconds, Solver, RequestedBy, RequestedAt, AppliedBy, AppliedAt, CostIqd, Km, Parcels, Version);
}

/// <summary>A planned truck departure to load when the plan is approved (the lane's next one).</summary>
internal sealed record TruckLoad(string FromHubCode, string ToHubCode, DateTimeOffset DepartAt, int Trucks, IReadOnlyList<Guid> ShipmentIds);

/// <summary>Everything a run keeps: the views and what approving it applies.</summary>
internal sealed record PlanPayload(TruckPlanView? Trucks, CourierPlanView? Couriers, ZonePlanView? Zones, IReadOnlyList<string> Violations, IReadOnlyList<TruckLoad> Loads)
{
    public static PlanPayload Empty { get; } = new(null, null, null, [], []);
}

/// <summary>A parcel's place in its courier's approved run: the courier app lists the run in this order.</summary>
internal sealed class CourierRunStop
{
    private CourierRunStop()
    {
    }

    public Guid ShipmentId { get; private set; }
    public Guid TenantId { get; private set; }
    public string CourierLabel { get; private set; } = "";
    public int Sequence { get; private set; }
    public Guid RunId { get; private set; }
    public DateTimeOffset PlannedAt { get; private set; }

    public static CourierRunStop Plan(Guid tenantId, Guid shipmentId, string courier, int sequence, Guid runId, DateTimeOffset now) => new()
    {
        TenantId = tenantId,
        ShipmentId = shipmentId,
        CourierLabel = courier,
        Sequence = sequence,
        RunId = runId,
        PlannedAt = now,
    };

    public void Replan(string courier, int sequence, Guid runId, DateTimeOffset now)
    {
        CourierLabel = courier;
        Sequence = sequence;
        RunId = runId;
        PlannedAt = now;
    }
}
