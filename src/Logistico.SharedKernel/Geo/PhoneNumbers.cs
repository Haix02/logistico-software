using Logistico.SharedKernel.Iraq;

namespace Logistico.SharedKernel.Geo;

/// <summary>
/// A phone the company can call: an Iraqi mobile in any of the local forms, or an international number written with a
/// leading plus (E.164). Stored as E.164.
/// </summary>
public static class PhoneNumbers
{
    public static string? Normalize(string? input)
    {
        if (IraqPhone.Normalize(input) is { } iraqi)
        {
            return iraqi;
        }

        if (string.IsNullOrWhiteSpace(input) || !input.Trim().StartsWith('+'))
        {
            return null;
        }

        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        return digits.Length is >= 8 and <= 15 ? "+" + digits : null;
    }

    public static bool IsValid(string? input) => Normalize(input) is not null;

    /// <summary>Iraqi numbers in the local form; anything else stays as stored.</summary>
    public static string Display(string? e164) => IraqPhone.Display(e164);
}
