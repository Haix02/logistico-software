using System.Globalization;

namespace Logistico.SharedKernel.Iraq;

/// <summary>An Iraqi governorate; <see cref="Latitude"/>/<see cref="Longitude"/> are its capital city.</summary>
public sealed record Governorate(string Code, string NameEn, string NameAr, double Latitude, double Longitude)
{
    public string Name => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? NameAr : NameEn;
}

/// <summary>Iraq's governorates, coded with ISO 3166-2:IQ (Halabja has no ISO code yet; IQ-HA is internal).</summary>
public static class IraqGovernorates
{
    public static readonly IReadOnlyList<Governorate> All =
    [
        new("IQ-BG", "Baghdad", "بغداد", 33.315, 44.366),
        new("IQ-BA", "Basra", "البصرة", 30.508, 47.783),
        new("IQ-NI", "Nineveh", "نينوى", 36.340, 43.130),
        new("IQ-AR", "Erbil", "أربيل", 36.191, 44.009),
        new("IQ-SU", "Sulaymaniyah", "السليمانية", 35.557, 45.435),
        new("IQ-DA", "Duhok", "دهوك", 36.867, 42.988),
        new("IQ-KI", "Kirkuk", "كركوك", 35.468, 44.392),
        new("IQ-NA", "Najaf", "النجف", 32.000, 44.336),
        new("IQ-KA", "Karbala", "كربلاء", 32.616, 44.025),
        new("IQ-BB", "Babil", "بابل", 32.483, 44.433),
        new("IQ-DI", "Diyala", "ديالى", 33.750, 44.643),
        new("IQ-AN", "Anbar", "الأنبار", 33.426, 43.300),
        new("IQ-SD", "Saladin", "صلاح الدين", 34.616, 43.678),
        new("IQ-WA", "Wasit", "واسط", 32.506, 45.824),
        new("IQ-QA", "Al-Qadisiyah", "القادسية", 31.990, 44.925),
        new("IQ-MU", "Muthanna", "المثنى", 31.317, 45.283),
        new("IQ-DQ", "Dhi Qar", "ذي قار", 31.044, 46.258),
        new("IQ-MA", "Maysan", "ميسان", 31.836, 47.145),
        new("IQ-HA", "Halabja", "حلبجة", 35.178, 45.986),
    ];

    private static readonly Dictionary<string, Governorate> ByCode = All.ToDictionary(g => g.Code, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string? code) => code is not null && ByCode.ContainsKey(code);

    public static Governorate? Find(string? code) => code is not null && ByCode.TryGetValue(code, out var g) ? g : null;

    /// <summary>Governorate name in the current UI language, or the code itself for non-Iraqi locations (e.g. CNSHA).</summary>
    public static string NameOrCode(string? code) => Find(code)?.Name ?? code ?? "";
}
