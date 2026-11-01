using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Logistico.SharedKernel.Identity;
using Logistico.SharedKernel.Iraq;

namespace Logistico.SharedKernel.Setup;

public static class SetupApiRoutes
{
    /// <summary>The company's profile (any office role reads it; administrators change it).</summary>
    public const string Company = "api/v1/company";

    /// <summary>The getting-started guide for the signed-in person.</summary>
    public const string GettingStarted = "api/v1/getting-started";

    /// <summary>The installation's health checks (administrators).</summary>
    public const string SystemChecks = "api/v1/system/checks";

    public static string SkipStep(string step) => $"{GettingStarted}/steps/{Uri.EscapeDataString(step)}/skip";
}

/// <summary>The company as it presents itself: in the console, on the app, to customers.</summary>
public sealed class CompanyProfile : IValidatableObject
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(100, MinimumLength = 2, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string Name { get; set; } = "";

    [StringLength(20, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Phone { get; set; }

    /// <summary>Where the head office is.</summary>
    [StringLength(5, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Governorate { get; set; }

    [StringLength(300, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Address { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Phone) && IraqPhone.Normalize(Phone) is null)
        {
            yield return new(ValidationText.Phone, [nameof(Phone)]);
        }

        if (!string.IsNullOrWhiteSpace(Governorate) && !IraqGovernorates.IsKnown(Governorate))
        {
            yield return new(ValidationText.Governorate, [nameof(Governorate)]);
        }
    }

    public CompanyProfile Clone() => new() { Name = Name, Phone = Phone, Governorate = Governorate, Address = Address };
}

public sealed record CompanyProfileView(CompanyProfile Profile, bool IsSet, DateTimeOffset? UpdatedAt, string? UpdatedBy, uint Version);

/// <summary>
/// The getting-started guide: what a new company sets up, in order. A step is done when the company's data says so
/// (not when someone ticks it), so it stays true to what is really there.
/// </summary>
public static class GettingStartedSteps
{
    public const string Company = "company";
    public const string Warehouses = "warehouses";
    public const string Prices = "prices";
    public const string Team = "team";
    public const string Couriers = "couriers";
    public const string Sellers = "sellers";
    public const string SortPlan = "sort-plan";
    public const string FirstOrder = "first-order";
    public const string FirstScan = "first-scan";
    public const string Automation = "automation";
    public const string MobileApp = "mobile-app";
    public const string Health = "health";

    /// <summary>Each step, the page that does it and the roles that can do it (administrators can do every step).</summary>
    public static readonly IReadOnlyList<(string Id, string Href, string Icon, string[] Roles)> All =
    [
        (Company, "settings/company", "office", []),
        (Warehouses, "warehouses", "home", []),
        (Prices, "pricing", "tag", [StaffRoles.Finance]),
        (Team, "team", "people", []),
        (Couriers, "fleet", "drive-time", [StaffRoles.Operations]),
        (Sellers, "merchants", "shop", [StaffRoles.Operations, StaffRoles.CustomerSupport]),
        (SortPlan, "warehouse/sort-plan", "sort", [StaffRoles.Operations]),
        (FirstOrder, "orders/new", "add", [StaffRoles.Operations, StaffRoles.CustomerSupport]),
        (FirstScan, "warehouse", "barcode", [StaffRoles.Operations]),
        (Automation, "automation", "automatic-updates", []),
        (MobileApp, "mobile-app", "mobile-phone", []),
        (Health, "system", "pulse", []),
    ];

    public static bool IsKnown(string id) => All.Any(s => s.Id == id);

    public static bool For(string role, string[] roles) => role == StaffRoles.Admin || roles.Contains(role);
}

/// <summary>A module's answer to "which getting-started steps does this company's data complete?".</summary>
public interface IGettingStartedFacts
{
    Task<IReadOnlyCollection<string>> DoneAsync(CancellationToken cancellationToken);
}

public sealed record GettingStartedStep(string Id, string Href, string Icon, bool Done, bool Skipped);

public sealed record GettingStartedView(IReadOnlyList<GettingStartedStep> Steps)
{
    public int Done => Steps.Count(s => s.Done || s.Skipped);

    public bool Finished => Steps.Count > 0 && Done == Steps.Count;

    /// <summary>The first step still to do.</summary>
    public GettingStartedStep? Next => Steps.FirstOrDefault(s => !s.Done && !s.Skipped);
}

/// <summary>A module's own part of the installation health: migrations not applied yet.</summary>
public interface IDatabaseHealth
{
    string Name { get; }

    Task<IReadOnlyList<string>> PendingMigrationsAsync(CancellationToken cancellationToken);
}

[JsonConverter(typeof(JsonStringEnumConverter<SystemCheckState>))]
public enum SystemCheckState
{
    Ok,
    Info,
    Warning,
    Problem,
}

/// <summary>One installation health check; the console words it from <see cref="Id"/>, <see cref="State"/> and <see cref="Values"/>.</summary>
public sealed record SystemCheck(string Id, SystemCheckState State, IReadOnlyList<string> Values);

public sealed record SystemChecksView(IReadOnlyList<SystemCheck> Checks, DateTimeOffset CheckedAt)
{
    public bool HasProblems => Checks.Any(c => c.State == SystemCheckState.Problem);
}
