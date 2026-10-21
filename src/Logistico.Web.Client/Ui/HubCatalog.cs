using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Web.Client.Ui;

/// <summary>
/// The signed-in company's warehouse network in the browser. The layout loads it once (and again after warehouses
/// change) before pages render; one browser tab serves one company, so a single shared instance is enough — the same
/// way <see cref="System.Globalization.CultureInfo.CurrentCulture"/> is shared.
/// Until it loads (and on anonymous pages) it holds the standard network.
/// </summary>
public static class HubCatalog
{
    public static HubNetwork Network { get; private set; } = IraqHubs.Default;

    public static bool IsLoaded { get; private set; }

    /// <summary>Raised after a new network was loaded (warehouses added, moved or retired).</summary>
    public static event Action? Changed;

    public static void Set(IReadOnlyList<HubInfo> hubs)
    {
        if (hubs.Count == 0)
        {
            return;
        }

        Network = new HubNetwork(hubs);
        IsLoaded = true;
        Changed?.Invoke();
    }

    public static IReadOnlyList<HubInfo> All => Network.All;

    public static HubInfo Primary => Network.Primary;

    public static IReadOnlyList<ActionBarcodeSpec> ActionBarcodes => Network.ActionBarcodes;

    public static HubInfo? Find(string? hubCode) => Network.Find(hubCode);

    public static HubInfo FindOrDefault(string? hubCode) => Network.FindOrDefault(hubCode);

    public static HubInfo ResolveDestinationHub(string? locationCode) => Network.ResolveDestinationHub(locationCode);

    public static bool TryParseActionBarcode(string? raw, out ActionBarcodeCommand command) => Network.TryParseActionBarcode(raw, out command);
}
