using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Application;

/// <summary>Customer, seller and staff requests: record them, apply simple ones automatically, resolve the rest.</summary>
internal sealed class RequestDesk(ShipmentsDbContext db, NumberGenerator numbers, AutomationPolicies policies, HubDirectory hubs, TransferPlanner planner, Activity activity, TimeProvider clock)
{
    public async Task<(ServiceRequest Request, bool Applied)> CreateAsync(Shipment shipment, RequestFields fields, RequestSource source, string? by, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (!RequestRules.IsAllowed(fields.Type, shipment.Status, shipment.DeliveredAt, now))
        {
            throw new DomainException("request.not_allowed", "This request is not possible at this stage of the delivery.", ShipmentTexts.Of(fields.Type), ShipmentTexts.Of(shipment.Status));
        }

        var duplicate = await db.Requests.IgnoreQueryFilters().AnyAsync(r => r.TenantId == shipment.TenantId && r.ShipmentId == shipment.Id
            && r.Type == fields.Type && (r.Status == RequestStatus.Open || r.Status == RequestStatus.InProgress), ct);
        if (duplicate)
        {
            throw new DomainException("request.duplicate", "There is already an open request of this kind for this order.");
        }

        var policy = await policies.GetAsync(shipment.TenantId, ct);
        var request = ServiceRequest.Create(shipment, await numbers.RequestNumberAsync(ct), fields, source, by, policy.RequestSlaHours, now);
        db.Requests.Add(request);

        var applied = false;
        if (source is RequestSource.Customer or RequestSource.Seller && policy.AutoApplyCustomerRequests && request.CanAutoApply(shipment))
        {
            try
            {
                request.ApplyTo(shipment, await hubs.NetworkAsync(shipment.TenantId, ct), new Actor(source == RequestSource.Customer ? "customer" : "seller", "system"), now);
                await planner.ReconcileAsync(shipment, policy.AutoPlanTransfers, ct);
                request.Resolve(null, Actor.System.Name, now);
                activity.Log(shipment.TenantId, AutomationRule.RequestApplied, shipment, $"{request.Number} {request.Type}");
                if (request.Type == RequestType.Reschedule)
                {
                    activity.Notify(shipment, NotificationTemplate.Rescheduled, policy);
                }

                applied = true;
            }
            catch (DomainException)
            {
                // Leave it open for a person to look at.
            }
        }

        if (!applied && source == RequestSource.Customer)
        {
            activity.Notify(shipment, NotificationTemplate.RequestReceived, policy, request.Number);
        }

        return (request, applied);
    }

    public async Task ResolveAsync(ServiceRequest request, ResolveServiceRequest resolution, string? by, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (resolution.Outcome == RequestStatus.Rejected)
        {
            request.Reject(resolution.Note, by, now);
        }
        else
        {
            if (resolution.Apply && request.Type != RequestType.Complaint)
            {
                var shipment = await db.Shipments.FirstAsync(s => s.Id == request.ShipmentId, ct);
                request.ApplyTo(shipment, await hubs.NetworkAsync(shipment.TenantId, ct), new Actor(by), now);
                await planner.ReconcileAsync(shipment, (await policies.GetAsync(shipment.TenantId, ct)).AutoPlanTransfers, ct);
                if (request.Type == RequestType.Reschedule)
                {
                    activity.Notify(shipment, NotificationTemplate.Rescheduled, await policies.GetAsync(shipment.TenantId, ct));
                }
            }

            request.Resolve(resolution.Note, by, now);
        }

        var key = OpsAlert.Subject(AlertKind.RequestOverdue, "request", request.Id);
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.SubjectKey == key && a.ResolvedAt == null, ct);
        alert?.Resolve(by, automatic: false, now);
    }
}
