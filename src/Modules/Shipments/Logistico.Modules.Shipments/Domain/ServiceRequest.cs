using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Iraq;

namespace Logistico.Modules.Shipments.Domain;

/// <summary>A customer, seller or staff request on an order: new date, new address or phone, cancel, return, complaint.</summary>
internal sealed class ServiceRequest
{
    private ServiceRequest()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Number { get; private set; } = "";
    public Guid ShipmentId { get; private set; }
    public string TrackingNumber { get; private set; } = "";
    public RequestType Type { get; private set; }
    public RequestStatus Status { get; private set; }
    public RequestSource Source { get; private set; }
    public string? Details { get; private set; }
    public DateTimeOffset? PreferredDate { get; private set; }
    public string? NewGovernorate { get; private set; }
    public string? NewCity { get; private set; }
    public string? NewAddress { get; private set; }
    public string? NewLandmark { get; private set; }
    public string? NewPhone { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset DueAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolvedBy { get; private set; }
    public string? Resolution { get; private set; }
    public string? CreatedBy { get; private set; }
    public uint Version { get; private set; }

    public bool IsOpen => Status is RequestStatus.Open or RequestStatus.InProgress;

    public static ServiceRequest Create(
        Shipment shipment,
        string number,
        RequestFields fields,
        RequestSource source,
        string? by,
        int slaHours,
        DateTimeOffset now) => new()
        {
            Id = Guid.CreateVersion7(now),
            TenantId = shipment.TenantId,
            Number = number,
            ShipmentId = shipment.Id,
            TrackingNumber = shipment.TrackingNumber,
            Type = fields.Type,
            Status = RequestStatus.Open,
            Source = source,
            Details = Clean(fields.Details),
            PreferredDate = fields.Type == RequestType.Reschedule ? fields.PreferredDate : null,
            NewGovernorate = Clean(fields.NewGovernorate)?.ToUpperInvariant(),
            NewCity = Clean(fields.NewCity),
            NewAddress = Clean(fields.NewAddress),
            NewLandmark = Clean(fields.NewLandmark),
            NewPhone = fields.Type == RequestType.ChangePhone ? IraqPhone.Normalize(fields.NewPhone) : null,
            CreatedAt = now,
            DueAt = now.AddHours(slaHours),
            CreatedBy = by,
        };

    /// <summary>Applies the request to the order (the order enforces what can still change).</summary>
    public void ApplyTo(Shipment shipment, HubNetwork network, Actor actor, DateTimeOffset now)
    {
        switch (Type)
        {
            case RequestType.Reschedule:
                shipment.Reschedule(PreferredDate!.Value, actor, now);
                break;
            case RequestType.ChangeAddress:
                shipment.ChangeAddress(NewGovernorate, NewCity, NewAddress!, NewLandmark, network, actor, now);
                break;
            case RequestType.ChangePhone:
                shipment.ChangePhone(NewPhone!, actor, now);
                break;
            case RequestType.Cancel:
                shipment.Cancel(Details ?? Number, actor, now);
                break;
            case RequestType.Return:
                shipment.StartReturn(ReturnReason.CustomerReturn, Details ?? Number, actor, now);
                break;
            case RequestType.Complaint:
                break;
        }
    }

    public void Start(string? by)
    {
        EnsureOpen();
        Status = RequestStatus.InProgress;
        ResolvedBy = by;
    }

    public void Resolve(string? note, string? by, DateTimeOffset now) => Close(RequestStatus.Resolved, note, by, now);

    public void Reject(string? note, string? by, DateTimeOffset now) => Close(RequestStatus.Rejected, note, by, now);

    /// <summary>
    /// Requests the automation engine may apply without a person: a new date, a new phone, an address in the same
    /// governorate, or cancelling an order that has not been collected yet.
    /// </summary>
    public bool CanAutoApply(Shipment shipment) => Type switch
    {
        RequestType.Reschedule or RequestType.ChangePhone => true,
        RequestType.ChangeAddress => NewGovernorate is null || NewGovernorate == shipment.DestinationCode,
        RequestType.Cancel => shipment.Status is ShipmentStatus.Booked or ShipmentStatus.Draft,
        _ => false,
    };

    private void Close(RequestStatus status, string? note, string? by, DateTimeOffset now)
    {
        EnsureOpen();
        Status = status;
        Resolution = Clean(note);
        ResolvedBy = by;
        ResolvedAt = now;
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new DomainException("request.closed", "This request is already closed.", Number);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
