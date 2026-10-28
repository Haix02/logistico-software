using System.ComponentModel.DataAnnotations;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Contracts;

/// <summary>A courier's day on their phone: the parcels still to deliver, and what is done today.</summary>
public sealed record CourierRun(
    string CourierCode,
    string CourierName,
    int DeliveredToday,
    int FailedToday,
    decimal CashCollectedTodayIqd,
    IReadOnlyList<CourierStop> Stops,
    decimal CashToHandInIqd = 0,
    int ParcelsToHandIn = 0);

/// <summary>One parcel out for delivery with everything the courier needs at the door.</summary>
public sealed record CourierStop(
    Guid Id,
    string TrackingNumber,
    string ConsigneeName,
    string? ConsigneePhone,
    string DestinationCode,
    string? City,
    string? Address,
    string? Landmark,
    string? Items,
    decimal? CodAmount,
    string? CodCurrency,
    int Attempts,
    string? MerchantName,
    string? Notes);

/// <summary>Why a delivery did not happen, and where the courier was.</summary>
public sealed class CourierFailureRequest : IValidatableObject
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [EnumDataType(typeof(DeliveryFailureReason), ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Choice))]
    public DeliveryFailureReason? Reason { get; set; }

    public GeoPoint? Location { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => GeoPoint.Check(Location, nameof(Location));
}

/// <summary>
/// Proof of delivery from the courier app: who took the parcel (a name when it was not the customer), an optional
/// finger signature, and where the phone was. Every part is optional for apps that do not send it yet.
/// </summary>
public sealed class CourierDeliveryRequest : IValidatableObject
{
    /// <summary>Longest signature accepted (SVG path data, about a minute of drawing).</summary>
    public const int MaxSignatureLength = 20_000;

    [EnumDataType(typeof(DeliveryReceiver), ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Choice))]
    public DeliveryReceiver ReceivedBy { get; set; } = DeliveryReceiver.Customer;

    [StringLength(120, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? ReceiverName { get; set; }

    /// <summary>The signature as SVG path data in a 300 × 150 box: only "M" and "L" commands and numbers.</summary>
    [StringLength(MaxSignatureLength, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Signature { get; set; }

    public GeoPoint? Location { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ReceivedBy != DeliveryReceiver.Customer && string.IsNullOrWhiteSpace(ReceiverName))
        {
            yield return new(ValidationText.Required, [nameof(ReceiverName)]);
        }

        if (!string.IsNullOrEmpty(Signature) && !IsPathData(Signature))
        {
            yield return new(ValidationText.Format, [nameof(Signature)]);
        }

        foreach (var result in GeoPoint.Check(Location, nameof(Location)))
        {
            yield return result;
        }
    }

    /// <summary>Path data the office can draw safely: move/line commands, digits, dots, minus signs and spaces.</summary>
    public static bool IsPathData(string value) =>
        value.Length > 0 && value[0] == 'M' && value.All(c => c is 'M' or 'L' or ' ' or '.' or '-' || char.IsAsciiDigit(c));
}

/// <summary>A phone's position (WGS 84) and how sure it was, in metres.</summary>
public sealed class GeoPoint
{
    [Range(-90d, 90d, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public double Latitude { get; set; }

    [Range(-180d, 180d, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public double Longitude { get; set; }

    [Range(0d, 100_000d, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public double? AccuracyMeters { get; set; }

    /// <summary>A request's position, checked by its own rules (the validator does not look inside nested objects).</summary>
    public static IEnumerable<ValidationResult> Check(GeoPoint? point, string name)
    {
        if (point is null)
        {
            return [];
        }

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(point, new ValidationContext(point), results, validateAllProperties: true);
        return results.Select(r => new ValidationResult(r.ErrorMessage, r.MemberNames.Select(m => $"{name}.{m}").ToList()));
    }
}

/// <summary>What the courier app recorded at one attempt: delivered (with who took it) or failed (with the reason).</summary>
public sealed record DeliveryProofItem(
    Guid Id,
    bool Delivered,
    DeliveryFailureReason? FailureReason,
    DateTimeOffset RecordedAt,
    string CourierLabel,
    DeliveryReceiver? ReceivedBy,
    string? ReceiverName,
    string? Signature,
    double? Latitude,
    double? Longitude,
    double? AccuracyMeters);
