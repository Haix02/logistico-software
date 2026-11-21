using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.Optimization;
using Logistico.Optimization.LineHaul;
using Logistico.Optimization.Routing;
using Logistico.Optimization.Zoning;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Iraq;
using Microsoft.EntityFrameworkCore;
using GeoPoint = Logistico.Optimization.GeoPoint;

namespace Logistico.Modules.Shipments.Application;

/// <summary>Loads a company's planning settings (defaults when it never changed them).</summary>
internal sealed class PlanningPolicies(ShipmentsDbContext db, TimeProvider clock)
{
    public async Task<PlanningPolicy> GetAsync(Guid tenantId, CancellationToken ct) =>
        (await db.PlanningSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct))?.ToPolicy() ?? new PlanningPolicy();

    public async Task<PlanningSettings> GetForUpdateAsync(Guid tenantId, CancellationToken ct)
    {
        var settings = await db.PlanningSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (settings is null)
        {
            settings = PlanningSettings.Create(tenantId, new PlanningPolicy(), null, clock.GetUtcNow());
            db.PlanningSettings.Add(settings);
        }

        return settings;
    }
}

/// <summary>A problem for the engine and what the screens need to describe it.</summary>
internal sealed record TruckProblem(LineHaulProblem Problem, int EstimatedVolumes);

internal sealed record CourierProblem(HubInfo Hub, CourierRunProblem Problem, IReadOnlyDictionary<Guid, (string? Area, string Governorate)> Places);

internal sealed record ZoneProblem(HubInfo Hub, ZoningProblem Problem, int Couriers, int Deliveries);

/// <summary>
/// Turns the company's live data into planning problems (docs/20 §2): parcels waiting for trucks, parcels on a hub's
/// shelves for its couriers, and a hub's recent deliveries for zones. Positions come from the GPS of earlier deliveries
/// to the same customer, then from the middle of their area, then from their governorate (§4.3).
/// </summary>
internal sealed class PlanBuilder(ShipmentsDbContext db, HubDirectory hubs, ITenantContext tenant, TimeProvider clock)
{
    /// <summary>Iraq has kept +03:00 all year since 2008.</summary>
    internal static readonly TimeSpan Baghdad = TimeSpan.FromHours(3);

    /// <summary>Roads between cities are about 20 % longer than the straight line (the same factor as <see cref="HubNetwork.TransitHours"/>).</summary>
    internal const double InterCityRoadFactor = 1.2;

    private const int MaxParcels = 5_000;

    public async Task<TruckProblem> TrucksAsync(PlanningPolicy policy, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var network = await hubs.NetworkAsync(tenant.TenantId, ct);
        var lanes = new List<HaulLane>();
        foreach (var from in network.All)
        {
            foreach (var to in network.All.Where(h => h.Code != from.Code))
            {
                var hours = network.TransitHours(from.Code, to.Code);
                var km = from.Latitude == 0 || to.Latitude == 0
                    ? hours * 80.0
                    : HubNetwork.DistanceKm(from.Latitude, from.Longitude, to.Latitude, to.Longitude) * InterCityRoadFactor;
                lanes.Add(new HaulLane(from.Code, to.Code, Math.Round(km, 1), hours));
            }
        }

        var returning = ShipmentLifecycle.ReturnStatuses;
        var warehouse = ShipmentLifecycle.WarehouseStatuses;
        var rows = await db.Shipments.AsNoTracking()
            .Where(s => s.CurrentHubCode != null
                && (warehouse.Contains(s.Status) || s.Status == ShipmentStatus.ReturnInitiated)
                && (returning.Contains(s.Status) ? (s.OriginHubCode ?? s.DestinationHubCode) : s.DestinationHubCode) != s.CurrentHubCode)
            .OrderBy(s => s.RescheduledTo ?? s.EtaAt ?? s.PromisedAt).ThenBy(s => s.Id)
            .Take(MaxParcels)
            .Select(s => new
            {
                s.Id,
                s.TrackingNumber,
                s.CurrentHubCode,
                Target = returning.Contains(s.Status) ? s.OriginHubCode ?? s.DestinationHubCode : s.DestinationHubCode,
                Returning = returning.Contains(s.Status),
                s.GrossWeightKg,
                s.VolumeCbm,
                Promise = s.RescheduledTo ?? s.EtaAt ?? s.PromisedAt,
                s.ReturnRequestedAt,
                s.LastStatusAt,
            })
            .ToListAsync(ct);

        PlanningPolicy.TryParseTime(policy.HubArrivalCutoff, out var cutoff);
        var estimated = 0;
        var parcels = rows.Select(r =>
        {
            var kg = Math.Max(0.1, (double)r.GrossWeightKg);
            var m3 = (double)r.VolumeCbm;
            if (m3 <= 0)
            {
                m3 = kg * policy.AssumedM3PerKg;
                estimated++;
            }

            // A delivery is due at its customer's hub before the couriers leave on its promised day; a return within three days.
            var due = r.Returning
                ? (r.ReturnRequestedAt ?? r.LastStatusAt).AddDays(3)
                : new DateTimeOffset((r.Promise ?? now.AddDays(3)).ToOffset(Baghdad).Date + cutoff, Baghdad).ToUniversalTime();
            return new HaulParcel(r.Id, r.TrackingNumber, r.CurrentHubCode!, r.Target, kg, m3, due, r.Returning);
        }).ToList();

        var problem = new LineHaulProblem(
            now,
            Departures(policy, now),
            lanes,
            parcels,
            new TruckType(policy.TruckCapacityKg, policy.TruckCapacityM3, policy.TruckTripCostIqd, policy.TruckCostPerKmIqd),
            policy.MaxTrucksPerDeparture,
            policy.TrucksPerHub,
            AllowVia: false, // a truck's manifest carries parcels for its destination hub only (HubTransfer.Plan)
            HandlingHours: 2);
        return new TruckProblem(problem, estimated);
    }

