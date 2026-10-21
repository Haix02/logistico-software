namespace Logistico.Modules.Shipments.Contracts.Rules;

/// <summary>
/// The standard Iraqi network every company starts with: five sorting hubs that together cover all governorates.
/// Companies add, move and retire warehouses afterwards; routing always goes through a <see cref="HubNetwork"/>.
/// </summary>
public static class IraqHubs
{
    public const string Baghdad = "HUB-BGW";
    public const string Basra = "HUB-BSR";
    public const string Erbil = "HUB-EBL";
    public const string Najaf = "HUB-NJF";
    public const string Mosul = "HUB-MOS";

    /// <summary>The five standard hubs, Baghdad first (the primary hub).</summary>
    public static IReadOnlyList<HubInfo> Standard { get; } =
    [
        new(Baghdad, "BGW", "IQ-BG", "مركز فرز بغداد المركزي", "Baghdad Central Hub")
        {
            Latitude = 33.315, Longitude = 44.366, Covers = ["IQ-BG", "IQ-AN", "IQ-DI", "IQ-SD", "IQ-WA"],
        },
        new(Basra, "BSR", "IQ-BA", "مركز فرز البصرة والجنوب", "Basra Southern Hub")
        {
            Latitude = 30.508, Longitude = 47.783, Covers = ["IQ-BA", "IQ-DQ", "IQ-MA", "IQ-MU"],
        },
        new(Erbil, "EBL", "IQ-AR", "مركز فرز أربيل والشمال", "Erbil Northern Hub")
        {
            Latitude = 36.191, Longitude = 44.009, Covers = ["IQ-AR", "IQ-SU", "IQ-DA", "IQ-KI", "IQ-HA"],
        },
        new(Najaf, "NJF", "IQ-NA", "مركز فرز النجف والفرات الأوسط", "Najaf Mid-Euphrates Hub")
        {
            Latitude = 32.000, Longitude = 44.336, Covers = ["IQ-NA", "IQ-KA", "IQ-BB", "IQ-QA"],
        },
        new(Mosul, "MOS", "IQ-NI", "مركز فرز الموصل ونينوى", "Mosul Nineveh Hub")
        {
            Latitude = 36.340, Longitude = 43.130, Covers = ["IQ-NI"],
        },
    ];

    /// <summary>The standard network (used to start a new company and in tests).</summary>
    public static HubNetwork Default { get; } = new(Standard);

    /// <summary>Hub code for a routing tag: "NJF" → "HUB-NJF". Every hub code is "HUB-" + its three-letter tag.</summary>
    public static string CodeFor(string routingTag) => "HUB-" + routingTag.Trim().ToUpperInvariant();

    /// <summary>Routing tag of a hub code: "HUB-NJF" → "NJF" (what labels, manifests and notes show).</summary>
    public static string TagOf(string? hubCode) =>
        hubCode is { Length: > 4 } code && code.StartsWith("HUB-", StringComparison.OrdinalIgnoreCase) ? code[4..].ToUpperInvariant() : hubCode ?? "";
}
