using Logistico.Modules.Shipments.Application;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;

namespace Logistico.Modules.Shipments.Infrastructure;

/// <summary>
/// Development-only: plays booked demo orders forward through Iraq's hub network with the real domain rules —
/// first mile, nightly line-haul trucks (21:00 Baghdad), last mile with failed attempts and automatic returns,
/// return trucks, hand-over to sellers, and customer requests.
/// </summary>
internal sealed class DemoJourneys(Random random, DateTimeOffset now, Guid tenantId, NumberGenerator numbers)
{
    private static readonly Dictionary<string, string[]> CouriersByHub = new()
    {
        [IraqHubs.Baghdad] = ["مصطفى (Baghdad-01)", "حيدر (Baghdad-02)", "علي (Baghdad-03)"],
        [IraqHubs.Basra] = ["كرار (South-01)"],
        [IraqHubs.Erbil] = ["Aram (North-01)"],
        [IraqHubs.Najaf] = ["مرتضى (Najaf-01)"],
        [IraqHubs.Mosul] = ["يونس (Mosul-01)"],
    };

    private static readonly Dictionary<string, string> Operators = new()
    {
        [IraqHubs.Baghdad] = "مصطفى — مخزن بغداد",
        [IraqHubs.Basra] = "كرار — مخزن البصرة",
        [IraqHubs.Erbil] = "Aram — Erbil hub",
        [IraqHubs.Najaf] = "حيدر — مخزن النجف",
        [IraqHubs.Mosul] = "يوسف — مخزن الموصل",
    };

    private static readonly string[] Shelves = ["A-01-1", "A-01-2", "A-02-1", "A-03-2", "B-01-1", "B-02-3", "C-01-1", "C-04-2"];
    private static readonly string[] Plates = ["BG-10421", "BG-55102", "BA-88210", "EBL-77310", "NJ-20511", "NI-61207"];
    private static readonly string[] Drivers = ["أبو علي", "سلام كاظم", "رعد حميد", "Shwan Ahmed", "قاسم جبار", "نزار يونس"];
    private static readonly AutomationPolicy Policy = new();

    public List<HubTransfer> Transfers { get; } = [];
    public List<ServiceRequest> Requests { get; } = [];
    public List<CustomerNotification> Notifications { get; } = [];
    public List<OpsAlert> Alerts { get; } = [];

    public async Task RunAsync(IReadOnlyList<Shipment> shipments, CancellationToken ct)
    {
        // Older orders have mostly finished their journey; a few stay stuck so the automation has real work to show.
        var journeys = shipments.Select(s => new Journey(s, s.CreatedAt, StageFor(s.CreatedAt))).ToList();
        foreach (var j in journeys)
        {
            FirstMile(j);
        }

        await LineHaulAsync(journeys.Where(j => !j.Done && j.S.NeedsTransfer).ToList(), ct);

        foreach (var j in journeys.Where(j => !j.Done && j.S.CurrentHubCode == j.S.DestinationHubCode && !j.S.IsReturning))
        {
            LastMile(j);
        }

        await LineHaulAsync(journeys.Where(j => !j.Done && j.S.IsReturning && j.S.NeedsTransfer).ToList(), ct);
        await HandOverReturnsAsync(journeys.Where(j => j.S.Status == ShipmentStatus.ReturnAtHub).ToList(), ct);
        await SeedRequestsAsync(journeys, ct);
        SeedMessages(journeys);
    }

    private void FirstMile(Journey j)
    {
        var s = j.S;
        if (j.Stage < 6 || !j.Step(random, 1, 8, now))
        {
            j.Done = true; // still waiting for pickup / drop-off
            return;
        }

        var originHub = IraqHubs.Default.FindOrDefault(s.OriginHubCode ?? s.DestinationHubCode);
        if (s.PickupRequired)
        {
            s.AdvanceTo(ShipmentStatus.PickedUp, j.At, new MilestoneDetails(Location: originHub.Code, CourierName: Courier(originHub.Code)), "scan", "seed", j.At);
            if (j.Stage < 10 || !j.Step(random, 1, 6, now))
            {
                j.Done = true;
                return;
            }
        }

        s.ReceiveAtHub(originHub, Shelf(), Actor.Scan(Operators[originHub.Code]), j.At);
        if (j.Stage < 16 && !s.NeedsTransfer)
        {
            j.Done = true; // on the shelf, waiting for a courier
        }
    }

