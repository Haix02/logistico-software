using Logistico.Modules.Merchants.Contracts;
using Logistico.Modules.Shipments.Application;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Infrastructure;

/// <summary>Development-only demo data: international freight and Instagram-shop deliveries in every stage across Iraq's hubs.</summary>
internal sealed class ShipmentsDemoSeeder(
    ShipmentsDbContext db,
    NumberGenerator numbers,
    IMerchantDirectory merchants,
    AutomationEngine automation,
    HubDirectory hubs,
    ITenantContext tenant,
    TimeProvider clock)
{
    private static readonly string[] Origins = ["CNSHA", "CNNGB", "CNSZX", "CNCAN", "TRIST", "TRMER", "AEJEA", "AEDXB", "DEHAM", "INNSA"];
    private static readonly string[] Ports = ["IQUQR", "IQBGW", "IQBSR", "IQEBL", "IQISU", "IQNJF"];
    private static readonly string[] Shippers = ["Ningbo Bright Lighting Co.", "Istanbul Textile Export A.S.", "Jebel Ali Trading FZE", "Guangzhou Auto Parts Ltd.", "Hamburg Medical GmbH", "Shenzhen Mobile Accessories"];
    private static readonly string[] Importers = ["شركة النور للتجارة العامة", "Erbil Mart", "Basra Oil Services", "مؤسسة الرافدين", "Baghdad Pharma Distribution", "شركة دجلة للإلكترونيات"];
    private static readonly string[] Customers = ["علي حسين", "زينب كاظم", "محمد جاسم", "نور الهدى", "حسن عبد الله", "مريم سعد", "Ahmed Saleh", "سجاد فلاح", "رقية عباس", "Karwan Ali", "فاطمة ناصر", "يوسف مهدي", "هبة طارق", "عمر خالد", "Dilan Omer", "سارة محمود"];
    private static readonly Dictionary<string, string[]> Areas = new()
    {
        ["IQ-BG"] = ["الكرادة", "المنصور", "زيونة", "الأعظمية", "الكاظمية", "حي الجامعة", "الدورة", "البلديات", "اليرموك", "الشعب"],
        ["IQ-BA"] = ["العشار", "الجزائر", "البراضعية", "الزبير", "الطويسة"],
        ["IQ-AR"] = ["Ankawa", "Dream City", "100m Road", "Shaqlawa Rd"],
        ["IQ-NA"] = ["حي الأمير", "حي الحسين", "الكوفة"],
        ["IQ-KA"] = ["حي الحسين", "باب بغداد", "حي العامل"],
        ["IQ-NI"] = ["الزهور", "المجموعة الثقافية", "حي الشرطة"],
        ["IQ-SU"] = ["Salim St", "Bakhtiari", "Raparin"],
        ["IQ-BB"] = ["الحلة - حي بابل", "الحلة - الجمعية"],
        ["IQ-DQ"] = ["الناصرية - حي أور", "الشطرة"],
        ["IQ-KI"] = ["حي العروبة", "رحيم آوه"],
        ["IQ-DI"] = ["بعقوبة - حي المعلمين", "بعقوبة - التحرير"],
        ["IQ-AN"] = ["الرمادي - حي الأندلس", "الفلوجة - حي الجولان"],
        ["IQ-WA"] = ["الكوت - حي الحسين", "الكوت - الجعفرية"],
        ["IQ-QA"] = ["الديوانية - حي العسكري", "الديوانية - العروبة"],
        ["IQ-MA"] = ["العمارة - حي الحسين", "العمارة - المعلمين"],
        ["IQ-DA"] = ["Malta", "Shindokha"],
        ["IQ-SD"] = ["تكريت - حي القادسية", "سامراء - المعتصم"],
    };

    // Where demo customers live (weights), roughly following where online shopping happens.
    private static readonly (string Code, int Weight)[] Demand =
    [
        ("IQ-BG", 40), ("IQ-BA", 9), ("IQ-AR", 7), ("IQ-NI", 7), ("IQ-SU", 5), ("IQ-NA", 5), ("IQ-KA", 4), ("IQ-BB", 4), ("IQ-DQ", 4),
        ("IQ-KI", 3), ("IQ-DI", 3), ("IQ-AN", 2), ("IQ-WA", 2), ("IQ-QA", 2), ("IQ-MA", 1), ("IQ-DA", 1), ("IQ-SD", 1),
    ];

    // Orders per hour of the day (Baghdad time): quiet mornings, busy evenings after work.
    private static readonly int[] HourWeights = [3, 2, 1, 1, 1, 1, 1, 2, 3, 4, 5, 6, 6, 6, 6, 6, 7, 8, 9, 11, 13, 14, 12, 7];

    // What each demo shop sells, with a price range in thousands of IQD.
    private static readonly Dictionary<string, (string Item, int MinK, int MaxK)[]> Catalogue = new(StringComparer.Ordinal)
    {
        ["بوتيك لمار"] = [("فستان سهرة", 45, 160), ("عباية خليجية", 35, 120), ("فستان صيفي", 25, 70), ("طقم بيجامة نسائي", 18, 45)],
        ["Baghdad Kicks"] = [("حذاء رياضي", 30, 110), ("سنيكرز Nike Air", 60, 180), ("حذاء جلد رجالي", 40, 120), ("نعال صيفي", 10, 25)],
        ["زهرة للتجميل"] = [("طقم مكياج", 25, 90), ("كريم ترطيب", 10, 35), ("سيروم فيتامين سي", 15, 45), ("أحمر شفاه ×2", 8, 25)],
        ["Erbil Gadgets"] = [("سماعات بلوتوث", 15, 75), ("شاحن سريع 20W", 10, 30), ("ساعة ذكية", 35, 160), ("باوربانك 20000", 20, 55), ("AirPods Pro", 120, 320)],
        ["عطور الفرات"] = [("عطر 100 مل", 25, 140), ("عطر عود", 60, 260), ("بخور ومبخرة", 15, 50), ("مجموعة عطور هدية", 45, 150)],
        ["Little Steps Kids"] = [("ملابس أطفال", 15, 55), ("ألعاب أطفال", 12, 60), ("حذاء أطفال", 15, 40), ("عربانة أطفال", 90, 240)],
        ["مطبخ ماما"] = [("كيك عيد ميلاد", 20, 60), ("حلويات منزلية", 12, 40), ("أواني مطبخ", 25, 110), ("قدر ضغط", 35, 90)],
        ["Mosul Handmade"] = [("سجادة يدوية", 60, 300), ("ديكور منزلي", 20, 80), ("مفرش طاولة مطرز", 25, 70), ("مسبحة يدوية", 15, 60)],
        ["دار الأناقة"] = [("حقيبة نسائية", 25, 120), ("ساعة يد نسائية", 30, 200), ("اكسسوارات ذهبية", 40, 350), ("نظارة شمسية", 15, 60)],
        ["Phone Case Hub"] = [("كفر موبايل ×3", 8, 25), ("كفر ايفون 15", 10, 30), ("حامي شاشة", 5, 15), ("كيبل شحن", 5, 15)],
        ["كتب ومكتبة الرصيف"] = [("كتب ×2", 10, 35), ("رواية", 8, 20), ("دفاتر وقرطاسية", 6, 25), ("مصحف", 15, 60)],
        ["Suli Fashion"] = [("قميص رجالي", 18, 55), ("بنطرون جينز", 22, 60), ("جاكيت شتوي", 40, 130), ("Hoodie", 25, 70)],
    };
    private static readonly string[] Landmarks = ["قرب جامع", "مقابل مدرسة", "خلف المول", "قرب سوق", "مقابل صيدلية", "قرب الملعب"];
    private static readonly string[] Items = ["فستان سهرة", "حذاء رياضي", "عطر 100 مل", "سماعات بلوتوث", "طقم مكياج", "كفر موبايل ×3", "ملابس أطفال", "كتب ×2", "ساعة يد", "حقيبة نسائية"];
    private static readonly string[] Couriers = ["مصطفى (Baghdad-01)", "حيدر (Baghdad-02)", "علي (Baghdad-03)", "كرار (South-01)", "Aram (North-01)"];
    private static readonly string[] Bins = ["A-01-1", "A-01-2", "A-02-1", "A-03-2", "B-01-1", "B-02-3", "C-01-1", "C-04-2", "R-01 (returns)"];

    private (string Name, string Phone, string Governorate, string Area, string Address)[]? customers;

    public async Task SeedAsync(int count, CancellationToken ct)
    {
        if (count <= 0)
        {
            return;
        }

        // Demo journeys follow the standard network, so the company starts with it.
        await hubs.EnsureStandardAsync(tenant.TenantId, ct);

        var random = new Random(2026);
        await SeedCouriersAsync(ct);

        if (!await db.Shipments.AnyAsync(s => s.ServiceType == ServiceType.International, ct))
        {
            for (var i = 0; i < count / 3; i++)
            {
                db.Shipments.Add(await InternationalAsync(random, ct));
            }

            await db.SaveChangesAsync(ct);
        }

        // The hub network demo (trucks, returns, requests) is seeded once.
        if (!await db.Transfers.AnyAsync(ct))
        {
            var shops = await merchants.ListActiveAsync(ct);
            if (shops.Count > 0)
            {
                var orders = new List<Shipment>(count);
                for (var i = 0; i < count; i++)
                {
                    orders.Add(await DomesticAsync(random, shops[random.Next(shops.Count)], ct));
                }

                var journeys = new DemoJourneys(random, clock.GetUtcNow(), tenant.TenantId, numbers);
                await journeys.RunAsync(orders, ct);
                db.Shipments.AddRange(orders);
                db.Transfers.AddRange(journeys.Transfers);
                db.Requests.AddRange(journeys.Requests);
                db.Notifications.AddRange(journeys.Notifications);
                db.Alerts.AddRange(journeys.Alerts);
                await db.SaveChangesAsync(ct);
            }
        }

        await SeedCustomsDeclarationsAsync(random, ct);
        await SeedCashHandoversAsync(ct);
        await SeedCodSettlementsAsync(ct);
        await SeedRatingsAsync(random, ct);
        await automation.SweepAsync(tenant.TenantId, ct);
    }

    /// <summary>Customers rate about half of last fortnight's deliveries from the tracking page: mostly happy, a few not.</summary>
    private async Task SeedRatingsAsync(Random random, CancellationToken ct)
    {
        if (await db.Shipments.AnyAsync(s => s.Rating != null, ct))
        {
            return;
        }

        string?[] happy = [null, null, "وصل بسرعة، شكراً", "المندوب محترم جداً", "Fast delivery, thank you", "التغليف ممتاز"];
        string?[] unhappy = ["تأخر يومين عن الموعد", "المندوب لم يتصل قبل الوصول", "The box arrived damaged"];
        var now = clock.GetUtcNow();
        var recent = await db.Shipments
            .Where(s => s.ServiceType == ServiceType.Domestic && s.Status == ShipmentStatus.Delivered && s.DeliveredAt > now.AddDays(-13))
            .OrderBy(s => s.DeliveredAt)
            .ToListAsync(ct);
        foreach (var shipment in recent.Where(_ => random.Next(100) < 45))
        {
            var stars = random.Next(100) switch { < 62 => 5, < 85 => 4, < 93 => 3, < 97 => 2, _ => 1 };
            var comments = stars >= 4 ? happy : unhappy;
            var ratedAt = shipment.DeliveredAt!.Value.AddHours(random.Next(1, 30));
            shipment.Rate(stars, comments[random.Next(comments.Length)], ratedAt < now ? ratedAt : now);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedCouriersAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        CourierRequest[] fleet =
        [
            new() { Code = "Baghdad-01", Name = "مصطفى", Phone = "07701112233", Governorate = "IQ-BG", VehicleType = VehicleType.Motorcycle, VehiclePlate = "BG-10421", Notes = "الكرخ — المنصور، اليرموك، حي الجامعة" },
            new() { Code = "Baghdad-02", Name = "حيدر", Phone = "07802223344", Governorate = "IQ-BG", VehicleType = VehicleType.Van, VehiclePlate = "BG-44890", Notes = "الرصافة — الكرادة، زيونة، الأعظمية" },
            new() { Code = "Baghdad-03", Name = "علي", Phone = "07713334455", Governorate = "IQ-BG", VehicleType = VehicleType.Motorcycle, VehiclePlate = "BG-31902", Notes = "بغداد — الكاظمية، الشعب، البلديات" },
            new() { Code = "South-01", Name = "كرار", Phone = "07814445566", Governorate = "IQ-BA", VehicleType = VehicleType.LightTruck, VehiclePlate = "BA-88210", Notes = "البصرة والجنوب — العشار، الجزائر، الزبير" },
            new() { Code = "North-01", Name = "Aram", Phone = "07505556677", Governorate = "IQ-AR", VehicleType = VehicleType.Van, VehiclePlate = "EBL-77310", Notes = "Erbil & Sulaymaniyah — Ankawa, Dream City" },
            new() { Code = "Najaf-01", Name = "مرتضى", Phone = "07806667788", Governorate = "IQ-NA", VehicleType = VehicleType.Motorcycle, VehiclePlate = "NJ-20511", Notes = "النجف وكربلاء وبابل" },
            new() { Code = "Mosul-01", Name = "يونس", Phone = "07707778899", Governorate = "IQ-NI", VehicleType = VehicleType.Van, VehiclePlate = "NI-61207", Notes = "الموصل — الجانب الأيسر والأيمن" },
        ];

        var existing = await db.Couriers.Select(c => c.Code).ToListAsync(ct);
        foreach (var req in fleet.Where(f => !existing.Contains(f.Code)))
        {
            db.Couriers.Add(Courier.Create(tenant.TenantId, req, now));
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedCustomsDeclarationsAsync(Random random, CancellationToken ct)
    {
        if (await db.CustomsDeclarations.AnyAsync(ct))
        {
            return;
        }

        var hsCodes = new[] { "8517.13.00", "6204.42.00", "8708.99.00", "9018.90.00", "9405.42.00", "3303.00.00" };
        var goods = new[] { "معدات اتصالات وإكسسوارات هواتف", "ألبسة ومنسوجات قطنية", "قطع غيار سيارات", "مستلزمات وأجهزة طبية", "وحدات إنارة LED صناعية", "عطور ومستحضرات تجميل" };
        var brokers = new[] { "شركة البوابة الجمركية — أم قصر", "مكتب الرافدين للتخليص الجمركي", "Al-Nahrain Customs Brokerage" };

        var shipments = await db.Shipments
            .Include(s => s.Events)
            .Where(s => s.ServiceType == ServiceType.International)
            .ToListAsync(ct);

        var seq = 10400;
        foreach (var s in shipments)
        {
            if (s.Status is ShipmentStatus.Booked or ShipmentStatus.PickedUp or ShipmentStatus.ReceivedAtOrigin or ShipmentStatus.Consolidated)
            {
                continue;
            }

            seq++;
            var now = s.UpdatedAt;
            var decl = CustomsDeclaration.CreateFor(s, now);
            var isHold = s.Status == ShipmentStatus.CustomsHold;
            var isReleased = s.Status is ShipmentStatus.Released or ShipmentStatus.AtDestinationWarehouse or ShipmentStatus.ReadyForDispatch or ShipmentStatus.OutForDelivery or ShipmentStatus.Delivered;
            var isAwaiting = s.Status is ShipmentStatus.DepartedOrigin or ShipmentStatus.InTransit or ShipmentStatus.ArrivedPortOfEntry;
            var idx = random.Next(hsCodes.Length);
            var rate = (idx % 3 + 1) * 5m;

            var req = new UpsertCustomsDeclarationRequest
            {
                CustomsOfficeCode = CustomsDutyCalculator.Offices.Any(o => o.Code == s.DestinationCode) ? s.DestinationCode : "IQUQR",
                Regime = "IM4",
                DeclarationNumber = isAwaiting ? null : $"2026-{s.DestinationCode}-IM4-{seq}",
                AssessmentNumber = isAwaiting ? null : $"ASM-2026-{seq}",
                ReceiptNumber = isReleased ? $"RCP-2026-{seq}" : null,
                ReleaseOrderNumber = isReleased ? $"REL-2026-{seq}" : null,
                SelectivityLane = isHold ? CustomsLane.Red : isReleased ? CustomsLane.Green : isAwaiting ? CustomsLane.Unassigned : CustomsLane.Yellow,
                HsCode = hsCodes[idx],
                GoodsDescription = goods[idx],
                CountryOfOrigin = s.OriginCode[..2],
                CooNumber = $"COO-{s.OriginCode[..2]}-2026-{seq}",
                CooEVerified = !isHold && !isAwaiting,
                CommercialInvoiceNumber = $"INV-2026-{seq}",
                InvoiceEVerified = !isAwaiting,
                CocNumber = $"COC-IQ-{seq}",
                CustomsValueUsd = s.DeclaredValue ?? 12_500m,
                DutyRatePercent = rate,
                HoldReason = isHold ? "Certificate of Origin pending electronic verification in Iraqi single window" : null,
                BrokerName = brokers[seq % brokers.Length],
                Action = CustomsAction.Save,
            };

            decl.Apply(req, s, "seed", now);
            db.CustomsDeclarations.Add(decl);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Couriers handed in each earlier day's cash in the evening (one short by 5,000 IQD); today's cash is still with them.
    /// </summary>
    private async Task SeedCashHandoversAsync(CancellationToken ct)
    {
        if (await db.CashHandovers.AnyAsync(ct))
        {
            return;
        }

        var baghdad = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(3));
        var today = new DateTimeOffset(baghdad.Date, baghdad.Offset).ToUniversalTime();
        var couriers = await db.Couriers.ToListAsync(ct);
        var collected = await db.Shipments
            .Where(s => s.CodCollectedAt != null && s.CodHandedOverAt == null && s.CourierName != null && s.DeliveredAt < today)
            .ToListAsync(ct);
        var first = true;
        foreach (var day in collected.GroupBy(s => new { s.CourierName, Day = s.DeliveredAt!.Value.ToOffset(TimeSpan.FromHours(3)).Date }).OrderBy(g => g.Key.Day))
        {
            if (couriers.FirstOrDefault(c => c.DisplayLabel == day.Key.CourierName) is not { } courier)
            {
                continue;
            }

            var parcels = day.ToList();
            var expected = parcels.Sum(p => p.CodAmount ?? 0m);
            var at = parcels.Max(p => p.DeliveredAt)!.Value.AddHours(2);
            var shortBy = first ? 5_000m : 0m;
            first = false;
            db.CashHandovers.Add(CashHandover.Create(tenant.TenantId, await numbers.CashHandoverNumberAsync(ct), courier, parcels, expected - shortBy,
                shortBy > 0 ? "نقص 5,000 د.ع — يُخصم من المندوب" : null, "seed", at));
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedCodSettlementsAsync(CancellationToken ct)
    {
        if (await db.CodSettlements.AnyAsync(ct))
        {
            return;
        }

        var now = clock.GetUtcNow();
        var cutoff = now.AddDays(-7);
        var olderDelivered = await db.Shipments
            .Where(s => s.ServiceType == ServiceType.Domestic && s.Status == ShipmentStatus.Delivered && s.CodHandedOverAt != null && s.CodSettlementId == null && s.CodAmount > 0 && s.MerchantId != null && s.DeliveredAt < cutoff)
            .OrderBy(s => s.DeliveredAt)
            .ToListAsync(ct);

        var seq = 0;
        var methods = new[] { PayoutMethod.ZainCash, PayoutMethod.QiCard, PayoutMethod.Cash, PayoutMethod.BankTransfer };
        foreach (var group in olderDelivered.GroupBy(s => new { MerchantId = s.MerchantId!.Value, MerchantName = s.MerchantName ?? "—" }).Take(4))
        {
            var batch = group.ToList();
            if (batch.Count == 0)
            {
                continue;
            }

            seq++;
            var settledAt = batch.Max(s => s.DeliveredAt)!.Value.AddHours(6);
            var settlement = CodSettlement.Create(
                tenant.TenantId,
                await numbers.SettlementNumberAsync(ct),
                group.Key.MerchantId,
                group.Key.MerchantName,
                batch,
                methods[(seq - 1) % methods.Length],
                $"REF-{settledAt:MMdd}-{1000 + seq}",
                "تسوية أسبوعية لمبالغ الدفع عند الاستلام",
                "seed",
                settledAt);
            db.CodSettlements.Add(settlement);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<Shipment> InternationalAsync(Random random, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var mode = (TransportMode)random.Next(0, 5);
        var bookedAt = now.AddDays(-random.Next(1, 90)).AddMinutes(-random.Next(0, 1440));
        var request = new CreateShipmentRequest
        {
            ServiceType = ServiceType.International,
            Mode = mode,
            ShipperName = Shippers[random.Next(Shippers.Length)],
            ConsigneeName = Importers[random.Next(Importers.Length)],
            ConsigneePhone = $"+96477{random.Next(10_000_000, 99_999_999)}",
            OriginCode = Origins[random.Next(Origins.Length)],
            DestinationCode = Ports[random.Next(Ports.Length)],
            Incoterm = random.Next(3) switch { 0 => "FOB", 1 => "CIF", _ => "EXW" },
            Pieces = random.Next(1, 40),
            GrossWeightKg = Math.Round((decimal)(random.NextDouble() * (mode is TransportMode.SeaFcl ? 18000 : 900)) + 1m, 3),
            VolumeCbm = Math.Round((decimal)(random.NextDouble() * (mode is TransportMode.SeaFcl ? 60 : 6)), 4),
            DeclaredValue = Math.Round((decimal)(random.NextDouble() * 50_000) + 100m, 2),
            DeclaredCurrency = "USD",
            CodCurrency = null,
            PromisedAt = bookedAt.AddDays(mode switch { TransportMode.Air or TransportMode.Courier => 7, TransportMode.Road => 12, _ => 40 }),
        };

        var shipment = Shipment.Book(tenant.TenantId, await numbers.TrackingNumberAsync(ct), request, null, "seed", bookedAt, IraqHubs.Default);
        ShipmentStatus[] path =
        [
            ShipmentStatus.ReceivedAtOrigin, ShipmentStatus.Consolidated, ShipmentStatus.DepartedOrigin, ShipmentStatus.InTransit,
            ShipmentStatus.ArrivedPortOfEntry, ShipmentStatus.CustomsPending, ShipmentStatus.Released, ShipmentStatus.AtDestinationWarehouse,
            ShipmentStatus.OutForDelivery, ShipmentStatus.Delivered,
        ];
        // Old freight has mostly arrived; recent bookings are spread along the route.
        var steps = (now - bookedAt).TotalDays > 30 && random.Next(10) > 0 ? path.Length : random.Next(0, path.Length + 1);
        var at = bookedAt;
        foreach (var next in path.Take(steps))
        {
            at = at.AddHours(random.Next(6, 96));
            if (at > now)
            {
                break;
            }

            if (next == ShipmentStatus.Released && random.Next(5) == 0)
            {
                shipment.AdvanceTo(ShipmentStatus.CustomsHold, at, new MilestoneDetails("Umm Qasr customs", "Certificate of Origin not e-verified"), "customs", "seed", at);
                break;
            }

            var hub = IraqHubs.Default.FindOrDefault(shipment.DestinationHubCode);
            switch (next)
            {
                case ShipmentStatus.AtDestinationWarehouse:
                    shipment.ReceiveAtHub(hub, Bins[random.Next(Bins.Length - 1)], Actor.Scan("seed"), at);
                    break;
                case ShipmentStatus.OutForDelivery:
                    shipment.DispatchToCourier(hub, hub.Code == IraqHubs.Basra ? "كرار (South-01)" : hub.Code == IraqHubs.Erbil ? "Aram (North-01)" : Couriers[random.Next(3)], Actor.Scan("seed"), at);
                    break;
                default:
                    var details = next is ShipmentStatus.ArrivedPortOfEntry or ShipmentStatus.CustomsPending
                        ? new MilestoneDetails(shipment.DestinationCode == "IQBGW" ? "Baghdad International Airport" : "Umm Qasr North Port")
                        : new MilestoneDetails();
                    shipment.AdvanceTo(next, at, details, "system", "seed", at);
                    break;
            }
        }

        return shipment;
    }

    private async Task<Shipment> DomesticAsync(Random random, MerchantLookupItem shop, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        // A pool of customers, a few of whom order often: the demo then has repeat buyers at the same address.
        customers ??= Enumerable.Range(0, 900)
            .Select(_ =>
            {
                var governorate = Demand[Weighted(random, Demand.Select(d => d.Weight).ToArray())].Code;
                var areas = Areas[governorate];
                return (Name: Customers[random.Next(Customers.Length)], Phone: $"07{random.Next(5, 10)}{random.Next(10_000_000, 99_999_999)}",
                    Governorate: governorate, Area: areas[random.Next(areas.Length)],
                    Address: $"محلة {random.Next(100, 999)}، زقاق {random.Next(1, 60)}، دار {random.Next(1, 120)}");
            })
            .ToArray();
        var customer = customers[(int)(customers.Length * Math.Pow(random.NextDouble(), 1.5))];

        // Most orders are recent; the rest reach back almost three months so periods can be compared.
        var daysAgo = random.Next(10) < 6 ? random.Next(0, 21) : random.Next(21, 80);
        var day = Endpoints.Api.StartOfBaghdadDay(now).AddDays(-daysAgo);
        var weekend = day.ToOffset(TimeSpan.FromHours(3)).DayOfWeek is DayOfWeek.Thursday or DayOfWeek.Friday;
        var hour = Weighted(random, HourWeights.Select((w, h) => weekend && h >= 17 ? w * 3 / 2 : w).ToArray());
        var bookedAt = day.AddHours(hour).AddMinutes(random.Next(60));
        if (bookedAt > now)
        {
            bookedAt = now.AddMinutes(-random.Next(5, 600));
        }

        var products = Catalogue.TryGetValue(shop.Name, out var sold) ? sold : Items.Select(i => (Item: i, MinK: 12, MaxK: 120)).ToArray();
        var product = products[random.Next(products.Length)];
        var request = new CreateShipmentRequest
        {
            ServiceType = ServiceType.Domestic,
            Mode = TransportMode.Road,
            MerchantId = shop.Id,
            ExternalReference = $"IG-{random.Next(1000, 9999)}",
            ConsigneeName = customer.Name,
            ConsigneePhone = customer.Phone,
            OriginCode = shop.Governorate,
            DestinationCode = customer.Governorate,
            DestinationCity = customer.Area,
            DeliveryAddress = customer.Address,
            Landmark = Landmarks[random.Next(Landmarks.Length)],
            ItemsDescription = product.Item,
            PickupRequired = random.Next(4) != 0,
            Pieces = random.Next(1, 4),
            GrossWeightKg = Math.Round((decimal)(random.NextDouble() * 4) + 0.2m, 3),
            CodAmount = random.Next(5) == 0 ? null : random.Next(product.MinK, product.MaxK + 1) * 1_000m,
            CodCurrency = "IQD",
            DeliveryFee = customer.Governorate == shop.Governorate ? 5_000m : 8_000m,
        };

        return Shipment.Book(tenant.TenantId, await numbers.TrackingNumberAsync(ct), request, new MerchantSnapshot(shop.Id, shop.Name, shop.Governorate), "seed", bookedAt, IraqHubs.Default);
    }

    /// <summary>An index picked with probability proportional to its weight.</summary>
    private static int Weighted(Random random, IReadOnlyList<int> weights)
    {
        var pick = random.Next(weights.Sum());
        for (var i = 0; i < weights.Count; i++)
        {
            pick -= weights[i];
            if (pick < 0)
            {
                return i;
            }
        }

        return weights.Count - 1;
    }
}
