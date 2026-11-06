using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Geo;

namespace Logistico.Modules.Shipments.Contracts;

/// <summary>What a parcel at a hub is waiting for; it decides which shelves the parcel can go on.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ShelfPurpose>))]
public enum ShelfPurpose
{
    /// <summary>Any parcel.</summary>
    Any,

    /// <summary>At the hub that delivers it, waiting for a courier.</summary>
    Deliver,

    /// <summary>Waiting for a truck to another hub (returns on their way back included).</summary>
    Forward,

    /// <summary>A return back at the seller's hub, waiting to be handed over.</summary>
    Return,
}

/// <summary>
/// One line of a hub's sort plan: which parcels go on which shelves. Conditions left empty match every parcel; the most
/// specific rule that matches wins, and its shelves fill in order, so a full shelf overflows to the next one and a rule
/// whose shelves are all full overflows to the next matching rule.
/// </summary>
public sealed partial class ShelfRuleRequest : IValidatableObject
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(16, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string HubCode { get; set; } = "";

    public ShelfPurpose Purpose { get; set; } = ShelfPurpose.Any;

    /// <summary>For parcels waiting for a truck: the hub they travel to.</summary>
    [StringLength(16, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? ToHubCode { get; set; }

    /// <summary>The customer's governorate.</summary>
    [StringLength(5, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Governorate { get; set; }

    /// <summary>A district or area that appears in the order's area, such as "الكرادة" (spelling variants match).</summary>
    [StringLength(80, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Area { get; set; }

    /// <summary>One seller's parcels, for example a big shop with its own shelf.</summary>
    public Guid? MerchantId { get; set; }

    /// <summary>The shelves, filled in this order.</summary>
    public List<string> Shelves { get; set; } = [];

    /// <summary>How many parcels fit on each shelf; empty for no limit.</summary>
    [Range(1, 10_000, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int? CapacityPerShelf { get; set; }

    [StringLength(200, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Note { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var shelves = Shelves.Select(SortPlanRules.Shelf).ToList();
        if (shelves.Count is 0 or > SortPlanRules.MaxShelves
            || shelves.Any(s => s is null || !ShelfPattern().IsMatch(s))
            || shelves.Distinct(StringComparer.Ordinal).Count() != shelves.Count)
        {
            yield return new(ValidationText.Shelves, [nameof(Shelves)]);
        }

        if (!string.IsNullOrWhiteSpace(ToHubCode) && Purpose is not (ShelfPurpose.Forward or ShelfPurpose.Any))
        {
            yield return new(ValidationText.Choice, [nameof(ToHubCode)]);
        }

        if (!string.IsNullOrWhiteSpace(Governorate) && !PlaceCodes.IsValid(Governorate))
        {
            yield return new(ValidationText.Governorate, [nameof(Governorate)]);
        }
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9./-]{0,19}$")]
    private static partial Regex ShelfPattern();
}

/// <summary>A sort plan rule as the console shows it.</summary>
public sealed record ShelfRuleItem(
    Guid Id,
    string HubCode,
    ShelfPurpose Purpose,
    string? ToHubCode,
    string? Governorate,
    string? Area,
    Guid? MerchantId,
    string? MerchantName,
    IReadOnlyList<string> Shelves,
    int? CapacityPerShelf,
    string? Note,
    int Specificity,
    DateTimeOffset UpdatedAt,
    string? UpdatedBy);

/// <summary>A shelf at a hub and what is on it now.</summary>
public sealed record ShelfLoad(string Shelf, int Parcels, int? Capacity, Guid? RuleId)
{
    public bool IsFull => Capacity is { } c && Parcels >= c;
}

/// <summary>
/// A hub's sort plan: its rules, most specific first (the order they are tried in), and every shelf in use or in the
/// plan in natural order (A-2 before A-10), with what is on it.
/// </summary>
public sealed record SortPlanView(string HubCode, IReadOnlyList<ShelfRuleItem> Rules, IReadOnlyList<ShelfLoad> Shelves, int WithoutShelf);

/// <summary>Where the sort plan would put a parcel scanned in at the hub now.</summary>
public sealed record ShelfSuggestion(
    string TrackingNumber,
    ShelfPurpose Purpose,
    string? Shelf,
    Guid? RuleId,
    bool Full,
    bool AlreadyThere);

public static class SortPlanRules
{
    /// <summary>The most shelves one rule may list.</summary>
    public const int MaxShelves = 12;

    /// <summary>The most rules a hub may have.</summary>
    public const int MaxRulesPerHub = 200;

    /// <summary>A shelf name as stored: trimmed and upper case.</summary>
    public static string? Shelf(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    /// <summary>
    /// How specific a rule is; the most specific matching rule is tried first. A seller beats an area, an area beats a
    /// governorate or a destination hub, and any condition beats a rule for every parcel.
    /// </summary>
    public static int Specificity(ShelfPurpose purpose, string? toHub, string? governorate, string? area, Guid? merchantId) =>
        (merchantId is null ? 0 : 16)
        + (string.IsNullOrWhiteSpace(area) ? 0 : 8)
        + (string.IsNullOrWhiteSpace(governorate) ? 0 : 4)
        + (string.IsNullOrWhiteSpace(toHub) ? 0 : 4)
        + (purpose == ShelfPurpose.Any ? 0 : 1);
}
