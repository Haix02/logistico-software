using System.Globalization;

namespace Logistico.Modules.Shipments.Contracts.Rules;

/// <summary>
/// Iraqi customs valuation, duty calculation (at official CBI exchange rate) and ASYCUDA compliance rules
/// (docs/05-customs-clearance-and-iraq-compliance.md).
/// </summary>
public static class CustomsDutyCalculator
{
    /// <summary>Central Bank of Iraq (CBI) official USD/IQD customs valuation rate.</summary>
    public const decimal OfficialUsdToIqdRate = 1_310m;

    public sealed record CustomsOfficeInfo(string Code, string NameEn, string NameAr)
    {
        public string Name => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? NameAr : NameEn;
    }

    public static readonly IReadOnlyList<CustomsOfficeInfo> Offices =
    [
        new("IQUQR", "Umm Qasr North / South Port", "ميناء أم قصر الشمالي / الجنوبي"),
        new("IQBGW", "Baghdad International Airport", "مطار بغداد الدولي"),
        new("IQBSR", "Basra / Safwan Border", "منفذ البصرة / سفوان"),
        new("IQEBL", "Erbil / Ibrahim Khalil", "أربيل / إبراهيم الخليل"),
        new("IQISU", "Sulaymaniyah / Bashmakh", "السليمانية / باشماخ"),
        new("IQNJF", "Najaf International Airport", "مطار النجف الدولي"),
    ];

    public static readonly string[] Regimes = ["IM4", "IM5", "IM7", "TR8"];

    public static string OfficeNameOrCode(string? code) =>
        Offices.FirstOrDefault(o => string.Equals(o.Code, code, StringComparison.OrdinalIgnoreCase))?.Name ?? code ?? "";

    public static decimal CalculateDutyIqd(decimal customsValueUsd, decimal dutyRatePercent)
    {
        if (customsValueUsd <= 0 || dutyRatePercent <= 0)
        {
            return 0m;
        }

        return Math.Round(customsValueUsd * OfficialUsdToIqdRate * (dutyRatePercent / 100m), 0, MidpointRounding.AwayFromZero);
    }

    public static decimal CalculateStampAndPortFeesIqd(decimal customsValueUsd)
    {
        if (customsValueUsd <= 0)
        {
            return 0m;
        }

        return Math.Round(Math.Max(25_000m, customsValueUsd * OfficialUsdToIqdRate * 0.005m), 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Under Iraq General Commission for Customs rules, a declaration can only be released when an ASYCUDA
    /// declaration number is assigned and both Certificate of Origin (CoO) and Commercial Invoice are e-verified.
    /// </summary>
    public static bool CanRelease(string? declarationNumber, bool cooEVerified, bool invoiceEVerified) =>
        !string.IsNullOrWhiteSpace(declarationNumber) && cooEVerified && invoiceEVerified;
}

