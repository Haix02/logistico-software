using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Contracts;

/// <summary>Result of a confirmation call made by staff.</summary>
public sealed class RecordConfirmationRequest
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public ConfirmationOutcome? Outcome { get; set; }

    [StringLength(300, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Note { get; set; }
}

/// <summary>The customer confirms or declines on the tracking page, proving it is them with the phone's last 4 digits.</summary>
public sealed partial class CustomerConfirmation : IValidatableObject
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public string PhoneLast4 { get; set; } = "";

    public bool Confirm { get; set; } = true;

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

/// <summary>An order waiting for the customer's confirmation, with what we know about the customer.</summary>
public sealed record ConfirmationQueueItem(
    Guid Id,
    string TrackingNumber,
    string? MerchantName,
    string ConsigneeName,
    string? ConsigneePhone,
    string DestinationCode,
    string? DestinationCity,
    string? ItemsDescription,
    decimal? CodAmount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RequestedAt,
    int Attempts,
    CustomerRisk Risk,
    int PreviousDelivered,
    int PreviousReturned,
    uint Version);

public sealed record ConfirmationQueue(
    IReadOnlyList<ConfirmationQueueItem> Items,
    int Total,
    int Overdue,
    int ConfirmedToday,
    int DeclinedToday,
    int AutoCancelledToday);
