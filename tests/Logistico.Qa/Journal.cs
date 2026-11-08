using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Logistico.Qa;

/// <summary>Every API call the agents made: for the "no server error" check and the latency report.</summary>
public sealed partial class Journal
{
    private readonly ConcurrentQueue<Call> calls = new();

    public sealed record Call(string Agent, string Method, string Path, int Status, double Milliseconds, string? Failure);

    public IReadOnlyCollection<Call> Calls => calls;

    public IEnumerable<Call> ServerErrors => calls.Where(c => c.Status >= 500);

    public void Add(Call call) => calls.Enqueue(call);

    public double Percentile(double percent, Func<Call, bool>? filter = null)
    {
        var times = calls.Where(filter ?? (_ => true)).Select(c => c.Milliseconds).Order().ToList();
        return times.Count == 0 ? 0 : times[(int)Math.Ceiling(percent / 100 * times.Count) - 1];
    }

    /// <summary>A Markdown table per endpoint (ids folded together), slowest first.</summary>
    public string Report()
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Calls: {calls.Count}. p50 {Percentile(50):0} ms, p95 {Percentile(95):0} ms, p99 {Percentile(99):0} ms.");
        text.AppendLine();
        text.AppendLine("| Endpoint | Calls | p50 ms | p95 ms | Max ms | Non-2xx |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var group in calls.GroupBy(c => $"{c.Method} {Fold(c.Path)}").OrderByDescending(g => g.Max(c => c.Milliseconds)))
        {
            var times = group.Select(c => c.Milliseconds).Order().ToList();
            var p95 = times[(int)Math.Ceiling(.95 * times.Count) - 1];
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {group.Key} | {times.Count} | {times[times.Count / 2]:0} | {p95:0} | {times[^1]:0} | {group.Count(c => c.Status is < 200 or >= 300)} |");
        }

        return text.ToString();
    }

    private static string Fold(string path) => Tracking().Replace(Id().Replace(path.Split('?')[0], "{id}"), "{tracking}");

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F-]{27}")]
    private static partial Regex Id();

    [GeneratedRegex("LX[0-9]{11}")]
    private static partial Regex Tracking();

    /// <summary>Records every request of one agent.</summary>
    public sealed class Recorder(Journal journal, string agent) : DelegatingHandler(new SocketsHttpHandler())
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var clock = Stopwatch.StartNew();
            var response = await base.SendAsync(request, cancellationToken);
            await response.Content.LoadIntoBufferAsync(cancellationToken);
            string? failure = (int)response.StatusCode >= 400 ? await response.Content.ReadAsStringAsync(cancellationToken) : null;
            journal.Add(new Call(agent, request.Method.Method, request.RequestUri!.PathAndQuery, (int)response.StatusCode, clock.Elapsed.TotalMilliseconds, failure));
            return response;
        }
    }
}
