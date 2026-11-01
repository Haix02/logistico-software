namespace Logistico.Modules.Shipments.Contracts.Rules;

/// <summary>
/// Natural order for shelf names, the way people walk a hub: A-2 before A-10, B-01-9 before B-01-10. Runs of digits
/// compare by value, everything else letter by letter ignoring case. The database sorts shelf columns the same way
/// with its "natural" collation (ICU, numeric ordering).
/// </summary>
public sealed class ShelfOrder : IComparer<string?>
{
    public static readonly ShelfOrder Instance = new();

    /// <summary>The PostgreSQL collation that sorts shelf columns in natural order.</summary>
    public const string Collation = "natural";

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null || y is null)
        {
            return x is null ? 1 : -1; // parcels without a shelf come last
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var (a, nextI) = Digits(x, i);
                var (b, nextJ) = Digits(y, j);
                var byValue = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
                if (byValue != 0)
                {
                    return byValue;
                }

                i = nextI;
                j = nextJ;
                continue;
            }

            var byLetter = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (byLetter != 0)
            {
                return byLetter;
            }

            i++;
            j++;
        }

        var byLength = (x.Length - i).CompareTo(y.Length - j);
        return byLength != 0 ? byLength : string.CompareOrdinal(x, y);
    }

    /// <summary>The run of digits starting at <paramref name="start"/>, without leading zeros.</summary>
    private static (string Value, int End) Digits(string s, int start)
    {
        var end = start;
        while (end < s.Length && char.IsAsciiDigit(s[end]))
        {
            end++;
        }

        var first = start;
        while (first < end - 1 && s[first] == '0')
        {
            first++;
        }

        return (s[first..end], end);
    }
}
