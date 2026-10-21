using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Logistico.Web.Client.Ui;

/// <summary>
/// Count-aware texts. A resource key may have plural variants named after the CLDR categories — <c>Key_One</c>,
/// <c>Key_Two</c>, <c>Key_Few</c>, <c>Key_Many</c> — and the base key covers everything else. English only needs
/// <c>_One</c> ("1 parcel"); Arabic uses all four ("طرد واحد", "طردان", "3 طرود", "11 طردًا", "100 طرد").
/// </summary>
public static class Plural
{
    /// <summary>The text for <paramref name="count"/>: <c>{0}</c> is the formatted count, <c>{1}…</c> are <paramref name="more"/>.</summary>
    public static string Count(this IStringLocalizer localizer, string key, int count, params object?[] more)
    {
        var suffix = Suffix(count, CultureInfo.CurrentUICulture);
        var text = suffix.Length == 0 ? localizer[key] : localizer[key + suffix];
        if (text.ResourceNotFound)
        {
            text = localizer[key];
        }

        object?[] args = [Fmt.Number(count), .. more];
        return string.Format(CultureInfo.CurrentCulture, text.Value, args);
    }

    /// <summary>CLDR plural category of a whole number, as a resource-key suffix ("" for "other").</summary>
    public static string Suffix(long count, CultureInfo culture)
    {
        var n = Math.Abs(count);
        if (culture.TwoLetterISOLanguageName != "ar")
        {
            return n == 1 ? "_One" : "";
        }

        return (n, n % 100) switch
        {
            (1, _) => "_One",
            (2, _) => "_Two",
            (_, >= 3 and <= 10) => "_Few",
            (_, >= 11 and <= 99) => "_Many",
            _ => "",
        };
    }
}
