using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Contracts;

/// <summary>Filters, sorting and paging for the shipment list. Bound from the query string on the server.</summary>
public sealed class ShipmentQuery
{
    public const int MaxPageSize = 200;

    /// <summary>Free text: tracking number, phone, customer, seller, shipper or order reference.</summary>
    public string? Q { get; set; }
    public ShipmentStatus[]? Status { get; set; }
    public ServiceType? Service { get; set; }
    public Guid? MerchantId { get; set; }
    public string? Governorate { get; set; }

    /// <summary>Only parcels physically in a warehouse (received, ready for dispatch).</summary>
    public bool? InWarehouse { get; set; }

    /// <summary>Parcels physically at this hub now (HUB-BGW…).</summary>
    public string? Hub { get; set; }

    /// <summary>Parcels whose destination hub is this hub.</summary>
    public string? DestinationHub { get; set; }

    /// <summary>Parcels sitting at a hub that is not where they must go next (they need a line-haul truck).</summary>
    public bool? NeedsTransfer { get; set; }

    /// <summary>Delivery promise passed and the parcel is still on its way.</summary>
    public bool? Late { get; set; }

    /// <summary>Orders in this confirmation state (e.g. Pending: waiting for the customer to confirm).</summary>
    public ConfirmationStatus? Confirmation { get; set; }

    /// <summary>Courier code or name (parcels assigned to that courier).</summary>
    public string? Courier { get; set; }

    /// <summary>createdAt | trackingNumber | status | consignee | merchant | cod | eta | destination | bin</summary>
    public string? Sort { get; set; }
    public bool? Desc { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    public int EffectivePage => Paging.Page(Page);
    public int EffectivePageSize => Math.Clamp(PageSize ?? 25, 1, MaxPageSize);

    public string ToQueryString()
    {
        var parts = new List<string>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{key}={Uri.EscapeDataString(value)}");
            }
        }

        Add("q", Q);
        foreach (var status in Status ?? [])
        {
            Add("status", status.ToString());
        }

        Add("service", Service?.ToString());
        Add("merchantId", MerchantId?.ToString());
        Add("governorate", Governorate);
        Add("inWarehouse", InWarehouse?.ToString(CultureInfo.InvariantCulture).ToLowerInvariant());
        Add("hub", Hub);
        Add("destinationHub", DestinationHub);
        Add("needsTransfer", NeedsTransfer?.ToString(CultureInfo.InvariantCulture).ToLowerInvariant());
        Add("late", Late?.ToString(CultureInfo.InvariantCulture).ToLowerInvariant());
        Add("confirmation", Confirmation?.ToString());
        Add("courier", Courier);
        Add("sort", Sort);
        Add("desc", Desc?.ToString(CultureInfo.InvariantCulture).ToLowerInvariant());
        Add("page", Page?.ToString(CultureInfo.InvariantCulture));
        Add("pageSize", PageSize?.ToString(CultureInfo.InvariantCulture));
        return parts.Count == 0 ? "" : "?" + string.Join('&', parts);
    }

    public ShipmentQuery Clone() => (ShipmentQuery)MemberwiseClone();
}

/// <summary>Exact shipment references used by printing and other bulk actions.</summary>
public sealed class ShipmentReferencesRequest : IValidatableObject
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [MinLength(1, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [MaxLength(ShipmentQuery.MaxPageSize, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public List<string?>? References { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (References is null)
        {
            yield break;
        }

        for (var index = 0; index < References.Count; index++)
        {
            var value = References[index]?.Trim();
            if (string.IsNullOrWhiteSpace(value)
                || (!Guid.TryParse(value, out _) && !TrackingNumber.IsValid(value)))
            {
                yield return new ValidationResult("Use a shipment ID or a valid tracking number.", [$"{nameof(References)}[{index}]"]);
            }
        }
    }
}
