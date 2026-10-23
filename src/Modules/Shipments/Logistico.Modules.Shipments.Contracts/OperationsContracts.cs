using System.ComponentModel.DataAnnotations;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Iraq;

namespace Logistico.Modules.Shipments.Contracts;

public sealed record CustomsQueueItem(
    Guid ShipmentId,
    string TrackingNumber,
    ShipmentStatus ShipmentStatus,
    TransportMode Mode,
    string OriginCode,
    string DestinationCode,
    string ShipperName,
    string ConsigneeName,
    int Pieces,
    decimal GrossWeightKg,
    decimal? DeclaredValue,
    string? DeclaredCurrency,
    string CustomsOfficeCode,
    string Regime,
    string? DeclarationNumber,
    string? AssessmentNumber,
    string? ReceiptNumber,
    string? ReleaseOrderNumber,
    CustomsLane SelectivityLane,
    string? HsCode,
    string? GoodsDescription,
    string CountryOfOrigin,
    string? CooNumber,
    bool CooEVerified,
    string? CommercialInvoiceNumber,
    bool InvoiceEVerified,
    string? CocNumber,
    decimal CustomsValueUsd,
    decimal DutyRatePercent,
    decimal AssessedDutyIqd,
    decimal TaxAndFeesIqd,
    string? HoldReason,
    string? BrokerName,
    DateTimeOffset? RegisteredAt,
    DateTimeOffset? ReleasedAt,
    DateTimeOffset UpdatedAt,
    uint? Version = null); // the saved declaration's version (null before one exists): send it as If-Match when saving

public sealed record CustomsSummary(
    int AwaitingDeclaration,
    int InClearance,
    int OnHold,
    int ReleasedCount,
    decimal TotalAssessedDutiesIqd,
    IReadOnlyList<CustomsQueueItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed class UpsertCustomsDeclarationRequest : IValidatableObject
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(10, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string CustomsOfficeCode { get; set; } = "IQUQR";

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(10, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string Regime { get; set; } = "IM4";

    [StringLength(40, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? DeclarationNumber { get; set; }

    [StringLength(40, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? AssessmentNumber { get; set; }

    [StringLength(40, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? ReceiptNumber { get; set; }

    [StringLength(40, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? ReleaseOrderNumber { get; set; }

    public CustomsLane SelectivityLane { get; set; } = CustomsLane.Unassigned;

    [StringLength(16, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? HsCode { get; set; }

    [StringLength(500, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? GoodsDescription { get; set; }

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(2, MinimumLength = 2, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string CountryOfOrigin { get; set; } = "CN";

    [StringLength(60, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? CooNumber { get; set; }

    public bool CooEVerified { get; set; }

    [StringLength(60, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? CommercialInvoiceNumber { get; set; }

    public bool InvoiceEVerified { get; set; }

    [StringLength(60, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? CocNumber { get; set; }

    [Range(typeof(decimal), "0", "100000000", ParseLimitsInInvariantCulture = true, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal CustomsValueUsd { get; set; }

    [Range(typeof(decimal), "0", "200", ParseLimitsInInvariantCulture = true, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal DutyRatePercent { get; set; } = 5m;

    [Range(typeof(decimal), "0", "100000000000", ParseLimitsInInvariantCulture = true, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal? AssessedDutyIqd { get; set; }

    [Range(typeof(decimal), "0", "100000000000", ParseLimitsInInvariantCulture = true, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal? TaxAndFeesIqd { get; set; }

    [StringLength(500, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? HoldReason { get; set; }

    [StringLength(150, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? BrokerName { get; set; }

    public CustomsAction Action { get; set; } = CustomsAction.Save;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Action == CustomsAction.Hold && string.IsNullOrWhiteSpace(HoldReason))
        {
            yield return new(ValidationText.Required, [nameof(HoldReason)]);
        }

        if (Action == CustomsAction.Release && string.IsNullOrWhiteSpace(DeclarationNumber))
        {
            yield return new(ValidationText.Required, [nameof(DeclarationNumber)]);
        }
    }
}

public sealed record CourierListItem(
    Guid Id,
    string Code,
    string Name,
    string Phone,
    string Governorate,
    VehicleType VehicleType,
    string? VehiclePlate,
    bool IsActive,
    string? Notes,
    int OutForDeliveryCount,
    int DeliveredTodayCount,
    int TotalDeliveredCount,
    int FailedCount,
    decimal CashOnRunIqd,
    uint Version,
    decimal CashToHandInIqd = 0,
    int ParcelsToHandIn = 0)
{
    public string DisplayLabel => $"{Name} ({Code})";
}

public sealed record CourierLookupItem(
    Guid Id,
    string Code,
    string Name,
    string Governorate,
    VehicleType VehicleType)
{
    public string DisplayLabel => $"{Name} ({Code})";
}

public sealed record FleetOverviewResult(
    int ActiveCouriers,
    int OutForDelivery,
    int DeliveredToday,
    int FailedAttempts,
    decimal CashWithCouriersIqd,
    IReadOnlyList<CourierListItem> Couriers);

public sealed class CourierRequest : IValidatableObject
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(24, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string Code { get; set; } = "";

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(120, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string Name { get; set; } = "";

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public string Phone { get; set; } = "";

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public string Governorate { get; set; } = "IQ-BG";

    public VehicleType VehicleType { get; set; } = VehicleType.Motorcycle;

    [StringLength(30, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? VehiclePlate { get; set; }

    [StringLength(500, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IraqPhone.IsValid(Phone))
        {
            yield return new(ValidationText.IraqiMobile, [nameof(Phone)]);
        }

        if (!IraqGovernorates.IsKnown(Governorate))
        {
            yield return new(ValidationText.Governorate, [nameof(Governorate)]);
        }
    }
}

public sealed record SellerCodBalance(
    Guid MerchantId,
    string MerchantName,
    int DeliveredUnsettledCount,
    decimal GrossCodIqd,
    decimal DeliveryFeesIqd,
    decimal NetPayableIqd,
    int InProgressCount,
    decimal InProgressCodIqd,
    int WithCouriersCount = 0,
    decimal WithCouriersIqd = 0);

public sealed record CodSettlementItem(
    Guid Id,
    string SettlementNumber,
    Guid MerchantId,
    string MerchantName,
    string Currency,
    int ShipmentCount,
    decimal GrossCodAmount,
    decimal DeliveryFeesAmount,
    decimal NetPayoutAmount,
    PayoutMethod PayoutMethod,
    string? PayoutReference,
    string? Notes,
    DateTimeOffset SettledAt,
    string? SettledBy);

public sealed record FinanceSummary(
    decimal PendingCodToCollectIqd,
    decimal UnsettledCodCollectedIqd,
    decimal UnsettledDeliveryFeesIqd,
    decimal UnsettledNetPayableIqd,
    decimal TotalSettledNetPaidIqd,
    decimal TotalDeliveryRevenueIqd,
    IReadOnlyList<SellerCodBalance> SellerBalances,
    IReadOnlyList<CodSettlementItem> RecentSettlements,
    decimal CodWithCouriersIqd = 0);

public sealed class CreateCodSettlementRequest
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public Guid? MerchantId { get; set; }

    public PayoutMethod PayoutMethod { get; set; } = PayoutMethod.ZainCash;

    [StringLength(100, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? PayoutReference { get; set; }

    [StringLength(500, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Notes { get; set; }
}
