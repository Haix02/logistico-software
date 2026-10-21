using System.Globalization;
using System.Text.RegularExpressions;

namespace Logistico.Web.Client.Ui;

/// <summary>
/// Right-to-left helpers. Latin/number runs inside Arabic text (courier codes, shop names, "4×6") are isolated so
/// their punctuation keeps left-to-right order, and directional icons point the way the page reads.
/// </summary>
public static partial class Bidi
{
    public static bool Rtl => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

    /// <summary>"Next / forward" in reading order: → in English, ← in Arabic.</summary>
    public static string Forward => Rtl ? "arrow-left" : "arrow-right";
    public static string Back => Rtl ? "arrow-right" : "arrow-left";
    public static string ChevronForward => Rtl ? "chevron-left" : "chevron-right";
    public static string ChevronBack => Rtl ? "chevron-right" : "chevron-left";

    /// <summary>Arrow used between two places ("Baghdad → Basra"), pointing in reading direction.</summary>
    public static string Arrow => Rtl ? "←" : "→";

    /// <summary>Splits text into runs; runs flagged <c>Ltr</c> must be rendered inside a left-to-right isolate.</summary>
    public static IEnumerable<(string Text, bool Ltr)> Split(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        if (!Rtl || !HasArabic(text))
        {
            yield return (text, false);
            yield break;
        }

        var last = 0;
        foreach (Match match in LatinRun().Matches(text))
        {
            if (match.Index > last)
            {
                yield return (text[last..match.Index], false);
            }

            yield return (match.Value, true);
            last = match.Index + match.Length;
        }

        if (last < text.Length)
        {
            yield return (text[last..], false);
        }
    }

    private static bool HasArabic(string text)
    {
        foreach (var c in text)
        {
            if (c is >= '؀' and <= 'ۿ')
            {
                return true;
            }
        }

        return false;
    }

    // Latin letters/digits with the punctuation that usually sits between them (codes, names, sizes).
    // Brackets stay outside the run so they mirror together with the surrounding Arabic text.
    [GeneratedRegex(@"[A-Za-z0-9][A-Za-z0-9 \-./:#+_×&,'’]*[A-Za-z0-9]|[A-Za-z0-9]")]
    private static partial Regex LatinRun();
}