    /// <summary>Groups parcels into nightly trucks per lane: departed, arrived, still on the road, or loading tonight.</summary>
    private async Task LineHaulAsync(List<Journey> travellers, CancellationToken ct)
    {
        var buckets = travellers
            .GroupBy(j => (From: j.S.CurrentHubCode!, To: j.S.TargetHubCode, Departs: NextDeparture(j.At.AddHours(1))))
            .OrderBy(g => g.Key.Departs);

        foreach (var bucket in buckets)
        {
            var (from, to, departs) = bucket.Key;
            var opened = bucket.Min(j => j.At);
            var lane = Transfers.Count;
            var transfer = HubTransfer.Open(tenantId, await numbers.TransferNumberAsync(ct), from, to, Plates[lane % Plates.Length], Drivers[lane % Drivers.Length], "automation", opened);
            Transfers.Add(transfer);

            foreach (var j in bucket.OrderBy(j => j.At))
            {
                transfer.Plan(j.S, j.At);
            }

            if (departs > now)
            {
                // Tonight's truck: some parcels already scanned onto it, the rest only planned.
                foreach (var j in bucket)
                {
                    if (random.Next(3) > 0 && j.At.AddMinutes(30) < now)
                    {
                        transfer.Load(j.S, j.At.AddMinutes(30));
                    }

                    j.Done = true;
                }

                continue;
            }

            foreach (var j in bucket)
            {
                transfer.Load(j.S, departs.AddMinutes(-40));
            }

            var transitHours = IraqHubs.Default.TransitHours(from, to);
            transfer.Depart(null, null, $"SEAL-{random.Next(10000, 99999)}", transitHours, Operators[from], departs);
            foreach (var j in bucket)
            {
                j.S.DepartOnTransfer(transfer.Number, from, to, transitHours, Actor.Scan(Operators[from]), departs);
                j.At = departs;
            }

            var arrives = departs.AddHours(transitHours).AddMinutes(random.Next(10, 150));
            if (arrives.AddMinutes(30) > now)
            {
                foreach (var j in bucket)
                {
                    j.Done = true; // still on the road
                }

                continue;
            }

            var toHub = IraqHubs.Default.FindOrDefault(to);
            var minute = 0;
            var missingOne = bucket.Count() > 3 && random.Next(25) == 0;
            foreach (var j in bucket)
            {
                if (missingOne && minute == 0)
                {
                    minute++;
                    j.Done = true;
                    continue; // never scanned off the truck
                }

                var at = arrives.AddMinutes(minute++);
                transfer.Receive(j.S, Operators[to], at);
                j.S.ReceiveAtHub(toHub, j.S.IsReturning ? "R-01" : Shelf(), Actor.Scan(Operators[to]), at);
                j.At = at;
            }

            if (transfer.Status == TransferStatus.InTransit)
            {
                foreach (var item in transfer.Close("Seal intact, one parcel not found on the truck", Operators[to], arrives.AddHours(2)))
                {
                    Alerts.Add(OpsAlert.ForTransfer(transfer, AlertKind.MissingInTransfer, AlertSeverity.Critical, null, transfer.Number, arrives.AddHours(2), item.ShipmentId, item.TrackingNumber));
                }
            }
        }
    }

