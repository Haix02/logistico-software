using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Iraq;

namespace Logistico.Modules.Shipments.Contracts;

/// <summary>
/// Booking request for both international freight and domestic deliveries (e.g. an Instagram shop's order).
/// The same validation runs in the browser form and on the server, in the user's language.
/// </summary>
public sealed partial class CreateShipmentRequest : IValidatableObject
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public ServiceType? ServiceType { get; set; } = Contracts.ServiceType.Domestic;

    public TransportMode? Mode { get; set; } = TransportMode.Road;

    /// <summary>The seller (Instagram shop) this order belongs to. Optional for international freight.</summary>
    public Guid? MerchantId { get; set; }

    /// <summary>The seller's own order reference, e.g. the Instagram DM or order number.</summary>
    [StringLength(60, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? ExternalReference { get; set; }

    [StringLength(200, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string ShipperName { get; set; } = "";

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(200, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string ConsigneeName { get; set; } = "";

    [StringLength(24, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? ConsigneePhone { get; set; }

    /// <summary>International: UN/LOCODE or IATA code (CNSHA, TRIST). Domestic: governorate code (IQ-BG).</summary>
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public string OriginCode { get; set; } = "IQ-BG";

    /// <summary>International: UN/LOCODE or IATA code (IQUQR). Domestic: governorate code (IQ-BA).</summary>
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public string DestinationCode { get; set; } = "";

    [StringLength(80, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? DestinationCity { get; set; }

    [StringLength(300, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? DeliveryAddress { get; set; }

    /// <summary>Nearest landmark ("أقرب نقطة دالة"), the way Iraqi addresses are usually given.</summary>
    [StringLength(200, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Landmark { get; set; }

    [StringLength(500, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? ItemsDescription { get; set; }

    /// <summary>True: we collect from the seller. False: the seller drops the parcel at our warehouse.</summary>
    public bool PickupRequired { get; set; } = true;

    [StringLength(40, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? HouseBillNumber { get; set; }

    [RegularExpression("^(EXW|FCA|CPT|CIP|DAP|DPU|DDP|FAS|FOB|CFR|CIF)$", ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Incoterm))]
    public string? Incoterm { get; set; }

    [Range(1, 100_000, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int Pieces { get; set; } = 1;

    [Range(typeof(decimal), "0.001", "1000000", ParseLimitsInInvariantCulture = true, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal GrossWeightKg { get; set; } = 1m;

    [Range(typeof(decimal), "0", "10000", ParseLimitsInInvariantCulture = true, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal VolumeCbm { get; set; }

    [Range(typeof(decimal), "0", "100000000", ParseLimitsInInvariantCulture = true, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal? DeclaredValue { get; set; }

    [RegularExpression("^[A-Z]{3}$", ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Currency))]
    public string? DeclaredCurrency { get; set; }

    /// <summary>Amount the courier collects from the customer.</summary>
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal? CodAmount { get; set; }

    [RegularExpression("^IQD$", ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Currency))]
    public string? CodCurrency { get; set; } = "IQD";

    /// <summary>Delivery fee charged to the seller (IQD).</summary>
    [Range(typeof(decimal), "0", "10000000", ParseLimitsInInvariantCulture = true, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal? DeliveryFee { get; set; }

    public DateTimeOffset? PromisedAt { get; set; }

    [StringLength(2000, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ServiceType == Contracts.ServiceType.Domestic)
        {
            if (!IraqGovernorates.IsKnown(DestinationCode))
            {
                yield return new(ValidationText.Governorate, [nameof(DestinationCode)]);
            }

            if (!IraqGovernorates.IsKnown(OriginCode))
            {
                yield return new(ValidationText.Governorate, [nameof(OriginCode)]);
            }

            if (!IraqPhone.IsValid(ConsigneePhone))
            {
                yield return new(ValidationText.IraqiMobile, [nameof(ConsigneePhone)]);
            }

            if (string.IsNullOrWhiteSpace(DeliveryAddress))
            {
                yield return new(ValidationText.Required, [nameof(DeliveryAddress)]);
            }
        }
        else
        {
            if (!LocationCodePattern().IsMatch(OriginCode ?? ""))
            {
                yield return new(ValidationText.LocationCode, [nameof(OriginCode)]);
            }

            if (!LocationCodePattern().IsMatch(DestinationCode ?? ""))
            {
                yield return new(ValidationText.LocationCode, [nameof(DestinationCode)]);
            }

            if (string.IsNullOrWhiteSpace(ShipperName))
            {
                yield return new(ValidationText.Required, [nameof(ShipperName)]);
            }

            if (Mode is null)
            {
                yield return new(ValidationText.Required, [nameof(Mode)]);
            }

            if (!string.IsNullOrWhiteSpace(ConsigneePhone) && !LoosePhonePattern().IsMatch(ConsigneePhone))
            {
                yield return new(ValidationText.Phone, [nameof(ConsigneePhone)]);
            }
        }
    }

    [GeneratedRegex("^[A-Za-z]{3,5}$")]
    private static partial Regex LocationCodePattern();

    [GeneratedRegex(@"^\+?[0-9 ]{7,20}$")]
    private static partial Regex LoosePhonePattern();
}

/// <summary>Records a milestone that moves the shipment to <see cref="Status"/>. Send the shipment's ETag in If-Match.</summary>
public sealed class RecordShipmentEventRequest : IValidatableObject
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public ShipmentStatus? Status { get; set; }

    /// <summary>When it happened in the real world. Defaults to now.</summary>
    public DateTimeOffset? OccurredAt { get; set; }

    [StringLength(100, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Location { get; set; }

    /// <summary>Shelf/bin code when the parcel is put away in a warehouse, e.g. A-03-2.</summary>
    [StringLength(20, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? WarehouseBin { get; set; }

    /// <summary>Courier taking the parcel out for delivery.</summary>
    [StringLength(100, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? CourierName { get; set; }

    public DeliveryFailureReason? FailureReason { get; set; }

    [StringLength(1000, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Status == ShipmentStatus.DeliveryFailed && FailureReason is null)
        {
            yield return new(ShipmentTexts.Problem("shipment.failure_reason_required"), [nameof(FailureReason)]);
        }
    }
}

/// <summary>Books a batch of domestic delivery orders for a seller in a single operation.</summary>
public sealed class BulkBookOrdersRequest : IValidatableObject
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public Guid? MerchantId { get; set; }

    public bool PickupRequired { get; set; } = true;

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [MinLength(1, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [MaxLength(200, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public List<ParsedOrderDraft>? Orders { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Orders is null)
        {
            yield break;
        }

        for (var i = 0; i < Orders.Count; i++)
        {
            var o = Orders[i];
            if (o is null)
            {
                yield return new(ValidationText.Required, [$"{nameof(Orders)}[{i}]"]);
                continue;
            }

            var nested = new List<ValidationResult>();
            Validator.TryValidateObject(o, new ValidationContext(o), nested, validateAllProperties: true);
            foreach (var problem in nested)
            {
                var members = problem.MemberNames.Select(member => $"{nameof(Orders)}[{i}].{member}").ToArray();
                yield return new(problem.ErrorMessage, members);
            }

            if (string.IsNullOrWhiteSpace(o.ConsigneeName))
            {
                yield return new(ValidationText.Required, [$"{nameof(Orders)}[{i}].{nameof(o.ConsigneeName)}"]);
            }

            if (!IraqPhone.IsValid(o.ConsigneePhone))
            {
                yield return new(ValidationText.IraqiMobile, [$"{nameof(Orders)}[{i}].{nameof(o.ConsigneePhone)}"]);
            }

            if (!IraqGovernorates.IsKnown(o.DestinationCode))
            {
                yield return new(ValidationText.Governorate, [$"{nameof(Orders)}[{i}].{nameof(o.DestinationCode)}"]);
            }
        }
    }
}
