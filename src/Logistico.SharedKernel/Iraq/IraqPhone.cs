namespace Logistico.SharedKernel.Iraq;

/// <summary>Iraqi mobile numbers (Zain 078/079, Asiacell 077, Korek 075, etc.) normalised to +9647XXXXXXXXX.</summary>
public static class IraqPhone
{
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("00964", StringComparison.Ordinal))
        {
            digits = digits[5..];
        }
        else if (digits.StartsWith("964", StringComparison.Ordinal))
        {
            digits = digits[3..];
        }
        else if (digits.StartsWith('0'))
        {
            digits = digits[1..];
        }

        return digits.Length == 10 && digits[0] == '7' ? "+964" + digits : null;
    }

    public static bool IsValid(string? input) => Normalize(input) is not null;

    /// <summary>Local display form: 0770 123 4567.</summary>
    public static string Display(string? e164) =>
        e164 is { Length: 14 } && e164.StartsWith("+9647", StringComparison.Ordinal)
            ? $"0{e164[4..7]} {e164[7..10]} {e164[10..]}"
            : e164 ?? "";

    /// <summary>Digits for wa.me links (country code, no plus).</summary>
    public static string? WhatsAppDigits(string? e164) => e164?.TrimStart('+');
}
