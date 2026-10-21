using System.ComponentModel.DataAnnotations;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Iraq;

namespace Logistico.Modules.Shipments.Contracts;

public sealed record ServiceRequestItem(
    Guid Id,
    string Number,
    Guid ShipmentId,
    string TrackingNumber,
    RequestType Type,
    RequestStatus Status,
    RequestSource Source,
    string? Details,
    DateTimeOffset? PreferredDate,
    string? NewGovernorate,
    string? NewCity,
    string? NewAddress,
    string? NewLandmark,
    string? NewPhone,
    DateTimeOffset CreatedAt,
    DateTimeOffset DueAt,
    DateTimeOffset? ResolvedAt,
    string? ResolvedBy,
    string? Resolution,
    string? CreatedBy,
    string ConsigneeName,
    string? MerchantName,
    ShipmentStatus ShipmentStatus,
    bool IsOverdue,
    uint Version);

public sealed record RequestTypeCount(RequestType Type, int Count);

public sealed record ServiceRequestList(
    IReadOnlyList<ServiceRequestItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int OpenCount,
    int OverdueCount,
    IReadOnlyList<RequestTypeCount> OpenByType);

public sealed class ServiceRequestQuery
{
    public RequestStatus[]? Status { get; set; }
    public RequestType? Type { get; set; }
    public RequestSource? Source { get; set; }
    public Guid? ShipmentId { get; set; }
    public bool? Overdue { get; set; }
    public string? Q { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    public int EffectivePage => Paging.Page(Page);
    public int EffectivePageSize => Math.Clamp(PageSize ?? 25, 1, 200);
}

/// <summary>Fields shared by staff and customer requests, with their validation.</summary>
public abstract class RequestFields : IValidatableObject
{
    public RequestType Type { get; set; } = RequestType.Reschedule;

    [StringLength(1000, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Details { get; set; }

    public DateTimeOffset? PreferredDate { get; set; }

    public string? NewGovernorate { get; set; }

    [StringLength(80, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? NewCity { get; set; }

    [StringLength(300, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? NewAddress { get; set; }

    [StringLength(200, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? NewLandmark { get; set; }

    public string? NewPhone { get; set; }

    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        switch (Type)
        {
            case RequestType.Reschedule:
                var clock = validationContext.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
                var today = DeliveryPromise.BaghdadDate(clock.GetUtcNow());
                if (PreferredDate is null || PreferredDate.Value.Date < today || PreferredDate.Value.Date > today.AddDays(14))
                {
                    yield return new(ValidationText.DateWindow, [nameof(PreferredDate)]);
                }
                else if (DeliveryPromise.IsFriday(PreferredDate.Value))
                {
                    yield return new(ValidationText.NoFriday, [nameof(PreferredDate)]);
                }

                break;
            case RequestType.ChangeAddress:
                if (string.IsNullOrWhiteSpace(NewAddress))
                {
                    yield return new(ValidationText.Required, [nameof(NewAddress)]);
                }

                if (NewGovernorate is not null && !IraqGovernorates.IsKnown(NewGovernorate))
                {
                    yield return new(ValidationText.Governorate, [nameof(NewGovernorate)]);
                }

                break;
            case RequestType.ChangePhone:
                if (!IraqPhone.IsValid(NewPhone))
                {
                    yield return new(ValidationText.IraqiMobile, [nameof(NewPhone)]);
                }

                break;
            case RequestType.Complaint:
                if (string.IsNullOrWhiteSpace(Details))
                {
                    yield return new(ValidationText.Required, [nameof(Details)]);
                }

                break;
        }
    }
}

/// <summary>A request recorded by staff (phone call, seller message, walk-in).</summary>
public sealed class CreateServiceRequest : RequestFields
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public Guid? ShipmentId { get; set; }

    public RequestSource Source { get; set; } = RequestSource.Staff;
}

/// <summary>A request sent by the customer from the public tracking page. The last four digits of the phone number prove it is them.</summary>
public sealed class CustomerRequest : RequestFields
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [RegularExpression("^[0-9]{4}$", ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.PhoneLast4))]
    public string PhoneLast4 { get; set; } = "";
}

public sealed record CustomerRequestReceipt(string Number, RequestType Type, RequestStatus Status, bool AppliedAutomatically, DateTimeOffset CreatedAt);

public sealed class ResolveServiceRequest
{
    /// <summary>Resolved (apply the change) or Rejected.</summary>
    public RequestStatus Outcome { get; set; } = RequestStatus.Resolved;

    /// <summary>When resolving, also apply the change to the order (new date, address, return…).</summary>
    public bool Apply { get; set; } = true;

    [StringLength(500, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Note { get; set; }
}
