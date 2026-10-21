using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Iraq;

namespace Logistico.Modules.Shipments.Contracts;

/// <summary>Create or edit a warehouse. The routing tag is chosen once (it is printed on labels) and cannot change.</summary>
public sealed partial class WarehouseRequest : IValidatableObject
{
    // Iraq's bounding box with a small margin.
    public const double MinLatitude = 29.0;
    public const double MaxLatitude = 37.5;
    public const double MinLongitude = 38.7;
    public const double MaxLongitude = 48.8;

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public string RoutingTag { get; set; } = "";

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(120, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string NameAr { get; set; } = "";

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(120, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string NameEn { get; set; } = "";

    public HubKind Kind { get; set; } = HubKind.Branch;

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public string Governorate { get; set; } = "";

    [StringLength(300, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Address { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public string? Phone { get; set; }

    [StringLength(120, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? ManagerName { get; set; }

    [StringLength(120, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? OpeningHours { get; set; }

    [Range(0, 1_000_000, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int? ShelfCapacity { get; set; }

    /// <summary>Governorates this warehouse delivers to and collects from.</summary>
    public List<string> Covers { get; set; } = [];

    /// <summary>Move governorates that another warehouse covers today to this one (the user confirmed the move).</summary>
    public bool TakeOverCoverage { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>The main warehouse: parcels with no better match go here.</summary>
    public bool IsPrimary { get; set; }

    [StringLength(500, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!TagPattern().IsMatch(RoutingTag ?? ""))
        {
            yield return new(ValidationText.RoutingTag, [nameof(RoutingTag)]);
        }

        if (!IraqGovernorates.IsKnown(Governorate))
        {
            yield return new(ValidationText.Governorate, [nameof(Governorate)]);
        }

        if (Latitude is < MinLatitude or > MaxLatitude || Longitude is < MinLongitude or > MaxLongitude)
        {
            yield return new(ValidationText.InsideIraq, [nameof(Latitude)]);
        }

        if (!string.IsNullOrWhiteSpace(Phone) && !IraqPhone.IsValid(Phone))
        {
            yield return new(ValidationText.IraqiMobile, [nameof(Phone)]);
        }

        if (Covers.Any(c => !IraqGovernorates.IsKnown(c)))
        {
            yield return new(ValidationText.Governorate, [nameof(Covers)]);
        }
    }

    public static WarehouseRequest From(WarehouseDetails w) => new()
    {
        RoutingTag = w.RoutingTag,
        NameAr = w.NameAr,
        NameEn = w.NameEn,
        Kind = w.Kind,
        Governorate = w.Governorate,
        Address = w.Address,
        Latitude = w.Latitude,
        Longitude = w.Longitude,
        Phone = w.Phone,
        ManagerName = w.ManagerName,
        OpeningHours = w.OpeningHours,
        ShelfCapacity = w.ShelfCapacity,
        Covers = [.. w.Covers],
        IsActive = w.IsActive,
        IsPrimary = w.IsPrimary,
        Notes = w.Notes,
    };

    [GeneratedRegex("^[A-Za-z]{3}$")]
    private static partial Regex TagPattern();
}

/// <summary>A warehouse with today's workload, for the warehouses screen.</summary>
public sealed record WarehouseDetails(
    Guid Id,
    string Code,
    string RoutingTag,
    string NameAr,
    string NameEn,
    HubKind Kind,
    string Governorate,
    string? Address,
    double Latitude,
    double Longitude,
    string? Phone,
    string? ManagerName,
    string? OpeningHours,
    int? ShelfCapacity,
    IReadOnlyList<string> Covers,
    bool IsActive,
    bool IsPrimary,
    string? Notes,
    DateTimeOffset UpdatedAt,
    string? UpdatedBy,
    WarehouseLoad Load,
    uint Version)
{
    public string Name => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? NameAr : NameEn;

    public string DisplayLabel => $"{RoutingTag} · {Name}";

    /// <summary>Shelf use in percent, when a capacity is set.</summary>
    public int? Utilisation => ShelfCapacity is > 0 ? (int)Math.Round(100.0 * Load.OnShelves / ShelfCapacity.Value) : null;
}

/// <summary>What a warehouse is handling right now.</summary>
public sealed record WarehouseLoad(
    int OnShelves,
    int ToDispatch,
    int ToTransfer,
    int ReturnsWaiting,
    int InboundParcels,
    int OpenTrucks,
    int ScansToday,
    int OpenAlerts,
    int DeliveredToday,
    int ActiveCouriers);

public sealed record WarehouseList(IReadOnlyList<WarehouseDetails> Items, IReadOnlyList<string> UncoveredGovernorates);

/// <summary>The company's active network as the browser needs it for routing and display.</summary>
public sealed record NetworkHubs(IReadOnlyList<HubInfo> Hubs, DateTimeOffset UpdatedAt);
