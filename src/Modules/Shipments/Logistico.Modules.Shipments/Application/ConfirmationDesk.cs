using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Application;

/// <summary>
/// Order confirmation before pickup: cash-on-delivery orders to customers whose parcels came back before (or all
/// orders, as the company chooses) wait until the customer confirms — by WhatsApp, on the tracking page or by phone.
/// </summary>
internal sealed class ConfirmationDesk(ShipmentsDbContext db, AutomationPolicies policies, Activity activity, TimeProvider clock)
{
    /// <summary>Return risk per mobile number from the company's delivery history (one grouped query).</summary>
    public async Task<Dictionary<string, CustomerRisk>> RisksAsync(Guid tenantId, IEnumerable<string> phones, CancellationToken ct)
    {
        var list = phones.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        var returning = new[] { ShipmentStatus.ReturnInitiated, ShipmentStatus.ReturnInTransit, ShipmentStatus.ReturnAtHub, ShipmentStatus.Returned };
        var rows = await db.Shipments.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.ServiceType == ServiceType.Domestic && s.ConsigneePhone != null && list.Contains(s.ConsigneePhone))
            .GroupBy(s => s.ConsigneePhone!)
            .Select(g => new
            {
                Phone = g.Key,
                Delivered = g.Count(s => s.Status == ShipmentStatus.Delivered),
                Returned = g.Count(s => returning.Contains(s.Status)),
                Refused = g.Count(s => s.ReturnReason == ReturnReason.CustomerRefused),
                Attempts = g.Sum(s => s.DeliveryAttempts),
            })
            .ToListAsync(ct);

        return list.ToDictionary(phone => phone, phone => rows.FirstOrDefault(r => r.Phone == phone) is { } r
            ? CustomerRiskRules.Assess(r.Delivered, r.Returned, r.Refused, Math.Max(0, r.Attempts - r.Delivered))
            : CustomerRisk.New);
    }

    /// <summary>After booking: hold the orders that need the customer's confirmation and send them the request.</summary>
    public async Task ApplyPolicyAsync(IReadOnlyList<Shipment> booked, CancellationToken ct)
    {
        var domestic = booked.Where(s => s.ServiceType == ServiceType.Domestic && s.ConsigneePhone is not null).ToList();
        if (domestic.Count == 0)
        {
            return;
        }

        var policy = await policies.GetAsync(domestic[0].TenantId, ct);
        if (policy.OrderConfirmation == OrderConfirmationMode.Off)
        {
            return;
        }

        var risks = policy.OrderConfirmation == OrderConfirmationMode.AllOrders
            ? []
            : await RisksAsync(domestic[0].TenantId, domestic.Select(s => s.ConsigneePhone!), ct);
        var now = clock.GetUtcNow();
        foreach (var shipment in domestic)
        {
            var risk = risks.GetValueOrDefault(shipment.ConsigneePhone!, CustomerRisk.New);
            if (!policy.NeedsConfirmation(risk))
            {
                continue;
            }

            shipment.RequireConfirmation(now);
            activity.Log(shipment.TenantId, AutomationRule.ConfirmationRequested, shipment, risk.ToString());
            activity.Notify(shipment, NotificationTemplate.ConfirmOrder, policy);
        }
    }

    /// <summary>Records a call result or the customer's own answer; a declined order is cancelled.</summary>
    public void Record(Shipment shipment, ConfirmationOutcome outcome, string? note, Actor actor) =>
        shipment.RecordConfirmation(outcome, note, actor, clock.GetUtcNow());
}