    private void LastMile(Journey j)
    {
        var s = j.S;
        var hub = IraqHubs.Default.FindOrDefault(s.DestinationHubCode);
        if (j.Stage < 30 || !j.Step(random, 2, 14, now))
        {
            j.Done = true; // on the shelf at the destination hub
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var courier = Courier(hub.Code);
            s.DispatchToCourier(hub, courier, Actor.Scan(Operators[hub.Code]), j.At);
            if (j.Stage < 40 || !j.Step(random, 1, 8, now))
            {
                j.Done = true; // out for delivery now
                return;
            }

            // Expensive cash-on-delivery orders are refused at the door more often.
            var expensive = s.CodAmount > 150_000;
            var fails = random.Next(100) < (j.Stage < 60 ? 35 : 15) + (expensive ? 12 : 0);
            if (!fails)
            {
                s.AdvanceTo(ShipmentStatus.Delivered, j.At, new MilestoneDetails(s.DestinationCity, CourierName: courier), "scan", courier, j.At);
                j.Done = true;
                return;
            }

            var reason = expensive && random.Next(2) == 0 ? DeliveryFailureReason.CustomerRefused : (DeliveryFailureReason)random.Next(0, 6);
            var decision = s.RecordFailedAttempt(reason, courier, null, Policy, new Actor(courier, "scan"), j.At);
            if (!j.Step(random, 3, 12, now))
            {
                j.Done = true; // failed today, still with the courier
                return;
            }

            // The courier brings the parcel back to the hub at the end of the run.
            s.ReceiveAtHub(hub, decision.FollowUp == FailureFollowUp.Return ? "R-01" : Shelf(), Actor.Scan(Operators[hub.Code]), j.At);
            if (decision.FollowUp == FailureFollowUp.Return)
            {
                return; // returns travel back on the return trucks
            }

            if (!j.Step(random, 10, 26, now))
            {
                j.Done = true; // rescheduled, waiting for the next attempt
                return;
            }
        }
    }

    private async Task HandOverReturnsAsync(List<Journey> waiting, CancellationToken ct)
    {
        foreach (var group in waiting.Where(j => j.At.AddDays(1) < now && random.Next(10) < 6).GroupBy(j => (j.S.MerchantId, j.S.CurrentHubCode)))
        {
            var number = await numbers.ReturnManifestNumberAsync(ct);
            var at = group.Max(j => j.At).AddHours(random.Next(4, 20));
            if (at > now)
            {
                continue;
            }

            foreach (var j in group)
            {
                j.S.HandOverToSeller(number, "مندوب المتجر", 0m, Actor.Scan(Operators[j.S.CurrentHubCode!]), at);
            }
        }
    }

    private async Task SeedRequestsAsync(List<Journey> journeys, CancellationToken ct)
    {
        var candidates = journeys.Select(j => j.S).OrderBy(_ => random.Next()).ToList();

        // Customers picking a new day after a failed attempt (applied automatically).
        foreach (var s in candidates.Where(s => s.Status == ShipmentStatus.AtWarehouse && s.LastFailureReason is not null).Take(6))
        {
            var at = Min(s.LastStatusAt.AddHours(2), now.AddMinutes(-1));
            var request = await NewAsync(s, new CustomerRequest { Type = RequestType.Reschedule, PreferredDate = DeliveryPromise.NextWorkingDay(now), PhoneLast4 = "0000" }, RequestSource.Customer, "customer", at, ct);
            request.ApplyTo(s, IraqHubs.Default, new Actor("customer", "system"), at);
            request.Resolve(null, "automation", at);
        }

        // Address corrections waiting for a person (the governorate changes).
        foreach (var s in candidates.Where(s => s.Status is ShipmentStatus.AtWarehouse or ShipmentStatus.Booked && s.LastFailureReason is null).Take(4))
        {
            await NewAsync(s, new CustomerRequest { Type = RequestType.ChangeAddress, NewGovernorate = "IQ-KA", NewCity = "حي الحسين", NewAddress = "قرب مستشفى الحسين، شارع 60", PhoneLast4 = "0000" },
                RequestSource.Customer, "customer", Min(s.LastStatusAt.AddHours(1), now.AddMinutes(-1)), ct);
        }

        // Complaints about parcels with a courier (some overdue).
        foreach (var s in candidates.Where(s => s.Status == ShipmentStatus.OutForDelivery).Take(3))
        {
            await NewAsync(s, new CreateServiceRequest { Type = RequestType.Complaint, Details = "الزبون يقول إن المندوب لم يتصل قبل الوصول" },
                RequestSource.Staff, "خدمة العملاء", Min(s.LastStatusAt.AddHours(1), now.AddMinutes(-1)), ct);
        }

        // Returns asked for after delivery.
        foreach (var s in candidates.Where(s => s.Status == ShipmentStatus.Delivered && s.DeliveredAt > now.AddDays(-6)).Take(4))
        {
            await NewAsync(s, new CustomerRequest { Type = RequestType.Return, Details = "المقاس غير مناسب", PhoneLast4 = "0000" },
                RequestSource.Customer, "customer", Min(s.DeliveredAt!.Value.AddHours(20), now.AddMinutes(-1)), ct);
        }
    }