    /// <summary>Every departure time of the settings within the planning days from now.</summary>
    internal static IReadOnlyList<DateTimeOffset> Departures(PlanningPolicy policy, DateTimeOffset now)
    {
        var today = now.ToOffset(Baghdad).Date;
        var end = now.AddDays(policy.PlanningDays);
        var times = policy.DepartureTimes.Select(t => PlanningPolicy.TryParseTime(t, out var time) ? time : (TimeSpan?)null).OfType<TimeSpan>().Distinct().ToList();
        return Enumerable.Range(0, policy.PlanningDays + 1)
            .SelectMany(day => times.Select(time => new DateTimeOffset(today.AddDays(day) + time, Baghdad).ToUniversalTime()))
            .Where(at => at > now && at <= end)
            .Order()
            .ToList();
    }

    public async Task<CourierProblem> CourierRunsAsync(string hubCode, PlanningPolicy policy, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hub = (await hubs.NetworkAsync(tenant.TenantId, ct)).Find(hubCode) ?? throw new DomainException("planning.unknown_hub", "This hub is not active in your network.", hubCode);
        var local = now.ToOffset(Baghdad);
        var endOfDay = new DateTimeOffset(local.Date.AddDays(1), Baghdad).ToUniversalTime(); // PostgreSQL keeps UTC
        PlanningPolicy.TryParseTime(policy.RunStart, out var runStart);
        var start = new DateTimeOffset(local.Date + runStart, Baghdad).ToUniversalTime();
        if (start < now)
        {
            start = now.AddMinutes(5 - (now.Minute % 5)).AddSeconds(-now.Second).AddMilliseconds(-now.Millisecond);
        }

        var warehouse = ShipmentLifecycle.WarehouseStatuses;
        var rows = await db.Shipments.AsNoTracking()
            .Where(s => s.CurrentHubCode == hub.Code && s.DestinationHubCode == hub.Code && warehouse.Contains(s.Status)
                && (s.RescheduledTo == null || s.RescheduledTo < endOfDay))
            .OrderBy(s => s.RescheduledTo ?? s.EtaAt ?? s.PromisedAt).ThenBy(s => s.Id)
            .Take(MaxParcels)
            .Select(s => new { s.Id, s.TrackingNumber, s.ConsigneePhone, s.DestinationCode, s.DestinationCity, s.GrossWeightKg, s.CodAmount, Due = s.RescheduledTo ?? s.EtaAt ?? s.PromisedAt })
            .ToListAsync(ct);

        var phones = rows.Select(r => r.ConsigneePhone).OfType<string>().Distinct().ToList();
        var byPhone = (await (from proof in db.DeliveryProofs.AsNoTracking()
                              join s in db.Shipments.AsNoTracking() on proof.ShipmentId equals s.Id
                              where proof.Delivered && proof.Latitude != null && proof.Longitude != null && s.ConsigneePhone != null && phones.Contains(s.ConsigneePhone)
                              orderby proof.RecordedAt descending
                              select new { s.ConsigneePhone, s.DestinationCode, Latitude = proof.Latitude!.Value, Longitude = proof.Longitude!.Value })
                .Take(10_000).ToListAsync(ct))
            .GroupBy(g => (g.ConsigneePhone!, g.DestinationCode))
            .ToDictionary(g => g.Key, g => new GeoPoint(g.First().Latitude, g.First().Longitude));
        var areas = await AreaCentresAsync(hub.Code, now, ct);

        var places = new Dictionary<Guid, (string? Area, string Governorate)>();
        var stops = rows.Select(r =>
        {
            var area = AreaKey(r.DestinationCity);
            places[r.Id] = (r.DestinationCity, r.DestinationCode);
            var (point, source) = r.ConsigneePhone is { } phone && byPhone.TryGetValue((phone, r.DestinationCode), out var gps)
                ? (gps, PointSource.Gps)
                : areas.TryGetValue((r.DestinationCode, area), out var centre)
                    ? (centre, PointSource.Area)
                    : (GovernorateCentre(r.DestinationCode, hub), PointSource.Governorate);
            var dueDay = r.Due?.ToOffset(Baghdad).Date;
            return new RouteStop(r.Id, r.TrackingNumber, point, source, area, r.DestinationCode, Math.Max(0.1, (double)r.GrossWeightKg), r.CodAmount ?? 0,
                MustGo: r.Due is null || r.Due < endOfDay, DaysLate: dueDay is { } d ? Math.Max(0, (local.Date - d).Days) : 0);
        }).ToList();

        var couriers = (await CouriersOfAsync(hub, ct)).Select(c => c.VehicleType == VehicleType.Motorcycle
                ? new RouteCourier(c.DisplayLabel, policy.MotorcycleStops, policy.MotorcycleKg, policy.MaxCashPerCourierIqd, policy.ShiftMinutes)
                : new RouteCourier(c.DisplayLabel, policy.VanStops, policy.VanKg, policy.MaxCashPerCourierIqd, policy.ShiftMinutes))
            .ToList();

        var problem = new CourierRunProblem(new GeoPoint(hub.Latitude, hub.Longitude), start, couriers, stops,
            new TravelModel(policy.RoadFactor, policy.CitySpeedKmh), policy.MinutesPerStop);
        return new CourierProblem(hub, problem, places);
    }

