using System.ComponentModel.DataAnnotations;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Iraq;

namespace Logistico.Modules.Shipments.Contracts;

/// <summary>
/// Corrects a delivery order before it is out with a courier: customer, address, contents and amounts.
/// Every change is written to the order's history with who made it.
/// </summary>
public sealed class UpdateOrderRequest : IValidatableObject
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(200, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string ConsigneeName { get; set; } = "";

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public string ConsigneePhone { get; set; } = "";

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public string DestinationCode { get; set; } = "";

    [StringLength(100, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? DestinationCity { get; set; }

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(300, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string DeliveryAddress { get; set; } = "";

    [StringLength(200, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Landmark { get; set; }

    [StringLength(300, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? ItemsDescription { get; set; }

    [Range(1, 999, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int Pieces { get; set; } = 1;

    [Range(0.01, 1000, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal GrossWeightKg { get; set; } = 1;

    [Range(0, 100_000_000, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal? CodAmount { get; set; }

    [Range(0, 10_000_000, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal? DeliveryFee { get; set; }

    [StringLength(60, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? ExternalReference { get; set; }

    [StringLength(1000, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IraqPhone.IsValid(ConsigneePhone))
        {
            yield return new(ValidationText.IraqiMobile, [nameof(ConsigneePhone)]);
        }

        if (!IraqGovernorates.IsKnown(DestinationCode))
        {
            yield return new(ValidationText.Governorate, [nameof(DestinationCode)]);
        }
    }

    public static UpdateOrderRequest From(ShipmentDetails s) => new()
    {
        ConsigneeName = s.ConsigneeName,
        ConsigneePhone = IraqPhone.Display(s.ConsigneePhone),
        DestinationCode = s.DestinationCode,
        DestinationCity = s.DestinationCity,
        DeliveryAddress = s.DeliveryAddress ?? "",
        Landmark = s.Landmark,
        ItemsDescription = s.ItemsDescription,
        Pieces = s.Pieces,
        GrossWeightKg = s.GrossWeightKg,
        CodAmount = s.CodAmount,
        DeliveryFee = s.DeliveryFee,
        ExternalReference = s.ExternalReference,
        Notes = s.Notes,
    };
}

/// <summary>Staff cancel an order: before collection it is cancelled, afterwards it goes back to the seller.</summary>
public sealed class CancelOrderRequest
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(300, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string Reason { get; set; } = "";
}
