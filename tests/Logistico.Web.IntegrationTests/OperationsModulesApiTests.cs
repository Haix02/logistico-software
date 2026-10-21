using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Logistico.Modules.Shipments.Contracts;

namespace Logistico.Web.IntegrationTests;

[Collection("Api")]
public sealed class OperationsModulesApiTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task International_shipment_clears_customs_after_mandatory_everification_and_exports_asycuda_sad_xml()
    {
        var client = factory.CreateUserClient();
        var booked = await client.BookAsync(ApiTestHelpers.InternationalBooking("Istanbul Machinery Export A.S."));

        // Advance shipment to ArrivedPortOfEntry
        var r1 = await client.RecordAsync(booked.Id, new RecordShipmentEventRequest { Status = ShipmentStatus.ReceivedAtOrigin }, await client.ETagAsync(booked.Id));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var r2 = await client.RecordAsync(booked.Id, new RecordShipmentEventRequest { Status = ShipmentStatus.DepartedOrigin }, await client.ETagAsync(booked.Id));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        var r3 = await client.RecordAsync(booked.Id, new RecordShipmentEventRequest { Status = ShipmentStatus.ArrivedPortOfEntry }, await client.ETagAsync(booked.Id));
        Assert.Equal(HttpStatusCode.OK, r3.StatusCode);

        // 1. Register ASYCUDA declaration -> advances shipment to CustomsPending
        var registerReq = new UpsertCustomsDeclarationRequest
        {
            CustomsOfficeCode = "IQUQR",
            Regime = "IM4",
            DeclarationNumber = "2026-IQUQR-IM4-77001",
            SelectivityLane = CustomsLane.Yellow,
            HsCode = "8422.30.00",
            GoodsDescription = "Industrial packaging machinery",
            CountryOfOrigin = "TR",
            CooNumber = "COO-TR-77001",
            CooEVerified = false,
            CommercialInvoiceNumber = "INV-TR-77001",
            InvoiceEVerified = true,
            CustomsValueUsd = 12_000m,
            DutyRatePercent = 5m,
            Action = CustomsAction.Register,
        };
        var regResp = await client.PutAsJsonAsync("/" + ShipmentsApiRoutes.CustomsByShipment(booked.Id), registerReq);
        Assert.Equal(HttpStatusCode.OK, regResp.StatusCode);
        var regItem = (await regResp.Content.ReadFromJsonAsync<CustomsQueueItem>())!;
        Assert.Equal(ShipmentStatus.CustomsPending, regItem.ShipmentStatus);
        Assert.Equal(786_000m, regItem.AssessedDutyIqd); // 12,000 * 1,310 * 5%

        // Changing a saved declaration needs its version, so a stale form cannot overwrite newer work.
        var etag = new EntityTagHeaderValue($"\"{regItem.Version}\"");
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await client.PutAsJsonAsync("/" + ShipmentsApiRoutes.CustomsByShipment(booked.Id), registerReq)).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.PutWithETagAsync(ShipmentsApiRoutes.CustomsByShipment(booked.Id), registerReq, new EntityTagHeaderValue("\"1\""))).StatusCode);

        // 2. Attempting release without CoO e-verification is rejected with 422
        registerReq.Action = CustomsAction.Release;
        var prematureRelease = await client.PutWithETagAsync(ShipmentsApiRoutes.CustomsByShipment(booked.Id), registerReq, etag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, prematureRelease.StatusCode);
        using (var problem = JsonDocument.Parse(await prematureRelease.Content.ReadAsStringAsync()))
        {
            Assert.Equal("customs.everification_required", problem.RootElement.GetProperty("code").GetString());
        }

        // 3. Complete mandatory e-verification and release
        registerReq.CooEVerified = true;
        registerReq.InvoiceEVerified = true;
        registerReq.SelectivityLane = CustomsLane.Green;
        registerReq.Action = CustomsAction.Release;
        var releaseResp = await client.PutWithETagAsync(ShipmentsApiRoutes.CustomsByShipment(booked.Id), registerReq, etag);
        Assert.Equal(HttpStatusCode.OK, releaseResp.StatusCode);
        var releasedItem = (await releaseResp.Content.ReadFromJsonAsync<CustomsQueueItem>())!;
        Assert.Equal(ShipmentStatus.Released, releasedItem.ShipmentStatus);
        Assert.NotNull(releasedItem.ReleaseOrderNumber);

        // 4. Download ASYCUDA SAD XML
        var sadResp = await client.GetAsync("/" + ShipmentsApiRoutes.CustomsSadXml(booked.Id));
        Assert.Equal(HttpStatusCode.OK, sadResp.StatusCode);
        var sadXml = await sadResp.Content.ReadAsStringAsync();
        Assert.Contains("<ASYCUDA>", sadXml, StringComparison.Ordinal);
        Assert.Contains("2026-IQUQR-IM4-77001", sadXml, StringComparison.Ordinal);
        Assert.Contains("8422.30.00", sadXml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fleet_couriers_support_creation_unique_codes_and_etag_concurrency()
    {
        var client = factory.CreateUserClient();
        var req = new CourierRequest
        {
            Code = "BG-TEST-01",
            Name = "وسام التميمي",
            Phone = "0770 888 9900",
            Governorate = "IQ-BG",
            VehicleType = VehicleType.Van,
            VehiclePlate = "BG-55120",
        };

        var createdResp = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.FleetCouriers, req);
        Assert.Equal(HttpStatusCode.Created, createdResp.StatusCode);
        var courier = (await createdResp.Content.ReadFromJsonAsync<CourierListItem>())!;
        Assert.Equal("+9647708889900", courier.Phone);
        Assert.Equal("BG-55120", courier.VehiclePlate);

        // Duplicate code is rejected with 409 Conflict
        var dupResp = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.FleetCouriers, req);
        Assert.Equal(HttpStatusCode.Conflict, dupResp.StatusCode);

        // Update with current ETag succeeds
        req.VehiclePlate = "BG-99999";
        using var updateMsg = new HttpRequestMessage(HttpMethod.Put, "/" + ShipmentsApiRoutes.CourierById(courier.Id))
        {
            Content = JsonContent.Create(req),
        };
        updateMsg.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{courier.Version}\""));
        var updatedResp = await client.SendAsync(updateMsg);
        Assert.Equal(HttpStatusCode.OK, updatedResp.StatusCode);

        // Stale ETag is rejected with 412 PreconditionFailed
        using var staleMsg = new HttpRequestMessage(HttpMethod.Put, "/" + ShipmentsApiRoutes.CourierById(courier.Id))
        {
            Content = JsonContent.Create(req),
        };
        staleMsg.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{courier.Version}\""));
        var staleResp = await client.SendAsync(staleMsg);
        Assert.Equal(HttpStatusCode.PreconditionFailed, staleResp.StatusCode);
    }

    [Fact]
    public async Task Finance_reconciles_delivered_cod_orders_and_settles_net_payout_to_seller()
    {
        var client = factory.CreateUserClient();
        var shop = await client.CreateMerchantAsync("متجر النهرين", "nahrain.store.test");

        // Book, receive, dispatch, and deliver 2 COD orders inside Baghdad (65,000 IQD COD, 8,000 IQD fee each -> 57,000 IQD net each)
        for (var i = 1; i <= 2; i++)
        {
            var request = ApiTestHelpers.DomesticOrder(shop, $"زبون {i}");
            request.DestinationCode = "IQ-BG";
            var booked = await client.BookAsync(request);
            await client.ScanAsync(ScanOperation.Receive, booked.TrackingNumber, "HUB-BGW", "A-01-1");
            await client.ScanAsync(ScanOperation.Dispatch, booked.TrackingNumber, "HUB-BGW", courier: "حيدر (Baghdad-02)");
            var del = await client.RecordAsync(booked.Id, new RecordShipmentEventRequest { Status = ShipmentStatus.Delivered }, await client.ETagAsync(booked.Id));
            Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        }

        // Check finance summary for this seller
        var summaryBefore = (await client.GetFromJsonAsync<FinanceSummary>("/" + ShipmentsApiRoutes.FinanceSummary + $"?merchantId={shop}"))!;
        var balance = Assert.Single(summaryBefore.SellerBalances);
        Assert.Equal(2, balance.DeliveredUnsettledCount);
        Assert.Equal(130_000m, balance.GrossCodIqd);
        Assert.Equal(16_000m, balance.DeliveryFeesIqd);
        Assert.Equal(114_000m, balance.NetPayableIqd);

        // Create COD settlement payout
        var settleResp = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.FinanceSettlements, new CreateCodSettlementRequest
        {
            MerchantId = shop,
            PayoutMethod = PayoutMethod.ZainCash,
            PayoutReference = "ZC-2026-8801",
            Notes = "Payout via ZainCash wallet",
        });
        Assert.Equal(HttpStatusCode.Created, settleResp.StatusCode);
        var settlement = (await settleResp.Content.ReadFromJsonAsync<CodSettlementItem>())!;
        Assert.Equal(2, settlement.ShipmentCount);
        Assert.Equal(130_000m, settlement.GrossCodAmount);
        Assert.Equal(16_000m, settlement.DeliveryFeesAmount);
        Assert.Equal(114_000m, settlement.NetPayoutAmount);

        // Second settlement attempt when no unsettled orders remain returns 422
        var secondResp = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.FinanceSettlements, new CreateCodSettlementRequest
        {
            MerchantId = shop,
            PayoutMethod = PayoutMethod.Cash,
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, secondResp.StatusCode);

        // Export settlement statement CSV
        var csvResp = await client.GetAsync("/" + ShipmentsApiRoutes.SettlementExport(settlement.Id));
        Assert.Equal(HttpStatusCode.OK, csvResp.StatusCode);
        var csv = await csvResp.Content.ReadAsStringAsync();
        Assert.Contains(settlement.SettlementNumber, csv, StringComparison.Ordinal);
        Assert.Contains("ZC-2026-8801", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hub_scans_are_logged_and_a_parcel_at_the_wrong_hub_raises_an_alert_until_it_reaches_its_hub()
    {
        var client = factory.CreateUserClient();
        var shop = await client.CreateMerchantAsync("متجر البصرة", "basra.store.test");
        var p1 = await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "مرتضى البصري"));
        var p2 = await client.BookAsync(ApiTestHelpers.DomesticOrder(shop, "حسن البغدادي"));

        // 1. Both received at the Baghdad hub in one bulk scan; both go on the same truck to Basra.
        var bulk = await client.PostAsJsonAsync("/" + ShipmentsApiRoutes.WarehouseBulk, new BulkScanRequest
        {
            TrackingNumbers = [p1.TrackingNumber, p2.TrackingNumber, "LX26999999990"],
            Operation = ScanOperation.Receive,
            HubCode = "HUB-BGW",
            WarehouseBin = "A-01-1",
        });
        var result = (await bulk.Content.ReadFromJsonAsync<BulkScanResult>())!;
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Single(result.Items.Where(i => i.Ok).Select(i => i.TransferNumber).Distinct());

        // 2. A check at Erbil (not on the parcel's route) is flagged; nothing changes.
        var check = await client.ScanAsync(ScanOperation.Verify, p1.TrackingNumber, "HUB-EBL");
        Assert.True(check.IsMisrouted);
        Assert.Equal("HUB-BSR", check.ExpectedHubCode);

        // 3. p2 turns up at Erbil by mistake: received there, flagged, taken off the Baghdad truck and planned Erbil → Basra.
        var wrong = await client.ScanAsync(ScanOperation.Receive, p2.TrackingNumber, "HUB-EBL", "X-01");
        Assert.True(wrong.IsMisrouted);
        Assert.Equal("HUB-BSR", wrong.NextHubCode);
        var alerts = (await client.GetFromJsonAsync<AlertList>("/" + ShipmentsApiRoutes.AutomationAlerts + "?kind=Misrouted"))!;
        Assert.Contains(alerts.Items, a => a.TrackingNumber == p2.TrackingNumber);

        // 4. The radar (persistent scan log) shows scans from every hub, including failures and misroutes.
        var radar = (await client.GetFromJsonAsync<HubRadar>("/" + ShipmentsApiRoutes.WarehouseRadar))!;
        Assert.True(radar.ScansToday >= 5);
        Assert.True(radar.MisroutedToday >= 2);
        Assert.Contains(radar.Recent, e => e.TrackingNumber == "LX26999999990" && !e.Ok);
        Assert.Contains(radar.Hubs, h => h.HubCode == "HUB-EBL" && h.MisroutedToday >= 2);

        // 5. Once it reaches Basra the automation clears the alert.
        await client.TruckAsync(p2.TrackingNumber, "HUB-EBL", "HUB-BSR");
        await client.RunAutomationAsync();
        var open = (await client.GetFromJsonAsync<AlertList>("/" + ShipmentsApiRoutes.AutomationAlerts + "?kind=Misrouted"))!;
        Assert.DoesNotContain(open.Items, a => a.TrackingNumber == p2.TrackingNumber);
        Assert.Contains(open.Items, a => a.TrackingNumber == p1.TrackingNumber); // still at its seller's hub, not yet where it belongs
        var closed = (await client.GetFromJsonAsync<AlertList>("/" + ShipmentsApiRoutes.AutomationAlerts + "?kind=Misrouted&state=closed"))!;
        Assert.Contains(closed.Items, a => a.TrackingNumber == p2.TrackingNumber && a.AutoResolved);
    }
}
