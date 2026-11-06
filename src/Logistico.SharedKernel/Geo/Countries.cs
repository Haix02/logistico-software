using System.Globalization;

namespace Logistico.SharedKernel.Geo;

/// <summary>A country the company operates in. Iraq keeps the built-in governorates; any other country uses areas the company defines.</summary>
public sealed record Country(string Code, string NameEn, string NameAr)
{
    public string Name => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" && NameAr.Length > 0 ? NameAr : NameEn;
}

/// <summary>ISO 3166-1 alpha-2 countries a company can choose. Two letters are accepted even when the name is not listed.</summary>
public static class Countries
{
    public const string Iraq = "IQ";

    private static readonly Country[] Named =
    [
        new("IQ", "Iraq", "العراق"),
        new("JO", "Jordan", "الأردن"),
        new("LB", "Lebanon", "لبنان"),
        new("SY", "Syria", "سوريا"),
        new("PS", "Palestine", "فلسطين"),
        new("SA", "Saudi Arabia", "السعودية"),
        new("AE", "United Arab Emirates", "الإمارات"),
        new("KW", "Kuwait", "الكويت"),
        new("QA", "Qatar", "قطر"),
        new("BH", "Bahrain", "البحرين"),
        new("OM", "Oman", "عُمان"),
        new("YE", "Yemen", "اليمن"),
        new("EG", "Egypt", "مصر"),
        new("SD", "Sudan", "السودان"),
        new("LY", "Libya", "ليبيا"),
        new("TN", "Tunisia", "تونس"),
        new("DZ", "Algeria", "الجزائر"),
        new("MA", "Morocco", "المغرب"),
        new("TR", "Turkey", "تركيا"),
        new("IR", "Iran", "إيران"),
        new("DE", "Germany", "ألمانيا"),
        new("FR", "France", "فرنسا"),
        new("GB", "United Kingdom", "المملكة المتحدة"),
        new("US", "United States", "الولايات المتحدة"),
        new("CA", "Canada", "كندا"),
        new("NL", "Netherlands", "هولندا"),
        new("SE", "Sweden", "السويد"),
        new("IT", "Italy", "إيطاليا"),
        new("ES", "Spain", "إسبانيا"),
        new("IN", "India", "الهند"),
        new("PK", "Pakistan", "باكستان"),
        new("CN", "China", "الصين"),
        new("JP", "Japan", "اليابان"),
        new("KR", "South Korea", "كوريا الجنوبية"),
        new("AU", "Australia", "أستراليا"),
        new("BR", "Brazil", "البرازيل"),
        new("MX", "Mexico", "المكسيك"),
        new("NG", "Nigeria", "نيجيريا"),
        new("ZA", "South Africa", "جنوب أفريقيا"),
        new("KE", "Kenya", "كينيا"),
        new("ET", "Ethiopia", "إثيوبيا"),
        new("ID", "Indonesia", "إندونيسيا"),
        new("MY", "Malaysia", "ماليزيا"),
        new("SG", "Singapore", "سنغافورة"),
        new("TH", "Thailand", "تايلاند"),
        new("VN", "Vietnam", "فيتنام"),
        new("PH", "Philippines", "الفلبين"),
        new("RU", "Russia", "روسيا"),
        new("UA", "Ukraine", "أوكرانيا"),
        new("PL", "Poland", "بولندا"),
        new("GR", "Greece", "اليونان"),
        new("PT", "Portugal", "البرتغال"),
        new("BE", "Belgium", "بلجيكا"),
        new("CH", "Switzerland", "سويسرا"),
        new("AT", "Austria", "النمسا"),
        new("NO", "Norway", "النرويج"),
        new("DK", "Denmark", "الدنمارك"),
        new("FI", "Finland", "فنلندا"),
        new("IE", "Ireland", "أيرلندا"),
        new("NZ", "New Zealand", "نيوزيلندا"),
        new("AR", "Argentina", "الأرجنتين"),
        new("CL", "Chile", "تشيلي"),
        new("CO", "Colombia", "كولومبيا"),
    ];

    private static readonly Dictionary<string, Country> ByCode = Named.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<Country> All => Named;

    public static bool IsIraq(string? code) => string.Equals(Normalize(code), Iraq, StringComparison.Ordinal);

    /// <summary>Any ISO-style two-letter code, so a country that is not in the short list can still be typed.</summary>
    public static bool IsKnown(string? code)
    {
        var normalized = Normalize(code);
        return normalized.Length == 2 && normalized.All(char.IsAsciiLetter);
    }

    public static Country? Find(string? code) =>
        Normalize(code) is { Length: 2 } key && ByCode.TryGetValue(key, out var country) ? country : null;

    public static string NameOf(string? code)
    {
        var normalized = Normalize(code);
        return Find(normalized)?.Name ?? normalized;
    }

    public static string Normalize(string? code) => (code ?? "").Trim().ToUpperInvariant();
}