    public async Task<ZoneProblem> ZonesAsync(string hubCode, int? zones, PlanningPolicy policy, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hub = (await hubs.NetworkAsync(tenant.TenantId, ct)).Find(hubCode) ?? throw new DomainException("planning.unknown_hub", "This hub is not active in your network.", hubCode);
        var since = now.AddDays(-policy.ZoneHistoryDays);
        var delivered = await db.Shipments.AsNoTracking()
            .Where(s => s.ServiceType == ServiceType.Domestic // areas belong to governorates; freight from abroad has port codes
                && s.DestinationHubCode == hub.Code && s.Status == ShipmentStatus.Delivered && s.DeliveredAt >= since)
            .GroupBy(s => new { s.DestinationCode, s.DestinationCity })
            .Select(g => new { g.Key.DestinationCode, g.Key.DestinationCity, Count = g.Count() })
            .ToListAsync(ct);
        var centres = await AreaCentresAsync(hub.Code, now, ct);
        var weeks = policy.ZoneHistoryDays / 7.0;
        var units = delivered
            .GroupBy(d => (d.DestinationCode, Area: AreaKey(d.DestinationCity)))
            .Select(g =>
            {
                var label = g.OrderByDescending(x => x.Count).Select(x => x.DestinationCity).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim()
                    ?? IraqGovernorates.NameOrCode(g.Key.DestinationCode);
                var (point, source) = centres.TryGetValue(g.Key, out var centre) ? (centre, PointSource.Area) : (GovernorateCentre(g.Key.DestinationCode, hub), PointSource.Governorate);
                return new ZoneUnit($"{g.Key.DestinationCode}|{g.Key.Area}", label, g.Key.DestinationCode, point, source, Math.Round(g.Sum(x => x.Count) / weeks, 2));
            })
            .Where(u => u.Workload > 0)
            .ToList();
        var couriers = (await CouriersOfAsync(hub, ct)).Count;
        var problem = new ZoningProblem(units, Math.Max(1, zones ?? couriers), policy.ZoneBalancePercent / 100.0, policy.RoadFactor);
        return new ZoneProblem(hub, problem, couriers, delivered.Sum(d => d.Count));
    }

    /// <summary>Active couriers who work in the hub's governorates.</summary>
    private async Task<List<Courier>> CouriersOfAsync(HubInfo hub, CancellationToken ct)
    {
        var covers = hub.Covers.Append(hub.Governorate).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return await db.Couriers.AsNoTracking().Where(c => c.IsActive && covers.Contains(c.Governorate)).OrderBy(c => c.Code).ToListAsync(ct);
    }

    /// <summary>The middle of each area the hub delivered to, from the courier app's GPS at the door (last half year).</summary>
    private async Task<Dictionary<(string Governorate, string Area), GeoPoint>> AreaCentresAsync(string hubCode, DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-180);
        var points = await (from proof in db.DeliveryProofs.AsNoTracking()
                            join s in db.Shipments.AsNoTracking() on proof.ShipmentId equals s.Id
                            where proof.Delivered && proof.Latitude != null && proof.Longitude != null && s.DestinationHubCode == hubCode && proof.RecordedAt >= since
                            orderby proof.RecordedAt descending
                            select new { s.DestinationCode, s.DestinationCity, Latitude = proof.Latitude!.Value, Longitude = proof.Longitude!.Value })
            .Take(20_000).ToListAsync(ct);
        return points.GroupBy(p => (p.DestinationCode, AreaKey(p.DestinationCity)))
            .Where(g => g.Key.Item2.Length > 0)
            .ToDictionary(g => g.Key, g => new GeoPoint(g.Average(p => p.Latitude), g.Average(p => p.Longitude)));
    }

    /// <summary>"  حي  الجامعة " and "حي الجامعه" are the same area: folded (Arabic letter forms, case, digits) and single-spaced.</summary>
    internal static string AreaKey(string? city) => string.Join(' ', SearchText.Fold(city).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static GeoPoint GovernorateCentre(string code, HubInfo hub) =>
        IraqGovernorates.Find(code) is { } g ? new GeoPoint(g.Latitude, g.Longitude) : new GeoPoint(hub.Latitude, hub.Longitude);
}
