using System.Globalization;
using System.Text.Json.Serialization;
using Logistico.SharedKernel.Iraq;

namespace Logistico.Modules.Shipments.Contracts.Rules;

/// <summary>
/// A company's warehouse network: which hub delivers to (and collects from) each governorate, how long trucks take
/// between hubs, and the action barcodes of its hub stations. Built from the company's active warehouses on the server,
/// and from <c>GET /api/v1/network/hubs</c> in the browser, so both sides route parcels the same way.
/// </summary>
public sealed class HubNetwork
{
    // Iraqi ports and airports belong to the governorate they are in.
    private static readonly Dictionary<string, string> PortGovernorates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IQUQR"] = "IQ-BA",
        ["IQBSR"] = "IQ-BA",
        ["IQBGW"] = "IQ-BG",
        ["IQEBL"] = "IQ-AR",
        ["IQISU"] = "IQ-SU",
        ["IQNJF"] = "IQ-NA",
    };

    private const double RoadFactor = 1.2;      // roads are about 20 % longer than the straight line
    private const double TruckKmPerHour = 80;
    private const double HandlingHours = 0.5;   // gate, weighbridge and unloading

    private readonly Dictionary<string, HubInfo> byCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HubInfo> byTag = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HubInfo> byGovernorate = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="hubs">Active hubs, the primary hub first. A governorate covered twice goes to the first hub listing it.</param>
    public HubNetwork(IEnumerable<HubInfo> hubs)
    {
        All = hubs.ToList();
        if (All.Count == 0)
        {
            throw new ArgumentException("A network needs at least one hub.", nameof(hubs));
        }

        foreach (var hub in All)
        {
            byCode.TryAdd(hub.Code, hub);
            byTag.TryAdd(hub.RoutingTag, hub);
            foreach (var governorate in hub.Covers)
            {
                byGovernorate.TryAdd(governorate, hub);
            }
        }

        ActionBarcodes =
        [
            new("CMD:MODE:RECEIVE", "استلام على الرف", "Mode: Receive to Shelf"),
            new("CMD:MODE:DISPATCH", "تسليم للمندوب", "Mode: Dispatch to Courier"),
            new("CMD:MODE:LOAD", "تحميل على شاحنة النقل", "Mode: Load Line-haul Truck"),
            new("CMD:MODE:VERIFY", "فحص ومطابقة المسار", "Mode: Audit & Verify Hub"),
            .. All.Select(h => new ActionBarcodeSpec("CMD:HUB:" + h.Code, $"محطة {h.ArabicName} ({h.RoutingTag})", $"Switch Hub: {h.EnglishName} ({h.RoutingTag})")),
            new("BIN:A-01-1", "رف A-01-1", "Shelf Bin A-01-1"),
            new("BIN:B-02-3", "رف B-02-3", "Shelf Bin B-02-3"),
        ];
    }

    /// <summary>Active hubs, primary first.</summary>
    public IReadOnlyList<HubInfo> All { get; }

    /// <summary>The hub used when nothing else applies (usually the head-office hub).</summary>
    public HubInfo Primary => All[0];

    /// <summary>Barcodes printed for hub stations: scan modes, one per hub, and sample shelves.</summary>
    public IReadOnlyList<ActionBarcodeSpec> ActionBarcodes { get; }

    /// <summary>The hub with this code ("HUB-NJF") or routing tag ("NJF"); null when it is not an active hub.</summary>
    public HubInfo? Find(string? hubCodeOrTag)
    {
        if (string.IsNullOrWhiteSpace(hubCodeOrTag))
        {
            return null;
        }

        var key = hubCodeOrTag.Trim();
        return byCode.GetValueOrDefault(key) ?? byTag.GetValueOrDefault(key);
    }

    /// <summary>Like <see cref="Find"/>, falling back to a stand-in for retired hubs so old records still display.</summary>
    public HubInfo FindOrDefault(string? hubCodeOrTag)
    {
        if (Find(hubCodeOrTag) is { } hub)
        {
            return hub;
        }

        if (string.IsNullOrWhiteSpace(hubCodeOrTag))
        {
            return Primary;
        }

        var tag = IraqHubs.TagOf(hubCodeOrTag.Trim());
        return new HubInfo(IraqHubs.CodeFor(tag), tag, "", tag, tag);
    }

    /// <summary>The hub that delivers to a place (governorate "IQ-BA" or Iraqi port "IQUQR"); the primary hub for anything else.</summary>
    public HubInfo ResolveDestinationHub(string? locationCode)
    {
        if (GovernorateOf(locationCode) is not { } governorate)
        {
            return Primary;
        }

        if (byGovernorate.TryGetValue(governorate, out var hub))
        {
            return hub;
        }

        // Not covered by any hub (e.g. its hub was retired): the nearest hub takes it.
        var place = IraqGovernorates.Find(governorate);
        return place is null || All.All(h => h.Latitude == 0)
            ? Primary
            : All.MinBy(h => DistanceKm(place.Latitude, place.Longitude, h.Latitude, h.Longitude))!;
    }

    /// <summary>The hub that collects from an Iraqi origin (seller's governorate, Iraqi port); null for origins abroad.</summary>
    public HubInfo? OriginHubFor(string? originCode) => GovernorateOf(originCode) is null ? null : ResolveDestinationHub(originCode);

    /// <summary>Typical road time for a line-haul truck between two hubs (hours), used for ETAs and overdue alerts.</summary>
    public int TransitHours(string fromHub, string toHub)
    {
        if (string.Equals(fromHub, toHub, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var from = Find(fromHub);
        var to = Find(toHub);
        if (from is null || to is null || from.Latitude == 0 || to.Latitude == 0)
        {
            return 10;
        }

        var hours = DistanceKm(from.Latitude, from.Longitude, to.Latitude, to.Longitude) * RoadFactor / TruckKmPerHour + HandlingHours;
        return Math.Max(1, (int)Math.Round(hours, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// True when a parcel is scanned at a hub that has no business handling it. A parcel legitimately passes through
    /// its origin hub (first mile, Iraqi origins only) and its destination hub (last mile); any other hub is a misroute.
    /// <paramref name="expectedHub"/> is the destination hub, where the parcel should go next.
    /// </summary>
    public bool IsCrossHubOrMisrouted(string? scannedHubCode, string? destinationCode, out HubInfo expectedHub, string? originCode = null)
    {
        expectedHub = ResolveDestinationHub(destinationCode);
        var currentHub = FindOrDefault(scannedHubCode);
        if (string.Equals(currentHub.Code, expectedHub.Code, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var originHub = OriginHubFor(originCode);
        return originHub is null || !string.Equals(currentHub.Code, originHub.Code, StringComparison.OrdinalIgnoreCase);
    }

    public bool TryParseActionBarcode(string? rawInput, out ActionBarcodeCommand command)
    {
        command = default;
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return false;
        }

        var text = rawInput.Trim().ToUpperInvariant();
        if (text.StartsWith("CMD:MODE:", StringComparison.Ordinal))
        {
            var mode = text["CMD:MODE:".Length..].Trim();
            if (mode is "RECEIVE" or "DISPATCH" or "VERIFY" or "LOAD")
            {
                command = new(ActionBarcodeKind.SwitchMode, mode);
                return true;
            }
        }
        else if (text.StartsWith("CMD:HUB:", StringComparison.Ordinal))
        {
            if (Find(text["CMD:HUB:".Length..]) is { } hub)
            {
                command = new(ActionBarcodeKind.SwitchHub, hub.Code);
                return true;
            }
        }
        else if (text.StartsWith("BIN:", StringComparison.Ordinal))
        {
            var bin = text["BIN:".Length..].Trim();
            if (bin.Length > 0)
            {
                command = new(ActionBarcodeKind.SetBin, bin);
                return true;
            }
        }

        return false;
    }

    /// <summary>Governorate of a place: the code itself for governorates, the host governorate for Iraqi ports; null abroad.</summary>
    public static string? GovernorateOf(string? locationCode)
    {
        var code = locationCode?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        if (IraqGovernorates.IsKnown(code))
        {
            return code.ToUpperInvariant();
        }

        return PortGovernorates.GetValueOrDefault(code);
    }

    /// <summary>Great-circle distance in kilometres.</summary>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double EarthKm = 6371;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return EarthKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}

/// <summary>A hub as routing sees it: code, routing tag printed on labels, names, position and the governorates it serves.</summary>
public sealed record HubInfo(string Code, string RoutingTag, string Governorate, string ArabicName, string EnglishName)
{
    public HubKind Kind { get; init; } = HubKind.SortingHub;

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    /// <summary>Governorates whose customers this hub delivers to and whose sellers it collects from.</summary>
    public IReadOnlyList<string> Covers { get; init; } = [];

    [JsonIgnore]
    public string Name => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? ArabicName : EnglishName;

    [JsonIgnore]
    public string DisplayLabel => $"{RoutingTag} · {Name}";
}

/// <summary>What a warehouse is for. All kinds route parcels the same way; the kind is shown to staff and on the map.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HubKind>))]
public enum HubKind
{
    /// <summary>Receives trucks, sorts and dispatches: مركز فرز.</summary>
    SortingHub,

    /// <summary>Local branch for pickups, drop-offs and last-mile couriers: فرع.</summary>
    Branch,

    /// <summary>Keeps sellers' stock for fulfilment: مستودع.</summary>
    Fulfilment,
}

public sealed record ActionBarcodeSpec(string Code, string ArabicLabel, string EnglishLabel)
{
    public string Label => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? ArabicLabel : EnglishLabel;
}

public enum ActionBarcodeKind
{
    SwitchMode,
    SwitchHub,
    SetBin,
}

public readonly record struct ActionBarcodeCommand(ActionBarcodeKind Kind, string Value);
