using System.Globalization;
using System.Resources;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Application;

/// <summary>Writes the automation activity log and queues customer messages in the same unit of work.</summary>
internal sealed class Activity(ShipmentsDbContext db, TimeProvider clock)
{
    public void Log(Guid tenantId, AutomationRule rule, Shipment? shipment, string? detail = null) =>
        db.AutomationLog.Add(AutomationLogEntry.Create(tenantId, rule, shipment?.Id, shipment?.TrackingNumber, detail, clock.GetUtcNow()));

    public void Log(Guid tenantId, AutomationRule rule, Guid? shipmentId, string? trackingNumber, string? detail) =>
        db.AutomationLog.Add(AutomationLogEntry.Create(tenantId, rule, shipmentId, trackingNumber, detail, clock.GetUtcNow()));

    /// <summary>Queues a WhatsApp message to the customer when the company's rules allow it.</summary>
    public void Notify(Shipment shipment, NotificationTemplate template, AutomationPolicy policy, string? requestNumber = null)
    {
        if (!policy.NotifyCustomers || string.IsNullOrEmpty(shipment.ConsigneePhone) || shipment.ServiceType != ServiceType.Domestic)
        {
            return;
        }

        var body = CustomerMessages.Compose(shipment, template, requestNumber);
        db.Notifications.Add(CustomerNotification.Queue(shipment, template, body, clock.GetUtcNow()));
        Log(shipment.TenantId, AutomationRule.NotificationQueued, shipment, template.ToString());
    }

    /// <summary>Logs and notifies the follow-up chosen after a failed attempt.</summary>
    public void AfterFailure(Shipment shipment, FailureDecision decision, AutomationPolicy policy)
    {
        switch (decision.FollowUp)
        {
            case FailureFollowUp.Return:
                Log(shipment.TenantId, AutomationRule.AutoReturn, shipment, decision.ReturnReason?.ToString());
                Notify(shipment, NotificationTemplate.ReturnStarted, policy);
                break;
            case FailureFollowUp.Reschedule:
                Log(shipment.TenantId, AutomationRule.AutoReschedule, shipment, decision.NextAttempt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                Notify(shipment, NotificationTemplate.DeliveryFailed, policy);
                break;
            default:
                Notify(shipment, NotificationTemplate.DeliveryFailed, policy);
                break;
        }
    }
}

/// <summary>Customer message texts (Resources/NotificationTexts[.ar].resx), written in Arabic for Iraqi customers.</summary>
internal static class CustomerMessages
{
    private static readonly ResourceManager Texts = new("Logistico.Modules.Shipments.Resources.NotificationTexts", typeof(CustomerMessages).Assembly);
    private static readonly CultureInfo Arabic = AppCultures.Create(AppCultures.Arabic);

    public static string Compose(Shipment s, NotificationTemplate template, string? requestNumber = null)
    {
        var previous = CultureInfo.CurrentUICulture;
        var previousFormat = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = Arabic;
            CultureInfo.CurrentCulture = Arabic;
            var firstName = s.ConsigneeName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? s.ConsigneeName;
            var shop = s.MerchantName ?? s.ShipperName;
            var courier = CourierFirstName(s.CourierName) ?? Get("OurCourier");
            var cod = s.CodAmount is > 0 && s.CodCurrency is not null
                ? (s.CodCurrency == "IQD" ? $"{s.CodAmount.Value.ToString("N0", Arabic)} د.ع" : $"{s.CodAmount.Value.ToString("N2", Arabic)} {s.CodCurrency}")
                : Get("NoCod");
            var reason = s.LastFailureReason is { } r ? ShipmentTexts.Of(r) : "";
            var next = s.RescheduledTo is { } at ? TimeZoneInfo.ConvertTime(at, Baghdad).ToString("dddd d MMMM", Arabic) : "";
            return string.Format(Arabic, Get("Template_" + template), firstName, s.TrackingNumber, shop, courier, cod, reason, next, requestNumber ?? "");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
            CultureInfo.CurrentCulture = previousFormat;
        }
    }

    private static readonly TimeZoneInfo Baghdad = FindBaghdad();

    private static string Get(string key) => Texts.GetString(key, Arabic) ?? key;

    /// <summary>"علي (Baghdad-03)" → "علي".</summary>
    private static string? CourierFirstName(string? courier)
    {
        if (string.IsNullOrWhiteSpace(courier))
        {
            return null;
        }

        var name = courier.Split('(')[0].Trim();
        return name.Length == 0 ? null : name;
    }

    private static TimeZoneInfo FindBaghdad()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Baghdad");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Baghdad", TimeSpan.FromHours(3), "Baghdad", "Baghdad");
        }
    }
}
