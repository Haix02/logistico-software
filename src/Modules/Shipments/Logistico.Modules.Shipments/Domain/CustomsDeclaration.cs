using System.Globalization;
using System.Xml.Linq;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Domain;

/// <summary>
/// UNCTAD ASYCUDA World Single Administrative Document (SAD) and mandatory Iraqi compliance verification
/// record for an international shipment.
/// </summary>
internal sealed class CustomsDeclaration
{
    private CustomsDeclaration()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ShipmentId { get; private set; }
    public string CustomsOfficeCode { get; private set; } = "IQUQR";
    public string Regime { get; private set; } = "IM4";
    public string? DeclarationNumber { get; private set; }
    public string? AssessmentNumber { get; private set; }
    public string? ReceiptNumber { get; private set; }
    public string? ReleaseOrderNumber { get; private set; }
    public CustomsLane SelectivityLane { get; private set; }
    public string? HsCode { get; private set; }
    public string? GoodsDescription { get; private set; }
    public string CountryOfOrigin { get; private set; } = "CN";
    public string? CooNumber { get; private set; }
    public bool CooEVerified { get; private set; }
    public string? CommercialInvoiceNumber { get; private set; }
    public bool InvoiceEVerified { get; private set; }
    public string? CocNumber { get; private set; }
    public decimal CustomsValueUsd { get; private set; }
    public decimal DutyRatePercent { get; private set; } = 5m;
    public decimal AssessedDutyIqd { get; private set; }
    public decimal TaxAndFeesIqd { get; private set; }
    public string? HoldReason { get; private set; }
    public string? BrokerName { get; private set; }
    public DateTimeOffset? RegisteredAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }
    public uint Version { get; private set; }

    public static CustomsDeclaration CreateFor(Shipment shipment, DateTimeOffset now)
    {
        if (shipment.ServiceType != ServiceType.International)
        {
            throw new DomainException("customs.domestic_not_supported", "Customs declarations apply only to international shipments.");
        }

        var originCountry = shipment.OriginCode.Length >= 2 ? shipment.OriginCode[..2].ToUpperInvariant() : "CN";
        var customsValueUsd = shipment.DeclaredValue ?? 1_000m;
        var dutyIqd = CustomsDutyCalculator.CalculateDutyIqd(customsValueUsd, 5m);
        var feesIqd = CustomsDutyCalculator.CalculateStampAndPortFeesIqd(customsValueUsd);

        return new CustomsDeclaration
        {
            Id = Guid.CreateVersion7(now),
            TenantId = shipment.TenantId,
            ShipmentId = shipment.Id,
            CustomsOfficeCode = CustomsDutyCalculator.Offices.Any(o => o.Code == shipment.DestinationCode) ? shipment.DestinationCode : "IQUQR",
            Regime = "IM4",
            SelectivityLane = CustomsLane.Unassigned,
            CountryOfOrigin = originCountry,
            GoodsDescription = shipment.ItemsDescription,
            CustomsValueUsd = customsValueUsd,
            DutyRatePercent = 5m,
            AssessedDutyIqd = dutyIqd,
            TaxAndFeesIqd = feesIqd,
            UpdatedAt = now,
        };
    }

    public void Apply(UpsertCustomsDeclarationRequest request, Shipment shipment, string? user, DateTimeOffset now)
    {
        if (shipment.ServiceType != ServiceType.International)
        {
            throw new DomainException("customs.domestic_not_supported", "Customs declarations apply only to international shipments.");
        }

        CustomsOfficeCode = request.CustomsOfficeCode.Trim().ToUpperInvariant();
        Regime = request.Regime.Trim().ToUpperInvariant();
        DeclarationNumber = Clean(request.DeclarationNumber)?.ToUpperInvariant();
        AssessmentNumber = Clean(request.AssessmentNumber)?.ToUpperInvariant();
        ReceiptNumber = Clean(request.ReceiptNumber)?.ToUpperInvariant();
        ReleaseOrderNumber = Clean(request.ReleaseOrderNumber)?.ToUpperInvariant();
        SelectivityLane = request.SelectivityLane;
        HsCode = Clean(request.HsCode);
        GoodsDescription = Clean(request.GoodsDescription);
        CountryOfOrigin = request.CountryOfOrigin.Trim().ToUpperInvariant();
        CooNumber = Clean(request.CooNumber)?.ToUpperInvariant();
        CooEVerified = request.CooEVerified;
        CommercialInvoiceNumber = Clean(request.CommercialInvoiceNumber)?.ToUpperInvariant();
        InvoiceEVerified = request.InvoiceEVerified;
        CocNumber = Clean(request.CocNumber)?.ToUpperInvariant();
        CustomsValueUsd = request.CustomsValueUsd;
        DutyRatePercent = request.DutyRatePercent;
        AssessedDutyIqd = request.AssessedDutyIqd ?? CustomsDutyCalculator.CalculateDutyIqd(request.CustomsValueUsd, request.DutyRatePercent);
        TaxAndFeesIqd = request.TaxAndFeesIqd ?? CustomsDutyCalculator.CalculateStampAndPortFeesIqd(request.CustomsValueUsd);
        HoldReason = Clean(request.HoldReason);
        BrokerName = Clean(request.BrokerName);
        UpdatedAt = now;
        UpdatedBy = user;

        if (DeclarationNumber is not null && RegisteredAt is null)
        {
            RegisteredAt = now;
        }

        var officeName = CustomsDutyCalculator.OfficeNameOrCode(CustomsOfficeCode);
        switch (request.Action)
        {
            case CustomsAction.Register:
                RegisteredAt ??= now;
                if (shipment.Status is ShipmentStatus.ArrivedPortOfEntry or ShipmentStatus.CustomsHold)
                {
                    shipment.AdvanceTo(
                        ShipmentStatus.CustomsPending,
                        now,
                        new MilestoneDetails(Location: officeName, Notes: DeclarationNumber is null ? null : $"ASYCUDA {DeclarationNumber}"),
                        "customs",
                        user,
                        now);
                }

                break;

            case CustomsAction.Hold:
                if (string.IsNullOrWhiteSpace(HoldReason))
                {
                    throw new DomainException("customs.hold_reason_required", "A hold reason is required when placing a shipment on customs hold.");
                }

                if (shipment.Status == ShipmentStatus.ArrivedPortOfEntry)
                {
                    shipment.AdvanceTo(ShipmentStatus.CustomsPending, now, new MilestoneDetails(Location: officeName), "customs", user, now);
                }

                if (shipment.Status == ShipmentStatus.CustomsPending)
                {
                    shipment.AdvanceTo(ShipmentStatus.CustomsHold, now, new MilestoneDetails(Location: officeName, Notes: HoldReason), "customs", user, now);
                }

                break;

            case CustomsAction.Release:
                if (!CustomsDutyCalculator.CanRelease(DeclarationNumber, CooEVerified, InvoiceEVerified))
                {
                    throw new DomainException(
                        "customs.everification_required",
                        "Cannot release from customs until the ASYCUDA declaration number is entered and both Certificate of Origin (CoO) and Commercial Invoice are electronically verified.");
                }

                ReleasedAt = now;
                ReleaseOrderNumber ??= $"REL-{now.Year}-{Id.ToString("N")[^10..].ToUpperInvariant()}"; // until ASYCUDA's own number is entered
                HoldReason = null;

                if (shipment.Status == ShipmentStatus.ArrivedPortOfEntry)
                {
                    shipment.AdvanceTo(ShipmentStatus.CustomsPending, now, new MilestoneDetails(Location: officeName), "customs", user, now);
                }
                else if (shipment.Status == ShipmentStatus.CustomsHold)
                {
                    shipment.AdvanceTo(ShipmentStatus.CustomsPending, now, new MilestoneDetails(Location: officeName, Notes: "Hold resolved"), "customs", user, now);
                }

                if (shipment.Status == ShipmentStatus.CustomsPending)
                {
                    shipment.AdvanceTo(
                        ShipmentStatus.Released,
                        now,
                        new MilestoneDetails(Location: officeName, Notes: $"Release order {ReleaseOrderNumber} ({DeclarationNumber})"),
                        "customs",
                        user,
                        now);
                }

                break;
        }
    }

    /// <summary>Generates the UNCTAD ASYCUDA World Single Administrative Document (SAD) XML payload.</summary>
    public string ToAsycudaSadXml(Shipment shipment)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("ASYCUDA",
                new XElement("Export_release",
                    new XElement("Date_of_exit", ReleasedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "")),
                new XElement("Identification",
                    new XElement("Office_segment",
                        new XElement("Customs_clearance_office_code", CustomsOfficeCode),
                        new XElement("Customs_Clearance_office_name", CustomsDutyCalculator.OfficeNameOrCode(CustomsOfficeCode))),
                    new XElement("Type",
                        new XElement("Type_of_declaration", Regime.Length >= 2 ? Regime[..2] : "IM"),
                        new XElement("Declaration_gen_procedure_code", Regime.Length > 2 ? Regime[2..] : "4")),
                    new XElement("Registration",
                        new XElement("Number", DeclarationNumber ?? ""),
                        new XElement("Date", (RegisteredAt ?? UpdatedAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
                    new XElement("Assessment",
                        new XElement("Number", AssessmentNumber ?? ""),
                        new XElement("Receipt_number", ReceiptNumber ?? ""),
                        new XElement("Release_order_number", ReleaseOrderNumber ?? "")),
                    new XElement("Selectivity_lane", SelectivityLane.ToString())),
                new XElement("Traders",
                    new XElement("Exporter",
                        new XElement("Exporter_name", shipment.ShipperName)),
                    new XElement("Consignee",
                        new XElement("Consignee_name", shipment.ConsigneeName),
                        new XElement("Consignee_phone", shipment.ConsigneePhone ?? "")),
                    new XElement("Declarant",
                        new XElement("Declarant_name", BrokerName ?? "Logistico Customs Brokerage Iraq"))),
                new XElement("General_information",
                    new XElement("Country",
                        new XElement("Country_first_destination", shipment.OriginCode),
                        new XElement("Country_of_origin_name", CountryOfOrigin),
                        new XElement("Destination_country_code", "IQ")),
                    new XElement("Transport",
                        new XElement("Mode_of_transport", shipment.Mode.ToString()),
                        new XElement("Waybill_reference", shipment.HouseBillNumber ?? shipment.TrackingNumber),
                        new XElement("Tracking_number", shipment.TrackingNumber))),
                new XElement("Valuation",
                    new XElement("Calculation_working_mode", "1"),
                    new XElement("Total_invoice",
                        new XElement("Amount_foreign_currency", CustomsValueUsd.ToString("0.00", CultureInfo.InvariantCulture)),
                        new XElement("Currency_code", "USD"),
                        new XElement("Currency_rate", CustomsDutyCalculator.OfficialUsdToIqdRate.ToString("0.00", CultureInfo.InvariantCulture))),
                    new XElement("Total_assessed_duty_iqd", AssessedDutyIqd.ToString("0", CultureInfo.InvariantCulture)),
                    new XElement("Total_tax_and_fees_iqd", TaxAndFeesIqd.ToString("0", CultureInfo.InvariantCulture))),
                new XElement("Item",
                    new XElement("Packages",
                        new XElement("Number_of_packages", shipment.Pieces),
                        new XElement("Gross_weight_kg", shipment.GrossWeightKg.ToString("0.000", CultureInfo.InvariantCulture))),
                    new XElement("Tarification",
                        new XElement("HScode",
                            new XElement("Commodity_code", HsCode ?? "0000.00.00")),
                        new XElement("Duty_rate_percent", DutyRatePercent.ToString("0.00", CultureInfo.InvariantCulture))),
                    new XElement("Goods_description",
                        new XElement("Country_of_origin_code", CountryOfOrigin),
                        new XElement("Description_of_goods", GoodsDescription ?? shipment.ItemsDescription ?? "General cargo")),
                    new XElement("Attached_documents",
                        new XElement("Document",
                            new XElement("Document_type", "COO"),
                            new XElement("Document_reference", CooNumber ?? ""),
                            new XElement("E_verified", CooEVerified ? "true" : "false")),
                        new XElement("Document",
                            new XElement("Document_type", "COMMERCIAL_INVOICE"),
                            new XElement("Document_reference", CommercialInvoiceNumber ?? ""),
                            new XElement("E_verified", InvoiceEVerified ? "true" : "false")),
                        new XElement("Document",
                            new XElement("Document_type", "COSQC_COC"),
                            new XElement("Document_reference", CocNumber ?? ""))))));

        return doc.Declaration + Environment.NewLine + doc.ToString();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

