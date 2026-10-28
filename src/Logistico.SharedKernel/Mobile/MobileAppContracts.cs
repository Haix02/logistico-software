using System.ComponentModel.DataAnnotations;
using Logistico.SharedKernel.Identity;

namespace Logistico.SharedKernel.Mobile;

public static class MobileApiRoutes
{
    /// <summary>What the signed-in account's app shows (any company account).</summary>
    public const string Config = "api/v1/app/config";

    /// <summary>The company's app settings (administrators).</summary>
    public const string Settings = "api/v1/app/settings";

    public const string Token = "api/v1/auth/token";
    public const string Refresh = "api/v1/auth/refresh";
}

/// <summary>The three experiences of the one mobile app, chosen by the account's role.</summary>
public static class AppKinds
{
    public const string Sender = "sender";
    public const string Courier = "courier";
    public const string Manager = "manager";

    public static string Of(string? role) => role switch
    {
        StaffRoles.Seller => Sender,
        StaffRoles.Courier => Courier,
        _ => Manager,
    };
}

/// <summary>
/// The parts of the app the company can switch on or off per kind of account (the app's "CMS"). A manager module is
/// offered only to roles whose policy allows its screens, so the app never shows what the API would refuse.
/// </summary>
public static class AppModules
{
    public const string SenderHome = "sender.home";
    public const string SenderOrders = "sender.orders";
    public const string SenderBook = "sender.book";
    public const string SenderPayouts = "sender.payouts";

    public const string CourierRun = "courier.run";
    public const string CourierScan = "courier.scan";
    public const string CourierCash = "courier.cash";

    public const string ManagerDashboard = "manager.dashboard";
    public const string ManagerAlerts = "manager.alerts";
    public const string ManagerOrders = "manager.orders";
    public const string ManagerHubScan = "manager.hub_scan";
    public const string ManagerShelfCheck = "manager.shelf_check";
    public const string ManagerFinance = "manager.finance";

    public static readonly IReadOnlyList<string> Sender = [SenderHome, SenderOrders, SenderBook, SenderPayouts];
    public static readonly IReadOnlyList<string> Courier = [CourierRun, CourierScan, CourierCash];
    public static readonly IReadOnlyList<string> Manager = [ManagerDashboard, ManagerAlerts, ManagerOrders, ManagerHubScan, ManagerShelfCheck, ManagerFinance];

    public static IReadOnlyList<string> Of(string kind) => kind switch
    {
        AppKinds.Sender => Sender,
        AppKinds.Courier => Courier,
        _ => Manager,
    };

    /// <summary>The policy whose roles may use a module's screens.</summary>
    public static string PolicyFor(string module) => module switch
    {
        ManagerHubScan or ManagerShelfCheck => StaffPolicies.Operate,
        ManagerFinance => StaffPolicies.Finance,
        _ when module.StartsWith("manager.", StringComparison.Ordinal) => StaffPolicies.Staff,
        _ when module.StartsWith("courier.", StringComparison.Ordinal) => StaffPolicies.Deliver,
        _ => StaffPolicies.Sell,
    };
}

/// <summary>A barcode scanner of an Android handheld that hands each code to apps as a broadcast intent.</summary>
public sealed record ScannerIntent(string Action, string Extra);

public static class ScannerIntents
{
    /// <summary>
    /// What common handhelds broadcast out of the box (or once their scanner app is pointed at <c>com.logistico.SCAN</c>);
    /// the app listens to all of them, plus the company's own pair from the settings.
    /// </summary>
    public static readonly IReadOnlyList<ScannerIntent> Known =
    [
        new("com.logistico.SCAN", "com.symbol.datawedge.data_string"), // Zebra DataWedge, intent output set to this action
        new("com.logistico.SCAN", "data"),                              // Honeywell and others set to this action
        new("android.intent.ACTION_DECODE_DATA", "barcode_string"),     // Urovo
        new("com.sunmi.scanner.ACTION_DATA_CODE_RECEIVED", "data"),     // Sunmi
        new("nlscan.action.SCANNER_RESULT", "SCAN_BARCODE1"),           // Newland
        new("android.intent.action.SCANRESULT", "value"),               // iData
        new("com.scanner.broadcast", "data"),                           // Chainway and generic
    ];
}

/// <summary>What the company's administrators set for the mobile app.</summary>
public sealed class MobileAppSettings : IValidatableObject
{
    public List<string> SenderModules { get; set; } = [.. AppModules.Sender];
    public List<string> CourierModules { get; set; } = [.. AppModules.Courier];
    public List<string> ManagerModules { get; set; } = [.. AppModules.Manager];

    /// <summary>Phones may scan with the camera (handhelds and scanner guns always work).</summary>
    public bool CameraScanning { get; set; } = true;

    /// <summary>Couriers must take a signature before a parcel counts as delivered.</summary>
    public bool RequireSignature { get; set; }

    [StringLength(100, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? ScannerAction { get; set; }

    [StringLength(100, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? ScannerExtra { get; set; }

    [StringLength(40, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? AppName { get; set; }

    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Format))]
    public string? PrimaryColor { get; set; }

    /// <summary>A message shown at the top of every app (a holiday, a new rule).</summary>
    [StringLength(300, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Announcement { get; set; }

    /// <summary>Older app versions are asked to update ("3.1.0").</summary>
    [RegularExpression(@"^\d{1,3}\.\d{1,3}\.\d{1,3}$", ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Format))]
    public string? MinimumVersion { get; set; }

    [StringLength(20, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? SupportPhone { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var (name, chosen, allowed) in new[]
        {
            (nameof(SenderModules), SenderModules, AppModules.Sender),
            (nameof(CourierModules), CourierModules, AppModules.Courier),
            (nameof(ManagerModules), ManagerModules, AppModules.Manager),
        })
        {
            if (chosen is null || chosen.Any(m => !allowed.Contains(m)))
            {
                yield return new(ValidationText.Choice, [name]);
            }
        }

        if (string.IsNullOrWhiteSpace(ScannerAction) != string.IsNullOrWhiteSpace(ScannerExtra))
        {
            yield return new(ValidationText.Required, [string.IsNullOrWhiteSpace(ScannerAction) ? nameof(ScannerAction) : nameof(ScannerExtra)]);
        }
    }
}

/// <summary>Everything the app needs at start-up for the signed-in account.</summary>
public sealed record MobileAppConfig(
    string Kind,
    IReadOnlyList<string> Modules,
    AppAccount Account,
    string AppName,
    string? PrimaryColor,
    bool CameraScanning,
    bool RequireSignature,
    IReadOnlyList<ScannerIntent> ScannerIntents,
    string? Announcement,
    string? MinimumVersion,
    string? SupportPhone);

public sealed record AppAccount(string Email, string Name, string Role, string? CourierCode, Guid? MerchantId);

/// <summary>The settings with their version (for If-Match) and who changed them last.</summary>
public sealed record MobileAppSettingsView(MobileAppSettings Settings, uint? Version, DateTimeOffset? UpdatedAt, string? UpdatedBy);

/// <summary>
/// The company's delivery rules from its app settings, for the modules that enforce them (the host implements it, so
/// modules do not reach into the settings store).
/// </summary>
public interface IDeliveryRules
{
    /// <summary>Couriers must take a signature before a parcel counts as delivered.</summary>
    Task<bool> SignatureRequiredAsync(CancellationToken ct);
}
