using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Logistico.Web.Client.Services;

/// <summary>Shared plumbing for the typed API clients: JSON calls, If-Match headers, problem details, query strings.</summary>
internal abstract class ApiClient(HttpClient http)
{
    protected HttpClient Http => http;

    protected async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        await ApiException.EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }

    /// <summary>GET that returns null for 404 instead of throwing.</summary>
    protected async Task<T?> FindAsync<T>(string url, CancellationToken ct)
        where T : class
    {
        using var response = await http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await ApiException.EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(ct);
    }

    protected async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, uint? version, CancellationToken ct)
    {
        using var response = await SendRawAsync(method, url, body, version, ct);
        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }

    protected async Task SendAsync(HttpMethod method, string url, object? body, uint? version, CancellationToken ct)
    {
        using var _ = await SendRawAsync(method, url, body, version, ct);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string url, object? body, uint? version, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body, body.GetType()) };
        if (version is { } v)
        {
            message.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{v}\""));
        }

        var response = await http.SendAsync(message, ct);
        try
        {
            await ApiException.EnsureSuccessAsync(response, ct);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>Builds "?a=1&amp;b=x" from name/value pairs, skipping empty values.</summary>
    protected static string Query(params (string Name, object? Value)[] parts)
    {
        var items = parts
            .Where(p => p.Value is not null && (p.Value is not string s || !string.IsNullOrWhiteSpace(s)))
            .Select(p => $"{p.Name}={Uri.EscapeDataString(Format(p.Value!))}")
            .ToList();
        return items.Count == 0 ? "" : "?" + string.Join('&', items);
    }

    private static string Format(object value) => value switch
    {
        bool b => b ? "true" : "false",
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
