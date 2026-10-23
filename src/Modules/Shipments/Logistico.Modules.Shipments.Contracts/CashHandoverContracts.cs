using System.ComponentModel.DataAnnotations;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Contracts;

/// <summary>The cash a courier collected on delivery and has not handed in at the office yet, parcel by parcel.</summary>
public sealed record CourierCash(Guid CourierId, string CourierCode, string CourierName, decimal ExpectedIqd, IReadOnlyList<CourierCashParcel> Parcels);

public sealed record CourierCashParcel(Guid Id, string TrackingNumber, string ConsigneeName, string? MerchantName, decimal CodAmount, DateTimeOffset? DeliveredAt);

/// <summary>The office receives a courier's cash: the amount counted (any difference to the expected amount is recorded).</summary>
public sealed class ReceiveCashRequest
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [Range(typeof(decimal), "0", "10000000000", ParseLimitsInInvariantCulture = true, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal? ReceivedIqd { get; set; }

    [StringLength(500, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Notes { get; set; }
}

/// <summary>A recorded cash hand-in. A negative difference is a shortage.</summary>
public sealed record CashHandoverItem(
    Guid Id,
    string Number,
    Guid CourierId,
    string CourierName,
    int ParcelCount,
    decimal ExpectedIqd,
    decimal ReceivedIqd,
    decimal DifferenceIqd,
    DateTimeOffset ReceivedAt,
    string? ReceivedBy,
    string? Notes);
