namespace Logistico.Web.Client.Services;

/// <summary>
/// One load at a time for a page whose filters change quickly (search as you type, paging): starting a load cancels the
/// one before it, and only the newest load's result is used, so an older, slower answer never replaces a newer one.
/// </summary>
internal sealed class LatestLoad
{
    private CancellationTokenSource? current;

    /// <summary>True while the newest load is still running.</summary>
    public bool Busy { get; private set; }

    /// <summary>
    /// Runs <paramref name="load"/> and returns its result, or <c>(false, default)</c> when a newer load started meanwhile.
    /// Errors of the newest load are thrown; those of superseded loads are dropped.
    /// </summary>
    public async Task<(bool IsLatest, T? Value)> RunAsync<T>(Func<CancellationToken, Task<T>> load)
    {
        current?.Cancel();
        var cts = new CancellationTokenSource();
        current = cts;
        Busy = true;
        try
        {
            var value = await load(cts.Token);
            return ReferenceEquals(cts, current) ? (true, value) : (false, default);
        }
        catch (Exception) when (!ReferenceEquals(cts, current))
        {
            return (false, default);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return (false, default);
        }
        finally
        {
            // The newest load is over: forget its token before disposing it, so the next load does not cancel a disposed one.
            if (ReferenceEquals(cts, current))
            {
                Busy = false;
                current = null;
            }

            cts.Dispose();
        }
    }
}
