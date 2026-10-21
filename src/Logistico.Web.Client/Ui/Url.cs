using System.Globalization;

namespace Logistico.Web.Client.Ui;

/// <summary>Builds page links with query strings ("transfers?status=InTransit&amp;page=2"), skipping empty values.</summary>
public static class Url
{
    public static string With(string path, params (string Key, object? Value)[] parts)
    {
        var query = string.Join('&', parts
            .Select(p => (p.Key, Value: Format(p.Value)))
            .Where(p => p.Value.Length > 0)
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return query.Length == 0 ? path : $"{path}?{query}";
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
