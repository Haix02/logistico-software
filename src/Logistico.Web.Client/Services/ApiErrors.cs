using System.Net;
using System.Net.Http.Json;

namespace Logistico.Web.Client.Services;

/// <summary>RFC 9457 problem details as returned by the API (titles are already in the user's language).</summary>
internal sealed class ApiProblem
{
    public string? Type { get; set; }
    public string? Title { get; set; }
    public string? Detail { get; set; }
    public int? Status { get; set; }
    public string? Code { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
}

internal sealed class ApiException(HttpStatusCode statusCode, ApiProblem? problem)
    : Exception(problem?.Title ?? $"The server answered {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public ApiProblem? Problem { get; } = problem;

    public string UserMessage =>
        Problem?.Errors is { Count: > 0 } errors
            ? string.Join(" ", errors.SelectMany(e => e.Value).Distinct())
            : string.Join(" — ", new[] { Problem?.Title, Problem?.Detail }.Where(s => !string.IsNullOrWhiteSpace(s)).DefaultIfEmpty(Message));

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ApiProblem? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<ApiProblem>(ct);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
        }

        throw new ApiException(response.StatusCode, problem);
    }
}
