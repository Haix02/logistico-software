using Logistico.Modules.Shipments.Contracts;

namespace Logistico.Web.Client.Services;

/// <summary>
/// App-wide operational state shared by the shell and pages: side-menu counters (refreshed in the background and
/// after any change) and requests to open the command palette or the shortcut list.
/// </summary>
internal sealed class OpsState(OperationsApi api)
{
    public OpsCounters? Counters { get; private set; }

    public long LastLocalScanSeq { get; set; }

    public event Action? Changed;

    public event Action? PaletteRequested;

    public event Action? HelpRequested;

    public async Task RefreshAsync()
    {
        try
        {
            Counters = await api.GetCountersAsync();
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiException or TaskCanceledException)
        {
            // Counters are best-effort; the next refresh will try again.
        }
    }

    public void OpenPalette() => PaletteRequested?.Invoke();

    public void OpenHelp() => HelpRequested?.Invoke();
}
