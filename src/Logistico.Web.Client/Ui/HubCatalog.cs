using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel.Geo;
using Logistico.SharedKernel.Iraq;

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

    /// <summary>The company's country. Iraq uses the governorates; any other country uses <see cref="Places"/>.</summary>
    public static string Country { get; private set; } = Countries.Iraq;

    public static IReadOnlyList<PlaceInfo> Places { get; private set; } = [];

    public static bool UsesIraqCatalog => Countries.IsIraq(Country);

    /// <summary>Raised after a new network was loaded (warehouses added, moved or retired).</summary>
    public static event Action? Changed;

    public static void Set(IReadOnlyList<HubInfo> hubs, string? country = null, IReadOnlyList<PlaceInfo>? places = null)
    {
        if (!string.IsNullOrWhiteSpace(country))
        {
            Country = Countries.Normalize(country);
        }

        if (places is not null)
        {
            Places = places;
        }

        if (hubs.Count == 0)
        {
            IsLoaded = true;
            Changed?.Invoke();
            return;
        }

        var known = UsesIraqCatalog ? null : Places.Select(place => place.Code);
        Network = new HubNetwork(hubs, iraqCatalog: UsesIraqCatalog, knownPlaces: known);
        IsLoaded = true;
        Changed?.Invoke();
    }

    /// <summary>Where a domestic order can go: the governorates in Iraq, or the company's own regions.</summary>
    public static IReadOnlyList<PlaceInfo> Destinations =>
        UsesIraqCatalog
            ? IraqGovernorates.All.Select(g => new PlaceInfo(g.Code, g.NameEn, g.NameAr, g.Latitude, g.Longitude)).ToList()
            : Places;

    public static string PlaceName(string? code) =>
        Destinations.FirstOrDefault(place => string.Equals(place.Code, code, StringComparison.OrdinalIgnoreCase))?.Name
        ?? IraqGovernorates.NameOrCode(code ?? "");

    public static IReadOnlyList<HubInfo> All => Network.All;

    public static HubInfo Primary => Network.Primary;

    public static IReadOnlyList<ActionBarcodeSpec> ActionBarcodes => Network.ActionBarcodes;

    public static HubInfo? Find(string? hubCode) => Network.Find(hubCode);

    public static HubInfo FindOrDefault(string? hubCode) => Network.FindOrDefault(hubCode);

    public static HubInfo ResolveDestinationHub(string? locationCode) => Network.ResolveDestinationHub(locationCode);

    public static bool TryParseActionBarcode(string? raw, out ActionBarcodeCommand command) => Network.TryParseActionBarcode(raw, out command);
}
