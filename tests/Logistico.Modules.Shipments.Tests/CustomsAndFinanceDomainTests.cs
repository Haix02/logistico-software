using System.Xml.Linq;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Tests;

public class CustomsAndFinanceDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    private static Shipment BookedInternational() => Shipment.Book(
        Tenants.Default,
        "LX26000000105",
        new CreateShipmentRequest
        {
            ServiceType = ServiceType.International,
            Mode = TransportMode.SeaFcl,
            ShipperName = "Ningbo Bright Lighting Co.",
            ConsigneeName = "شركة النور للتجارة العامة",
            OriginCode = "CNNGB",
            DestinationCode = "IQUQR",
            Pieces = 20,
            GrossWeightKg = 12_000m,
            VolumeCbm = 28m,
            DeclaredValue = 10_000m,
            DeclaredCurrency = "USD",
        },
        null,
        "agent",
        Now, IraqHubs.Default);

    [Fact]
    public void CustomsDutyCalculator_calculates_duty_and_fees_at_official_cbi_rate()
    {
        var duty = CustomsDutyCalculator.CalculateDutyIqd(10_000m, 10m);
        var fees = CustomsDutyCalculator.CalculateStampAndPortFeesIqd(10_000m);

        Assert.Equal(1_310_000m, duty);
        Assert.Equal(65_500m, fees);
        Assert.False(CustomsDutyCalculator.CanRelease("2026-IQUQR-IM4-10001", cooEVerified: true, invoiceEVerified: false));
        Assert.True(CustomsDutyCalculator.CanRelease("2026-IQUQR-IM4-10001", cooEVerified: true, invoiceEVerified: true));
    }

    [Theory]
    [InlineData(10_000, "USD", 10_000)]
    [InlineData(13_100_000, "IQD", 10_000)]
    [InlineData(10_000, "EUR", 0)] // no official rate: the clerk enters dollars from the invoice
    [InlineData(null, "USD", 0)]
    public void The_customs_value_is_in_dollars_whatever_currency_was_declared(int? declared, string currency, int usd) =>
        Assert.Equal((decimal)usd, CustomsDutyCalculator.CustomsValueUsd((decimal?)declared, currency));

    [Fact]
    public void Customs_releases_only_valued_and_paid_declarations_and_keeps_the_first_release()
    {
        var shipment = BookedInternational();
        foreach (var (status, day) in new[] { (ShipmentStatus.ReceivedAtOrigin, 0), (ShipmentStatus.DepartedOrigin, 1), (ShipmentStatus.ArrivedPortOfEntry, 2) })
        {
            shipment.AdvanceTo(status, Now.AddDays(day), new MilestoneDetails(), "system", "agent", Now.AddDays(day));
        }

        var decl = CustomsDeclaration.CreateFor(shipment, Now.AddDays(2));
        var release = new UpsertCustomsDeclarationRequest
        {
            CustomsOfficeCode = "IQUQR",
            Regime = "IM4",
            DeclarationNumber = "2026-IQUQR-IM4-50002",
            CountryOfOrigin = "CN",
            CooEVerified = true,
            InvoiceEVerified = true,
            CustomsValueUsd = 0m,
            DutyRatePercent = 5m,
            Action = CustomsAction.Release,
        };

        Assert.Equal("customs.value_required", Assert.Throws<DomainException>(() => decl.Apply(release, shipment, "broker", Now.AddDays(3))).Code);
        release.CustomsValueUsd = 10_000m;
        Assert.Equal("customs.payment_required", Assert.Throws<DomainException>(() => decl.Apply(release, shipment, "broker", Now.AddDays(3))).Code);

        release.ReceiptNumber = "RCP-2026-50002";
        decl.Apply(release, shipment, "broker", Now.AddDays(3));
        decl.Apply(release, shipment, "broker", Now.AddDays(5)); // saved again later
        Assert.Equal((ShipmentStatus.Released, Now.AddDays(3)), (shipment.Status, decl.ReleasedAt));

        release.Action = CustomsAction.Hold;
        release.HoldReason = "Inspection";
        Assert.Equal("customs.already_released", Assert.Throws<DomainException>(() => decl.Apply(release, shipment, "broker", Now.AddDays(6))).Code);
    }

    [Fact]
    public void Customs_declaration_enforces_mandatory_iraq_everification_before_release_and_exports_sad_xml()
    {
        var shipment = BookedInternational();
        var at = Now.AddHours(2);
        shipment.AdvanceTo(ShipmentStatus.ReceivedAtOrigin, at, new MilestoneDetails(), "system", "agent", at);
        at = at.AddHours(12);
        shipment.AdvanceTo(ShipmentStatus.DepartedOrigin, at, new MilestoneDetails(), "system", "agent", at);
        at = at.AddHours(24);
        shipment.AdvanceTo(ShipmentStatus.ArrivedPortOfEntry, at, new MilestoneDetails(), "system", "agent", at);

        var decl = CustomsDeclaration.CreateFor(shipment, at);

        // Register moves shipment to CustomsPending
        at = at.AddHours(1);
        decl.Apply(new UpsertCustomsDeclarationRequest
        {
            CustomsOfficeCode = "IQUQR",
            Regime = "IM4",
            DeclarationNumber = "2026-IQUQR-IM4-50001",
            SelectivityLane = CustomsLane.Yellow,
            HsCode = "9405.42.00",
            CountryOfOrigin = "CN",
            CustomsValueUsd = 10_000m,
            DutyRatePercent = 10m,
            CooNumber = "COO-CN-9001",
            CooEVerified = false,
            CommercialInvoiceNumber = "INV-9001",
            InvoiceEVerified = true,
            Action = CustomsAction.Register,
        }, shipment, "broker", at);

        Assert.Equal(ShipmentStatus.CustomsPending, shipment.Status);

        // Attempting release without CoO e-verification fails
        at = at.AddHours(1);
        var ex = Assert.Throws<DomainException>(() => decl.Apply(new UpsertCustomsDeclarationRequest
        {
            CustomsOfficeCode = "IQUQR",
            Regime = "IM4",
            DeclarationNumber = "2026-IQUQR-IM4-50001",
            CountryOfOrigin = "CN",
            CooEVerified = false,
            InvoiceEVerified = true,
            Action = CustomsAction.Release,
        }, shipment, "broker", at));
        Assert.Equal("customs.everification_required", ex.Code);

        // Release succeeds once CoO and Invoice are both e-verified
        decl.Apply(new UpsertCustomsDeclarationRequest
        {
            CustomsOfficeCode = "IQUQR",
            Regime = "IM4",
            DeclarationNumber = "2026-IQUQR-IM4-50001",
            SelectivityLane = CustomsLane.Green,
            HsCode = "9405.42.00",
            CountryOfOrigin = "CN",
            CustomsValueUsd = 10_000m,
            DutyRatePercent = 10m,
            CooNumber = "COO-CN-9001",
            CooEVerified = true,
            CommercialInvoiceNumber = "INV-9001",
            InvoiceEVerified = true,
            CocNumber = "COC-IQ-9001",
            ReceiptNumber = "RCP-2026-9001",
            Action = CustomsAction.Release,
        }, shipment, "broker", at);

        Assert.Equal(ShipmentStatus.Released, shipment.Status);
        Assert.NotNull(decl.ReleasedAt);
        Assert.NotNull(decl.ReleaseOrderNumber);

        var xml = decl.ToAsycudaSadXml(shipment);
        var doc = XDocument.Parse(xml);
        Assert.Equal("ASYCUDA", doc.Root?.Name.LocalName);
        Assert.Contains("2026-IQUQR-IM4-50001", xml, StringComparison.Ordinal);
        Assert.Contains("9405.42.00", xml, StringComparison.Ordinal);
        Assert.Contains("COO-CN-9001", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void CodSettlement_computes_net_seller_payout_and_marks_shipments_settled()
    {
        var shop = new MerchantSnapshot(Guid.CreateVersion7(), "بوتيك دجلة", "IQ-BG");
        var s1 = Shipment.Book(Tenants.Default, "LX26000000202", new CreateShipmentRequest
        {
            ServiceType = ServiceType.Domestic,
            MerchantId = shop.Id,
            ConsigneeName = "سارة",
            ConsigneePhone = "07701234567",
            OriginCode = "IQ-BG",
            DestinationCode = "IQ-BG",
            DeliveryAddress = "المنصور",
            CodAmount = 50_000m,
            CodCurrency = "IQD",
            DeliveryFee = 5_000m,
        }, shop, "agent", Now, IraqHubs.Default);

        var at = Now.AddHours(1);
        s1.AdvanceTo(ShipmentStatus.AtWarehouse, at, new MilestoneDetails(WarehouseBin: "A-01-1"), "scan", "clerk", at);
        at = at.AddHours(1);
        s1.AdvanceTo(ShipmentStatus.OutForDelivery, at, new MilestoneDetails(CourierName: "مصطفى"), "scan", "clerk", at);
        at = at.AddHours(2);
        s1.AdvanceTo(ShipmentStatus.Delivered, at, new MilestoneDetails(), "user", "courier", at);

        var settlement = CodSettlement.Create(
            Tenants.Default,
            "STL-2026-0001",
            shop.Id,
            shop.Name,
            [s1],
            [],
            PayoutMethod.ZainCash,
            "ZC-12345",
            "Weekly payout",
            "finance@logistico.iq",
            at.AddHours(1));

        Assert.Equal(50_000m, settlement.GrossCodAmount);
        Assert.Equal(5_000m, settlement.DeliveryFeesAmount);
        Assert.Equal(45_000m, settlement.NetPayoutAmount);
        Assert.Equal(settlement.Id, s1.CodSettlementId);
        Assert.NotNull(s1.CodSettledAt);
    }

    [Fact]
    public void Code128Barcode_encodes_iso15417_modules_and_renders_valid_svg()
    {
        var modules = Code128Barcode.EncodeModules("LX2600000105");
        Assert.StartsWith("11010010000", modules, StringComparison.Ordinal); // Start Code B (104)
        Assert.EndsWith("1100011101011", modules, StringComparison.Ordinal); // Stop Code (106)

        var svg = Code128Barcode.ToSvg("LX2600000105", 56, true);
        var doc = XDocument.Parse(svg);
        Assert.Equal("svg", doc.Root?.Name.LocalName);
        Assert.Contains("LX2600000105", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void IraqHubs_resolves_destination_hubs_detects_misroutes_and_parses_action_barcodes()
    {
        Assert.Equal(IraqHubs.Basra, IraqHubs.Default.ResolveDestinationHub("IQ-BA").Code);
        Assert.Equal(IraqHubs.Erbil, IraqHubs.Default.ResolveDestinationHub("IQ-AR").Code);
        Assert.Equal(IraqHubs.Najaf, IraqHubs.Default.ResolveDestinationHub("IQ-NA").Code);
        Assert.Equal(IraqHubs.Mosul, IraqHubs.Default.ResolveDestinationHub("IQ-NI").Code);
        Assert.Equal(IraqHubs.Baghdad, IraqHubs.Default.ResolveDestinationHub("IQ-BG").Code);

        // Basra parcel scanned in Baghdad is flagged as cross-hub/misrouted
        Assert.True(IraqHubs.Default.IsCrossHubOrMisrouted(IraqHubs.Baghdad, "IQ-BA", out var expectedHub));
        Assert.Equal("BSR", expectedHub.RoutingTag);
        Assert.False(IraqHubs.Default.IsCrossHubOrMisrouted(IraqHubs.Basra, "IQ-BA", out _));

        // Action barcodes parse cleanly
        Assert.True(IraqHubs.Default.TryParseActionBarcode("CMD:MODE:DISPATCH", out var modeCmd));
        Assert.Equal(ActionBarcodeKind.SwitchMode, modeCmd.Kind);
        Assert.Equal("DISPATCH", modeCmd.Value);

        Assert.True(IraqHubs.Default.TryParseActionBarcode("CMD:HUB:HUB-BSR", out var hubCmd));
        Assert.Equal(ActionBarcodeKind.SwitchHub, hubCmd.Kind);
        Assert.Equal(IraqHubs.Basra, hubCmd.Value);

        Assert.True(IraqHubs.Default.TryParseActionBarcode("BIN:A-01-1", out var binCmd));
        Assert.Equal(ActionBarcodeKind.SetBin, binCmd.Kind);
        Assert.Equal("A-01-1", binCmd.Value);
    }
}