    private async Task<ServiceRequest> NewAsync(Shipment s, RequestFields fields, RequestSource source, string by, DateTimeOffset at, CancellationToken ct)
    {
        var request = ServiceRequest.Create(s, await numbers.RequestNumberAsync(ct), fields, source, by, Policy.RequestSlaHours, at);
        Requests.Add(request);
        return request;
    }

    private void SeedMessages(List<Journey> journeys)
    {
        foreach (var s in journeys.Select(j => j.S).Where(s => s.LastStatusAt > now.AddHours(-30)))
        {
            var template = s.Status switch
            {
                ShipmentStatus.OutForDelivery => NotificationTemplate.OutForDelivery,
                ShipmentStatus.DeliveryFailed or ShipmentStatus.AtWarehouse when s.LastFailureReason is not null => NotificationTemplate.DeliveryFailed,
                ShipmentStatus.ReturnInitiated or ShipmentStatus.ReturnInTransit => NotificationTemplate.ReturnStarted,
                ShipmentStatus.Delivered => NotificationTemplate.Delivered,
                _ => (NotificationTemplate?)null,
            };
            if (template is null || s.ConsigneePhone is null)
            {
                continue;
            }

            var message = CustomerNotification.Queue(s, template.Value, CustomerMessages.Compose(s, template.Value), s.LastStatusAt);
            if (s.LastStatusAt < now.AddHours(-3))
            {
                message.Mark(NotificationStatus.Sent, "خدمة العملاء", s.LastStatusAt.AddMinutes(20));
            }

            Notifications.Add(message);
        }
    }

    private string Courier(string hubCode)
    {
        var couriers = CouriersByHub[hubCode];
        return couriers[random.Next(couriers.Length)];
    }

    private string Shelf() => Shelves[random.Next(Shelves.Length)];

    private int StageFor(DateTimeOffset createdAt)
    {
        var ageDays = (now - createdAt).TotalDays;
        if (ageDays < 3)
        {
            return random.Next(100);
        }

        // A few recent orders stay stuck so the automation has work to show; older ones have all finished.
        return ageDays < 21 && random.Next(100) < 6 ? random.Next(60) : 60 + random.Next(40);
    }

    /// <summary>Trucks leave every hub twice a day: 08:00 and 21:00 Baghdad time (05:00 and 18:00 UTC).</summary>
    private static DateTimeOffset NextDeparture(DateTimeOffset after)
    {
        var day = after.UtcDateTime.Date;
        foreach (var hour in new[] { 5, 18, 29 })
        {
            var departure = new DateTimeOffset(day.AddHours(hour), TimeSpan.Zero);
            if (departure > after)
            {
                return departure;
            }
        }

        return new DateTimeOffset(day.AddHours(29), TimeSpan.Zero);
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private sealed class Journey(Shipment shipment, DateTimeOffset at, int stage)
    {
        public Shipment S { get; } = shipment;
        public DateTimeOffset At { get; set; } = at;
        public int Stage { get; } = stage;
        public bool Done { get; set; }

        /// <summary>Moves the clock forward; false when that would be in the future (the parcel stops here).</summary>
        public bool Step(Random random, int minHours, int maxHours, DateTimeOffset now)
        {
            var next = At.AddHours(random.Next(minHours, maxHours)).AddMinutes(random.Next(0, 59));
            if (next > now.AddMinutes(-5))
            {
                return false;
            }

            At = next;
            return true;
        }
    }
}
