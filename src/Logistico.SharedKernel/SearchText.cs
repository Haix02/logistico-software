using System.Text;

namespace Logistico.SharedKernel;

/// <summary>
/// Free-text search that finds what people type, the way they type it. Letter case, the Arabic letters that are written
/// interchangeably (أ إ آ ٱ → ا, ة → ه, ى → ي, ؤ → و, ئ → ي), Arabic-Indic and Persian digits, harakat and tatweel all fold to
/// one form; a phone number matches with or without its leading 0 or +964; a query of several words matches when every
/// word appears, in any order and in any of the searched fields.
/// </summary>
/// <remarks>
/// The database keeps the same folded form of the searched fields in a generated column built with <see cref="Sql"/>,
/// so <see cref="Fold"/> and PostgreSQL must fold identically: both lower-case and then map <see cref="FoldFrom"/> to
/// <see cref="FoldTo"/> character by character, dropping the characters of <see cref="FoldFrom"/> that have no partner.
/// </remarks>
public static class SearchText
{
    /// <summary>أ إ آ ٱ → ا, ى → ي, ة → ه, ؤ → و, ئ → ي.</summary>
    private const string LettersFrom = "\u0623\u0625\u0622\u0671\u0649\u0629\u0624\u0626";
    private const string LettersTo = "\u0627\u0627\u0627\u0627\u064A\u0647\u0648\u064A";

    /// <summary>Arabic-Indic (٠…٩) and Persian (۰…۹) digits → 0…9.</summary>
    private const string DigitsFrom = "\u0660\u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0668\u0669\u06F0\u06F1\u06F2\u06F3\u06F4\u06F5\u06F6\u06F7\u06F8\u06F9";
    private const string DigitsTo = "01234567890123456789";

    /// <summary>Tatweel, harakat (fathatan … sukun) and the superscript alef: they never change which word it is.</summary>
    private const string Dropped = "\u0640\u064B\u064C\u064D\u064E\u064F\u0650\u0651\u0652\u0670";

    public const string FoldFrom = LettersFrom + DigitsFrom + Dropped;
    public const string FoldTo = LettersTo + DigitsTo;

    /// <summary>The most words a query may have; the rest are ignored.</summary>
    public const int MaxWords = 6;

    private static readonly Dictionary<char, char?> Map = BuildMap();

    /// <summary>Lower-cases <paramref name="text"/> and folds Arabic letter variants, digits and marks.</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var folded = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            if (!Map.TryGetValue(c, out var to))
            {
                folded.Append(c);
            }
            else if (to is { } replacement)
            {
                folded.Append(replacement);
            }
        }

        return folded.ToString();
    }

    /// <summary>
    /// The folded words of a query. A query made only of digits and phone punctuation is one word: its digits without a
    /// leading 00964, 964 or 0, so "0770 123" finds +9647701234567. A tracking number typed in groups ("LX26 0000") is
    /// one word too.
    /// </summary>
    public static IReadOnlyList<string> Words(string? query)
    {
        var folded = Fold(query).Trim();
        if (folded.Length == 0)
        {
            return [];
        }

        if (folded.All(c => char.IsAsciiDigit(c) || c is ' ' or '-' or '+' or '(' or ')' or '/'))
        {
            var digits = new string(folded.Where(char.IsAsciiDigit).ToArray());
            if (digits.Length == 0)
            {
                return [];
            }

            return [WithoutPhonePrefix(digits)];
        }

        var compact = folded.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (compact.Length > 2 && compact.StartsWith("lx", StringComparison.Ordinal) && compact[2..].All(char.IsAsciiDigit))
        {
            return [compact];
        }

        // "@shop" finds the Instagram handle, which is stored without the @.
        return folded
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => w.TrimStart('@'))
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxWords)
            .ToList();
    }

    /// <summary>Words this short only match the start of a word ("1" finds "Customer 1", not every number with a 1 in it).</summary>
    public const int ShortWord = 2;

    /// <summary>
    /// The LIKE pattern for <paramref name="word"/> (backslash escapes %, _ and itself). The searched text must start with a
    /// space (see <see cref="Sql"/>): a longer word matches anywhere, a short one only at the start of a word.
    /// </summary>
    public static string Pattern(string word)
    {
        var escaped = word.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
        return word.Length <= ShortWord ? "% " + escaped + "%" : "%" + escaped + "%";
    }

    /// <summary>True when every word of <paramref name="query"/> appears in one of <paramref name="fields"/> (in-memory lists).</summary>
    public static bool Matches(string? query, params string?[] fields)
    {
        var words = Words(query);
        if (words.Count == 0)
        {
            return true;
        }

        var haystack = " " + Fold(string.Join(' ', fields.Where(f => !string.IsNullOrEmpty(f))));
        return words.All(w => haystack.Contains(w.Length <= ShortWord ? " " + w : w, StringComparison.Ordinal));
    }

    /// <summary>
    /// PostgreSQL that folds <paramref name="expression"/> exactly like <see cref="Fold"/>, with the leading space that
    /// <see cref="Pattern"/> expects (immutable, so usable in generated columns and indexes).
    /// </summary>
    public static string Sql(string expression) => $"translate(lower(' ' || {expression}), '{FoldFrom}', '{FoldTo}')";

    private static string WithoutPhonePrefix(string digits)
    {
        if (digits.StartsWith("00964", StringComparison.Ordinal) && digits.Length > 5)
        {
            return digits[5..];
        }

        if (digits.StartsWith("964", StringComparison.Ordinal) && digits.Length > 6)
        {
            return digits[3..];
        }

        return digits.Length > 1 && digits[0] == '0' ? digits[1..] : digits;
    }

    private static Dictionary<char, char?> BuildMap()
    {
        var map = new Dictionary<char, char?>();
        for (var i = 0; i < FoldFrom.Length; i++)
        {
            map[FoldFrom[i]] = i < FoldTo.Length ? FoldTo[i] : null;
        }

        return map;
    }
}
