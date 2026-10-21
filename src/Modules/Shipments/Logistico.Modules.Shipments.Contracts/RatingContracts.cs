using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Contracts;

/// <summary>The customer rates a delivery on the tracking page (the last 4 digits of their mobile prove it is them).</summary>
public sealed partial class CustomerRating : IValidatableObject
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public string PhoneLast4 { get; set; } = "";

    [Range(1, 5, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int Stars { get; set; } = 5;

    [StringLength(500, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Comment { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!FourDigits().IsMatch(PhoneLast4 ?? ""))
        {
            yield return new(ValidationText.PhoneLast4, [nameof(PhoneLast4)]);
        }
    }

    [GeneratedRegex("^[0-9]{4}$")]
    private static partial Regex FourDigits();
}

/// <summary>A low rating (2 stars or fewer) opens a complaint so customer care calls back.</summary>
public static class RatingRules
{
    public const int ComplaintAtOrBelow = 2;
}
