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
    IReadOnlyList<CourierStop> Stops);

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

/// <summary>Why a delivery did not happen.</summary>
public sealed class CourierFailureRequest
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [EnumDataType(typeof(DeliveryFailureReason), ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Choice))]
    public DeliveryFailureReason? Reason { get; set; }
}
