using Logistico.Modules.Merchants.Contracts;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel.Identity;
using Xunit.Abstractions;

namespace Logistico.Qa;

/// <summary>
/// A whole working day on the real server, with people working at the same time: sellers book orders, the hub receives
/// them, a truck takes some to Basra, couriers deliver, fail and bring parcels back, customers follow their parcels,
/// the office counts the cash and pays the sellers. At the end every parcel must be where it should be and every
/// dinar accounted for. Each agent signs in with its own account, through the API the mobile app uses.
/// </summary>
[Collection("Qa")]
[Trait("Category", "Qa")]
public sealed class DeliveryDayTests(QaServer server, ITestOutputHelper output)
{
    private const int OrdersPerSeller = 12;
    private const string CourierPassword = "Courier#QA2026";

    private readonly Journal journal = new();

    private sealed record Parcel(CreateShipmentResponse Order, Guid Shop, string Destination, string Phone, decimal Cod, decimal Fee)
    {
        public string Tracking => Order.TrackingNumber;

        public string Hub => Destination == "IQ-BA" ? IraqHubs.Basra : IraqHubs.Baghdad;
    }

    private sealed record Rider(Guid Id, string Code, string Label, string Hub, Agent App);

    [Fact]
    public async Task A_delivery_day_ends_with_every_parcel_and_every_dinar_accounted_for()
    {
        var day = Guid.NewGuid().ToString("N")[..6];
        var admin = await Agent.SignInAsync(server, journal, "admin@logistico.local");
        var hub = await Agent.SignInAsync(server, journal, "ops@logistico.local");
        var finance = await Agent.SignInAsync(server, journal, "finance@logistico.local");
        var customer = Agent.Anonymous(server, journal, "customer");

        // Morning: the company takes on three shops and three couriers, each with their own account.
        var shops = await Task.WhenAll(Enumerable.Range(1, 3).Select(n => NewShopAsync(admin, day, n)));
        var riders = new[]
        {
            await NewCourierAsync(admin, $"BG{day}A", "IQ-BG", IraqHubs.Baghdad),
            await NewCourierAsync(admin, $"BG{day}B", "IQ-BG", IraqHubs.Baghdad),
            await NewCourierAsync(admin, $"BA{day}A", "IQ-BA", IraqHubs.Basra),
        };

        // Sellers book at the same time, each order priced by the company's quote.
        var parcels = (await Task.WhenAll(shops.Select(shop => BookAsync(shop.Seller, shop.Id, day)))).SelectMany(p => p).ToList();
        Assert.Equal(shops.Length * OrdersPerSeller, parcels.Select(p => p.Tracking).Distinct().Count());

        // The Baghdad hub scans everything in at once; the sort plan picks shelves and Basra parcels wait for a truck.
        var received = await Task.WhenAll(parcels.Select(p => ScanAsync(hub, ScanOperation.Receive, p.Tracking, IraqHubs.Baghdad)));
        Assert.All(received, r => Assert.True(r.Ok, $"{r.TrackingNumber}: {r.Message}"));
        Assert.All(received, r => Assert.False(string.IsNullOrEmpty(r.WarehouseBin), $"{r.TrackingNumber} got no shelf: {r.Message}"));
        Assert.All(received.Where(r => r.DestinationCode == "IQ-BA"), r => Assert.Equal(IraqHubs.Basra, r.NextHubCode));

        await TruckToBasraAsync(hub, parcels.Where(p => p.Hub == IraqHubs.Basra).ToList());

        // Each parcel goes out with a courier of the hub that delivers it.
        var runs = parcels.GroupBy(p => p.Hub).SelectMany(g =>
        {
            var hubRiders = riders.Where(r => r.Hub == g.Key).ToList();
            return g.Select((parcel, i) => (Parcel: parcel, Rider: hubRiders[i % hubRiders.Count]));
        }).ToList();
        await Task.WhenAll(runs.Select(r => ScanAsync(hub, ScanOperation.Dispatch, r.Parcel.Tracking, r.Parcel.Hub, courier: r.Rider.Label)));

        foreach (var rider in riders)
        {
            var run = await rider.App.GetAsync<CourierRun>(ShipmentsApiRoutes.CourierRun);
            var mine = runs.Where(r => r.Rider == rider).Select(r => r.Parcel).ToList();
            Assert.Equal(mine.Select(p => p.Tracking).Order(), run.Stops.Select(s => s.TrackingNumber).Order());
            Assert.Equal(mine.Sum(p => p.Cod), run.Stops.Sum(s => s.CodAmount ?? 0));
        }

        // Couriers work their runs at the same time. Every sixth customer does not answer; every ninth refuses.
        var outcomes = await Task.WhenAll(runs.Select((r, i) => AttemptAsync(r.Rider, r.Parcel, i)));
        var noAnswer = outcomes.Where(o => o.Result == Attempt.NoAnswer).ToList();
        var refused = outcomes.Where(o => o.Result == Attempt.Refused).ToList();
        Assert.NotEmpty(noAnswer);
        Assert.NotEmpty(refused);

        // Parcels nobody answered for come back to the hub and go out again the same day.
        await Task.WhenAll(noAnswer.Select(o => ScanAsync(hub, ScanOperation.Receive, o.Parcel.Tracking, o.Parcel.Hub)));
        await Task.WhenAll(noAnswer.Select(o => ScanAsync(hub, ScanOperation.Dispatch, o.Parcel.Tracking, o.Parcel.Hub, courier: o.Rider.Label)));
        await Task.WhenAll(noAnswer.Select(o => o.Rider.App.OkAsync(ShipmentsApiRoutes.CourierDelivered(o.Parcel.Order.Id), Proof())));

        var delivered = outcomes.Where(o => o.Result != Attempt.Refused).ToList();

        // Customers follow their parcels without seeing anything private.
        foreach (var outcome in outcomes.Take(10))
        {
            var view = await customer.GetAsync<PublicTrackingView>(ShipmentsApiRoutes.Track(outcome.Parcel.Tracking));
            Assert.Equal(outcome.Result == Attempt.Refused ? ShipmentStatus.ReturnInitiated : ShipmentStatus.Delivered, view.Status);
            var raw = await customer.Http.GetStringAsync("/" + ShipmentsApiRoutes.Track(outcome.Parcel.Tracking));
            Assert.DoesNotContain("consigneePhone", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("0781", raw, StringComparison.Ordinal);
        }

        var happy = delivered[0].Parcel;
        var rated = await customer.PostAsync(ShipmentsApiRoutes.TrackRating(happy.Tracking), new CustomerRating { Stars = 5, PhoneLast4 = happy.Phone[^4..] });
        Assert.True(rated.IsSuccessStatusCode, await rated.Content.ReadAsStringAsync());

        // Evening: each courier holds exactly what their delivered parcels collected, and hands it in.
        foreach (var rider in riders)
        {
            var expected = delivered.Where(o => o.Rider == rider).Sum(o => o.Parcel.Cod);
            var cash = await finance.GetAsync<CourierCash>(ShipmentsApiRoutes.CourierCash(rider.Id));
            Assert.Equal(expected, cash.ExpectedIqd);
            await hub.PostAsync<CashHandoverItem>(ShipmentsApiRoutes.CourierCash(rider.Id), new ReceiveCashRequest { ReceivedIqd = cash.ExpectedIqd });
            Assert.Equal(0, (await finance.GetAsync<CourierCash>(ShipmentsApiRoutes.CourierCash(rider.Id))).ExpectedIqd);
        }

        // The refused parcels travel back to the seller's hub.
        foreach (var outcome in refused.Where(o => o.Parcel.Hub == IraqHubs.Baghdad))
        {
            await ScanAsync(hub, ScanOperation.Receive, outcome.Parcel.Tracking, IraqHubs.Baghdad);
        }

        // Each shop is owed exactly its delivered cash minus delivery fees, and is paid that.
        foreach (var shop in shops)
        {
            var theirs = delivered.Where(o => o.Parcel.Shop == shop.Id).Select(o => o.Parcel).ToList();
            var owed = theirs.Sum(p => p.Cod - p.Fee);
            var home = await shop.Seller.GetAsync<SellerHome>(ShipmentsApiRoutes.Seller);
            Assert.Equal((owed, theirs.Count), (home.OwedIqd, home.OwedOrders));

            var paid = await finance.PostAsync<CodSettlementItem>(ShipmentsApiRoutes.FinanceSettlements,
                new CreateCodSettlementRequest { MerchantId = shop.Id, PayoutMethod = PayoutMethod.ZainCash, PayoutReference = $"QA-{day}" });
            Assert.Equal((theirs.Count, theirs.Sum(p => p.Cod), theirs.Sum(p => p.Fee), owed),
                (paid.ShipmentCount, paid.GrossCodAmount, paid.DeliveryFeesAmount, paid.NetPayoutAmount));

            var after = await shop.Seller.GetAsync<SellerHome>(ShipmentsApiRoutes.Seller);
            Assert.Equal((0m, 0, owed), (after.OwedIqd, after.OwedOrders, after.PaidIqd));
            var statement = await shop.Seller.Http.GetStringAsync("/" + ShipmentsApiRoutes.SellerStatement(paid.Id));
            Assert.All(theirs, p => Assert.Contains(p.Tracking, statement, StringComparison.Ordinal));

            // Paying twice finds nothing left to pay.
            var again = await finance.PostAsync(ShipmentsApiRoutes.FinanceSettlements, new CreateCodSettlementRequest { MerchantId = shop.Id });
            Assert.False(again.IsSuccessStatusCode, "A second settlement of the same parcels went through.");
        }

        // Nothing got lost: every parcel ends in the state its story says, with a timeline that tells it.
        foreach (var outcome in outcomes)
        {
            var order = await admin.GetAsync<ShipmentDetails>(ShipmentsApiRoutes.ById(outcome.Parcel.Order.Id));
            Assert.Equal(outcome.Result == Attempt.Refused ? ReturnedStatus(outcome.Parcel) : ShipmentStatus.Delivered, order.Status);
            Assert.Equal(outcome.Result == Attempt.NoAnswer ? 2 : 1, order.DeliveryAttempts);
        }

        var report = journal.Report();
        output.WriteLine(report);
        await File.WriteAllTextAsync(Path.Combine(QaServer.ResultsFolder, "delivery-day.md"), "# Delivery day\n\n" + report);
        Assert.Empty(journal.ServerErrors.Select(c => $"{c.Agent}: {c.Method} {c.Path} → {c.Status}: {c.Failure}"));
    }

    private static ShipmentStatus ReturnedStatus(Parcel parcel) =>
        parcel.Hub == IraqHubs.Baghdad ? ShipmentStatus.ReturnAtHub : ShipmentStatus.ReturnInitiated;

    private async Task<(Guid Id, Agent Seller)> NewShopAsync(Agent admin, string day, int number)
    {
        var created = await admin.PostAsync<CreateMerchantResponse>(MerchantsApiRoutes.Merchants, new MerchantRequest
        {
            Name = $"متجر الاختبار {number} {day}",
            InstagramHandle = $"qa.shop.{day}.{number}",
            ContactName = "صاحب المتجر",
            Phone = "0770 111 2233",
            Governorate = "IQ-BG",
        });
        var email = $"seller{number}.{day}@qa.test";
        await admin.OkAsync(TeamApiRoutes.Team, new CreateTeamMemberRequest
        {
            Email = email,
            DisplayName = $"بائع {number}",
            Role = StaffRoles.Seller,
            MerchantId = created.Id,
            Password = CourierPassword,
        });
        return (created.Id, await Agent.SignInAsync(server, journal, email, CourierPassword));
    }

    private async Task<Rider> NewCourierAsync(Agent admin, string code, string governorate, string hub)
    {
        var name = $"مندوب {code}";
        await admin.OkAsync(ShipmentsApiRoutes.FleetCouriers, new CourierRequest { Code = code, Name = name, Phone = "07802223344", Governorate = governorate });
        var courier = (await admin.GetAsync<List<CourierLookupItem>>(ShipmentsApiRoutes.FleetLookup)).Single(c => c.Code == code);
        var email = $"{code.ToLowerInvariant()}@qa.test";
        await admin.OkAsync(TeamApiRoutes.Team, new CreateTeamMemberRequest
        {
            Email = email,
            DisplayName = name,
            Role = StaffRoles.Courier,
            CourierCode = code,
            Password = CourierPassword,
        });
        return new Rider(courier.Id, code, courier.DisplayLabel, hub, await Agent.SignInAsync(server, journal, email, CourierPassword));
    }

    private static async Task<List<Parcel>> BookAsync(Agent seller, Guid shop, string day)
    {
        var parcels = await Task.WhenAll(Enumerable.Range(0, OrdersPerSeller).Select(async n =>
        {
            var destination = n % 3 == 0 ? "IQ-BA" : "IQ-BG";
            var cod = 20_000m + (n * 2_500m);
            var quote = await seller.GetAsync<PriceQuote>($"{ShipmentsApiRoutes.SellerQuote}?destination={destination}");
            var phone = $"0781 {Random.Shared.Next(2_000_000, 9_999_999)}";
            var order = await seller.PostAsync<CreateShipmentResponse>(ShipmentsApiRoutes.SellerOrders, new CreateShipmentRequest
            {
                ServiceType = ServiceType.Domestic,
                MerchantId = shop,
                ExternalReference = $"QA-{day}-{n}",
                ConsigneeName = $"زبون {day} {n}",
                ConsigneePhone = phone,
                OriginCode = "IQ-BG",
                DestinationCode = destination,
                DestinationCity = destination == "IQ-BA" ? "العشار" : "الكرادة",
                DeliveryAddress = "شارع 62، قرب الجامع",
                ItemsDescription = "فستان",
                CodAmount = cod,
                CodCurrency = "IQD",
            });
            return new Parcel(order, shop, destination, phone, cod, quote.Fee);
        }));
        return [.. parcels];
    }

    private static async Task<ScanOutcome> ScanAsync(Agent hub, ScanOperation operation, string tracking, string hubCode, string? courier = null, DeliveryFailureReason? reason = null) =>
        await hub.PostAsync<ScanOutcome>(ShipmentsApiRoutes.WarehouseScan, new ScanRequest
        {
            Operation = operation,
            TrackingNumber = tracking,
            HubCode = hubCode,
            CourierName = courier,
            FailureReason = reason,
        });

    /// <summary>Loads everything on the truck planned for it, sends it off and unloads it at Basra.</summary>
    private static async Task TruckToBasraAsync(Agent hub, IReadOnlyList<Parcel> parcels)
    {
        var loaded = await Task.WhenAll(parcels.Select(p => ScanAsync(hub, ScanOperation.Load, p.Tracking, IraqHubs.Baghdad)));
        Assert.All(loaded, l => Assert.True(l.Ok, l.Message));
        var numbers = loaded.Select(l => l.TransferNumber).Distinct().ToList();
        Assert.Single(numbers); // one lane, one truck: scanning at the same time must not open two

        var list = await hub.GetAsync<TransferListResult>($"{ShipmentsApiRoutes.Transfers}?status=Loading&hub={IraqHubs.Baghdad}");
        var truck = list.Items.Single(t => t.Number == numbers[0]);
        await hub.PostWithETagAsync(ShipmentsApiRoutes.TransferById(truck.Id), ShipmentsApiRoutes.TransferDepart(truck.Id),
            new DepartTransferRequest { VehiclePlate = "BG-55102", DriverName = "سلام", SealNumber = "QA-1" });

        var unloaded = await Task.WhenAll(parcels.Select(p => ScanAsync(hub, ScanOperation.Unload, p.Tracking, IraqHubs.Basra)));
        Assert.All(unloaded, u => Assert.True(u.Ok, u.Message));
    }

    private enum Attempt
    {
        Delivered,
        NoAnswer,
        Refused,
    }

    private sealed record Outcome(Rider Rider, Parcel Parcel, Attempt Result);

    private static async Task<Outcome> AttemptAsync(Rider rider, Parcel parcel, int index)
    {
        if (index % 9 == 4)
        {
            await rider.App.OkAsync(ShipmentsApiRoutes.CourierFailed(parcel.Order.Id), new CourierFailureRequest { Reason = DeliveryFailureReason.CustomerRefused });
            return new Outcome(rider, parcel, Attempt.Refused);
        }

        if (index % 6 == 1)
        {
            await rider.App.OkAsync(ShipmentsApiRoutes.CourierFailed(parcel.Order.Id), new CourierFailureRequest { Reason = DeliveryFailureReason.NoAnswer });
            return new Outcome(rider, parcel, Attempt.NoAnswer);
        }

        await rider.App.OkAsync(ShipmentsApiRoutes.CourierDelivered(parcel.Order.Id), Proof());
        return new Outcome(rider, parcel, Attempt.Delivered);
    }

    private static CourierDeliveryRequest Proof() => new()
    {
        ReceivedBy = DeliveryReceiver.Customer,
        Signature = "M10 10 L60 40 L120 20",
        Location = new GeoPoint { Latitude = 33.3, Longitude = 44.4 },
    };
}
