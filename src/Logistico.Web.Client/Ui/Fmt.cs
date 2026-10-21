using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Logistico.Web.Client.Ui;

/// <summary>Culture-aware display formatting (Arabic/English) used across pages.</summary>
public static class Fmt
{
    public static string Money(decimal? amount, string? currency)
    {
        if (amount is null)
        {
            return "—";
        }

        var format = currency is "IQD" ? "N0" : "N2";
        var number = amount.Value.ToString(format, CultureInfo.CurrentCulture);
        var symbol = currency switch
        {
            "IQD" => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? "د.ع" : "IQD",
            "USD" => "$",
            null => "",
            _ => currency,
        };
        return currency == "USD" ? $"{symbol}{number}" : $"{number} {symbol}".Trim();
    }

    public static string Totals(IEnumerable<Logistico.Modules.Shipments.Contracts.MoneyTotal> totals)
    {
        var parts = totals.OrderBy(t => t.Currency == "IQD" ? 0 : 1).Select(t => Money(t.Amount, t.Currency)).ToList();
        return parts.Count == 0 ? "—" : string.Join(" + ", parts);
    }

    /// <summary>Short form for dashboards: 131.9M IQD / 131.9 مليون د.ع. Use the exact amount as a tooltip.</summary>
    public static string Compact(decimal? amount, string? currency)
    {
        if (amount is null)
        {
            return "—";
        }

        var value = Math.Abs(amount.Value);
        var arabic = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        (decimal Divisor, string Suffix)? scale = value switch
        {
            >= 1_000_000_000m => (1_000_000_000m, arabic ? " مليار" : "B"),
            >= 1_000_000m => (1_000_000m, arabic ? " مليون" : "M"),
            >= 100_000m => (1_000m, arabic ? " ألف" : "K"),
            _ => null,
        };
        if (scale is null)
        {
            return Money(amount, currency);
        }

        var number = (amount.Value / scale.Value.Divisor).ToString("#,0.#", CultureInfo.CurrentCulture) + scale.Value.Suffix;
        var symbol = currency switch
        {
            "IQD" => arabic ? "د.ع" : "IQD",
            "USD" => "$",
            null => "",
            _ => currency,
        };
        return currency == "USD" ? $"{symbol}{number}" : $"{number} {symbol}".Trim();
    }

    public static string CompactTotals(IEnumerable<Logistico.Modules.Shipments.Contracts.MoneyTotal> totals)
    {
        var parts = totals.OrderBy(t => t.Currency == "IQD" ? 0 : 1).Select(t => Compact(t.Amount, t.Currency)).ToList();
        return parts.Count == 0 ? "—" : string.Join(" + ", parts);
    }

    public static string Date(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.CurrentCulture) ?? "—";

    public static string DateTime(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd MMM yyyy  HH:mm", CultureInfo.CurrentCulture) ?? "—";

    public static string Time(DateTimeOffset value) =>
        value.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);

    public static string Number(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string Number(decimal value, int decimals = 1) => value.ToString("N" + decimals, CultureInfo.CurrentCulture);

    /// <summary>"5 min ago", "3 h ago", "2 days ago" (localised).</summary>
    public static string Ago(DateTimeOffset value, IStringLocalizer L)
    {
        var span = DateTimeOffset.UtcNow - value;
        if (span.TotalMinutes < 1)
        {
            return L["Ago_JustNow"];
        }

        if (span.TotalHours < 1)
        {
            return L.Count("Ago_Minutes", (int)span.TotalMinutes);
        }

        if (span.TotalDays < 1)
        {
            return L.Count("Ago_Hours", (int)span.TotalHours);
        }

        return L.Count("Ago_Days", (int)span.TotalDays);
    }

    /// <summary>Governorate, port or airport name for a location code (IQ-BG, IQUQR, CNSHA).</summary>
    public static string Place(string? code) => Logistico.Modules.Shipments.Contracts.Rules.TradeLocations.Name(code);

    /// <summary>Event location: a hub code becomes the hub's name, a place code its name, free text stays as it is.</summary>
    public static string Location(string? location) =>
        HubCatalog.Find(location)?.Name ?? Place(location);

    /// <summary>Hub name for a hub code, or "—".</summary>
    public static string Hub(string? hubCode) => HubCatalog.Find(hubCode)?.Name ?? "—";

    /// <summary>Compact amount split into number and unit for KPI cards: ("131.9", "مليون د.ع").</summary>
    public static (string Number, string Unit) CompactParts(decimal? amount, string? currency)
    {
        var text = Compact(amount, currency);
        if (amount is null)
        {
            return (text, "");
        }

        if (currency == "USD")
        {
            return (text.TrimStart('$'), "$");
        }

        var cut = text.IndexOf(' ', StringComparison.Ordinal);
        return cut < 0 ? (text, "") : (text[..cut], text[(cut + 1)..]);
    }

    /// <summary>Compact parts for a set of currency totals (IQD first; other currencies appended to the unit).</summary>
    public static (string Number, string Unit) CompactTotalParts(IEnumerable<Logistico.Modules.Shipments.Contracts.MoneyTotal> totals)
    {
        var list = totals.OrderBy(t => t.Currency == "IQD" ? 0 : 1).ToList();
        if (list.Count == 0)
        {
            return ("0", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? "د.ع" : "IQD");
        }

        var (number, unit) = CompactParts(list[0].Amount, list[0].Currency);
        return list.Count == 1 ? (number, unit) : (number, unit + " + " + string.Join(" + ", list.Skip(1).Select(t => Compact(t.Amount, t.Currency))));
    }

    /// <summary>A percentage such as 92.4 → "92.4%" (one decimal, none for whole numbers).</summary>
    public static string Percent(decimal? value) =>
        value is { } v ? v.ToString(v == Math.Round(v) ? "N0" : "N1", CultureInfo.CurrentCulture) + "%" : "—";

    /// <summary>A duration given in hours, as "26.4 h" below two days and "3.2 days" above.</summary>
    public static (string Number, string Unit) Duration(decimal hours, IStringLocalizer L) =>
        hours < 48 ? (Number(hours), L["Unit_Hours"]) : (Number(hours / 24), L["Unit_Days"]);

    /// <summary>Hours as "5 h" / "2 days".</summary>
    public static string Hours(int? hours, IStringLocalizer L) =>
        hours is null ? "—"
        : hours < 48 ? L.Count("HoursN", hours.Value)
        : L.Count("DaysN", hours.Value / 24);
}
