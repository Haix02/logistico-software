using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.Optimization;
using Logistico.Optimization.LineHaul;
using Logistico.Optimization.Routing;
using Logistico.Optimization.Zoning;
using Logistico.SharedKernel;
using Microsoft.EntityFrameworkCore;
using GeoPoint = Logistico.Optimization.GeoPoint;

namespace Logistico.Modules.Shipments.Application;

/// <summary>
/// Runs the planners and applies approved plans (docs/20 §4). Every plan is re-checked by the engine's independent
/// checker and compared with today's practice before anyone sees it. Approving goes through the same domain methods as
/// the stations (trucks are planned with <see cref="HubTransfer.Plan"/>, parcels handed out by a dispatch scan), so the
/// state machine, manifests, customer messages and audit trail stay exactly as when people do it by hand.
/// </summary>
internal sealed class PlanningDesk(
    ShipmentsDbContext db,
    PlanningPolicies policies,
    PlanBuilder builder,
    TransferPlanner planner,
    WarehouseScanner scanner,
    ITenantContext tenant,
    TimeProvider clock)
{
    /// <summary>One plan at a time per company: solving takes the processors for several seconds.</summary>
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Running = new();

    public async Task<PlanningRun> RunAsync(StartPlanRequest request, string? by, CancellationToken ct)
    {
        var gate = Running.GetOrAdd(tenant.TenantId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, ct))
        {
            throw new DomainException("planning.busy", "A plan is being made for your company; try again in a moment.");
        }

        try
        {
            var policy = await policies.GetAsync(tenant.TenantId, ct);
            var options = new SolveOptions(policy.TimeLimitSeconds, Seed: 1, Workers: Math.Clamp(Environment.ProcessorCount, 1, 8));
            var run = request.Kind switch
            {
                PlanKind.Trucks => await TrucksAsync(policy, options, by, ct),
                PlanKind.CourierRuns => await CourierRunsAsync(request.HubCode!, policy, options, by, ct),
                _ => await ZonesAsync(request.HubCode!, request.Zones, policy, options, by, ct),
            };
            db.PlanningRuns.Add(run);
            await db.SaveChangesAsync(ct);
            return run;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<PlanningRun> TrucksAsync(PlanningPolicy policy, SolveOptions options, string? by, CancellationToken ct)
    {
        var (problem, estimated) = await builder.TrucksAsync(policy, ct);
        var plan = await Task.Run(() => LineHaulPlanner.Solve(problem, options), ct);
        var baseline = LineHaulBaseline.Plan(problem);
        var violations = LineHaulChecker.Check(problem, plan).Select(v => v.ToString()).ToList();

        var firstOnLane = plan.Departures.GroupBy(d => (d.From, d.To)).ToDictionary(g => g.Key, g => g.Min(d => d.DepartAt));
        var lateIds = plan.Routes.Where(r => r.LateHours > 0).Select(r => r.ParcelId).ToHashSet();
        var departures = plan.Departures.Select(d => new TruckDepartureView(d.From, d.To, d.DepartAt, d.ArriveAt, d.Trucks, d.Km, d.LoadKg, d.LoadM3,
            d.WeightFill, d.VolumeFill, d.Cost, d.ParcelIds.Count, d.ParcelIds.Count(lateIds.Contains), firstOnLane[(d.From, d.To)] == d.DepartAt)).ToList();
        var parcels = problem.Parcels.ToDictionary(p => p.Id);
        var attention = plan.Routes.Where(r => !r.Shipped || r.LateHours > 0)
            .OrderByDescending(r => r.Shipped ? r.LateHours : double.MaxValue).ThenBy(r => r.TrackingNumber, StringComparer.Ordinal)
            .Take(200)
            .Select(r => new PlannedParcelView(r.ParcelId, r.TrackingNumber, parcels[r.ParcelId].AtHub, parcels[r.ParcelId].ToHub, parcels[r.ParcelId].DueAt, r.ArriveAt, r.LateHours, r.Legs.Count > 1))
            .ToList();
        var loads = plan.Departures.Where(d => firstOnLane[(d.From, d.To)] == d.DepartAt)
            .Select(d => new TruckLoad(d.From, d.To, d.DepartAt, d.Trucks,
                d.ParcelIds.Where(id => plan.Routes.First(r => r.ParcelId == id).Legs[0].DepartAt == d.DepartAt).ToList()))
            .ToList();
        var view = new TruckPlanView(Kpis(plan.Kpis), Kpis(baseline.Kpis), departures, attention, problem.Truck.CapacityKg, problem.Truck.CapacityM3, estimated);

        return Record(PlanKind.Trucks, null, plan.Report, problem, policy, new PlanPayload(view, null, null, violations, loads), plan.Kpis.Cost, plan.Kpis.TruckKm, problem.Parcels.Count, by);
    }

    private async Task<PlanningRun> CourierRunsAsync(string hubCode, PlanningPolicy policy, SolveOptions options, string? by, CancellationToken ct)
    {
        var (hub, problem, places) = await builder.CourierRunsAsync(hubCode, policy, ct);
        var plan = await Task.Run(() => CourierRunPlanner.Solve(problem, options), ct);
        var baseline = CourierRunBaseline.Plan(problem);
        var violations = CourierRunChecker.Check(problem, plan).Select(v => v.ToString()).ToList();

        var stops = problem.Stops.ToDictionary(s => s.Id);
        var runs = plan.Routes.Select(r =>
        {
            var courier = problem.Couriers.First(c => c.Label == r.Courier);
            return new CourierRunView(r.Courier, r.Stops.Select(s =>
            {
                var stop = stops[s.StopId];
                return new RunStopView(s.StopId, s.TrackingNumber, s.Sequence, s.ArriveAt, s.KmSoFar, s.CashAfter, places[s.StopId].Area, places[s.StopId].Governorate,
                    stop.Point.Latitude, stop.Point.Longitude, (StopPrecision)stop.Source, stop.MustGo);
            }).ToList(), r.Km, r.Minutes, r.Cash, r.Kg, courier.MaxStops, courier.MaxKg, courier.MaxCash, courier.ShiftMinutes);
        }).ToList();
        var deferred = plan.Deferred.Select(d => new DeferredParcelView(d.StopId, d.TrackingNumber, d.MustGo, (DeferredBecause)d.Reason)).ToList();
        var view = new CourierPlanView(hub.Code, hub.Latitude, hub.Longitude, problem.StartAt, Kpis(plan.Kpis), Kpis(baseline.Kpis), runs, deferred,
            problem.Stops.Count(s => s.Source == PointSource.Gps), problem.Stops.Count(s => s.Source == PointSource.Area), problem.Stops.Count(s => s.Source == PointSource.Governorate));

        return Record(PlanKind.CourierRuns, hub.Code, plan.Report, problem, policy, new PlanPayload(null, view, null, violations, []), null, plan.Kpis.Km, problem.Stops.Count, by);
    }

    private async Task<PlanningRun> ZonesAsync(string hubCode, int? zones, PlanningPolicy policy, SolveOptions options, string? by, CancellationToken ct)
    {
        var (hub, problem, couriers, deliveries) = await builder.ZonesAsync(hubCode, zones, policy, ct);
        var plan = await Task.Run(() => ZonePlanner.Solve(problem, options), ct);
        var violations = ZoningChecker.Check(problem, plan).Select(v => v.ToString()).ToList();

        var units = problem.Units.ToDictionary(u => u.Key);
        var view = new ZonePlanView(hub.Code,
            plan.Zones.Select(z => new ZoneView(z.Number, z.CenterKey,
                z.UnitKeys.Select(k => units[k]).OrderByDescending(u => u.Workload)
                    .Select(u => new ZoneAreaView(u.Key, u.Label, u.Governorate, u.Workload, u.Point.Latitude, u.Point.Longitude, (StopPrecision)u.Source)).ToList(),
                z.Workload, z.Share, z.AverageKm)).ToList(),
            Math.Round(plan.Tolerance * 100, 1), policy.ZoneBalancePercent, Math.Round(plan.MaxDeviation * 100, 1), couriers, policy.ZoneHistoryDays, deliveries);

        return Record(PlanKind.Zones, hub.Code, plan.Report, problem, policy, new PlanPayload(null, null, view, violations, []), null, plan.WeightedKm, problem.Units.Count, by);
    }

    private PlanningRun Record<TProblem>(PlanKind kind, string? hub, SolveReport report, TProblem problem, PlanningPolicy policy, PlanPayload payload, decimal? cost, double? km, int parcels, string? by) =>
        PlanningRun.Record(tenant.TenantId, kind, hub, (PlanQuality)report.Status, report.Gap, report.Seconds, report.Solver, report.Seed, Hash(problem), policy, payload, cost, km, parcels, by, clock.GetUtcNow());

    /// <summary>Fingerprint of exactly what the solver saw.</summary>
    private static string Hash<T>(T problem) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(problem, PlanningSettings.Json)));

    private static TruckPlanKpis Kpis(LineHaulKpis k) => new(k.Trucks, k.TruckKm, k.Cost, k.Parcels, k.OnTime, k.Late, k.LateParcelHours, k.Unshipped, k.AverageFill);

    private static CourierPlanKpis Kpis(CourierRunKpis k) => new(k.Planned, k.Deferred, k.MustGoDeferred, k.Km, k.LongestMinutes, k.CouriersUsed, (int)k.StopsSpread);

    /// <summary>
    /// Approves a plan. Trucks: the next departure of each lane gets its trucks (the lane's loading truck, more when the plan
    /// needs more) with the planned parcels on their manifests. Courier runs: each courier's parcels are handed out by a
    /// dispatch scan in the planned order, which the courier app then follows. Zones are kept as the hub's current zones.
    /// Parcels that moved since the plan was made are skipped, never forced.
    /// </summary>
    public async Task<PlanApplyResult> ApplyAsync(PlanningRun run, string? by, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        run.EnsureReady(now);
        var payload = run.Payload();
        var applied = 0;
        var skipped = 0;
        var transfers = new List<string>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (run.Kind == PlanKind.Trucks)
        {
            var policy = await policies.GetAsync(tenant.TenantId, ct);
            foreach (var load in payload.Loads)
            {
                var ids = load.ShipmentIds.ToList();
                var parcels = await db.Shipments.Where(s => ids.Contains(s.Id)).OrderByDescending(s => s.GrossWeightKg).ThenBy(s => s.Id).ToListAsync(ct);
                var ready = parcels.Where(s => s.NeedsTransfer && s.CurrentHubCode == load.FromHubCode && s.TargetHubCode == load.ToHubCode).ToList();
                skipped += ids.Count - ready.Count;
                if (ready.Count == 0)
                {
                    continue;
                }

                var trucks = await db.Transfers.Include(t => t.Items)
                    .Where(t => t.Status == TransferStatus.Loading && t.FromHubCode == load.FromHubCode && t.ToHubCode == load.ToHubCode)
                    .OrderBy(t => t.CreatedAt)
                    .ToListAsync(ct);
                while (trucks.Count < load.Trucks)
                {
                    trucks.Add(await planner.OpenAsync(load.FromHubCode, load.ToHubCode, null, null, by, ct));
                }

                var ours = trucks.Select(t => t.Id).ToHashSet();
                var kg = trucks.ToDictionary(t => t.Id, t => (double)ready.Where(p => p.TransferId == t.Id).Sum(p => p.GrossWeightKg));
                foreach (var parcel in ready)
                {
                    if (parcel.TransferId is { } on && ours.Contains(on))
                    {
                        applied++;
                        continue;
                    }

                    if (parcel.TransferId is { } other)
                    {
                        var previous = await db.Transfers.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == other, ct);
                        if (previous is { Status: TransferStatus.Loading })
                        {
                            previous.Unplan(parcel, now);
                        }
                    }

                    // The emptiest truck takes the next (heaviest remaining) parcel.
                    var truck = trucks.Where(t => kg[t.Id] + (double)parcel.GrossWeightKg <= policy.TruckCapacityKg).OrderBy(t => kg[t.Id]).FirstOrDefault()
                        ?? trucks.OrderBy(t => kg[t.Id]).First();
                    truck.Plan(parcel, now);
                    kg[truck.Id] += (double)parcel.GrossWeightKg;
                    applied++;
                }

                transfers.AddRange(trucks.Select(t => t.Number));
            }
        }
        else if (run.Kind == PlanKind.CourierRuns && payload.Couriers is { } couriers)
        {
            var planned = couriers.Runs.SelectMany(r => r.Stops.Select(s => s.ShipmentId)).ToList();
            var existing = await db.CourierRunStops.Where(r => planned.Contains(r.ShipmentId)).ToDictionaryAsync(r => r.ShipmentId, ct);
            foreach (var courierRun in couriers.Runs.Where(r => r.Stops.Count > 0))
            {
                var ordered = courierRun.Stops.OrderBy(s => s.Sequence).ToList();
                var outcomes = await scanner.ScanAsync(ScanOperation.Dispatch, ordered.Select(s => s.TrackingNumber), couriers.HubCode, null, courierRun.Courier, null, null, by, ct);
                var ok = outcomes.Where(o => o.Ok && o.ShipmentId is not null).Select(o => o.ShipmentId!.Value).ToHashSet();
                foreach (var stop in ordered)
                {
                    if (!ok.Contains(stop.ShipmentId))
                    {
                        skipped++;
                        continue;
                    }

                    applied++;
                    if (existing.TryGetValue(stop.ShipmentId, out var row))
                    {
                        row.Replan(courierRun.Courier, stop.Sequence, run.Id, now);
                    }
                    else
                    {
                        db.CourierRunStops.Add(CourierRunStop.Plan(tenant.TenantId, stop.ShipmentId, courierRun.Courier, stop.Sequence, run.Id, now));
                    }
                }
            }
        }
        else
        {
            applied = payload.Zones?.Zones.Sum(z => z.Areas.Count) ?? 0;
        }

        run.Apply(by, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new PlanApplyResult(run.ToSummary(), applied, skipped, transfers.Distinct().ToList());
    }
}
