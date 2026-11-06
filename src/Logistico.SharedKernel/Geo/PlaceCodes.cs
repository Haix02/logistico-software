using System.Text.RegularExpressions;

namespace Logistico.SharedKernel.Geo;

/// <summary>
/// A domestic area code: two letters for the country and two for the area, as in IQ-BG or JO-AM.
/// Iraqi governorate codes already follow it.
/// </summary>
public static partial class PlaceCodes
{
    public static bool IsValid(string? code) => code is not null && Pattern().IsMatch(code.Trim());

    public static string Normalize(string code) => code.Trim().ToUpperInvariant();

    [GeneratedRegex("^[A-Za-z]{2}-[A-Za-z0-9]{2}$")]
    private static partial Regex Pattern();
}
