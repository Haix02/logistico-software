namespace Logistico.Modules.Shipments.Contracts.Rules;

/// <summary>
/// Tracking numbers look like <c>LX26000123457</c>: prefix "LX", 2-digit year, 8-digit sequence, Luhn check digit.
/// The check digit catches most typing mistakes before a lookup is made.
/// </summary>
public static class TrackingNumber
{
    public const string Prefix = "LX";
    public const int Length = 13;
    public const long MaxSequence = 99_999_999;

    public static string Create(int year, long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sequence, MaxSequence);
        var body = $"{year % 100:00}{sequence:00000000}";
        return $"{Prefix}{body}{LuhnCheckDigit(body)}";
    }

    public static bool IsValid(string? value)
    {
        var candidate = Normalize(value);
        if (candidate.Length != Length || !candidate.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var digits = candidate.AsSpan(Prefix.Length);
        foreach (var c in digits)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return LuhnCheckDigit(digits[..^1]) == digits[^1] - '0';
    }

    /// <summary>Upper-cases and strips spaces and dashes, so "lx 26-0001-2345-7" is accepted.</summary>
    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : string.Concat(value.Where(c => c is not (' ' or '-'))).ToUpperInvariant();

    /// <summary>Luhn (mod 10) check digit for a string of decimal digits.</summary>
    public static int LuhnCheckDigit(ReadOnlySpan<char> digits)
    {
        var sum = 0;
        var doubleIt = true; // the digit next to the (future) check digit is doubled
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            sum += d;
            doubleIt = !doubleIt;
        }

        return (10 - sum % 10) % 10;
    }
}
