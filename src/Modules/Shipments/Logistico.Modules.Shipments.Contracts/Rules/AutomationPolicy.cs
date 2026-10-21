using System.ComponentModel.DataAnnotations;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Contracts.Rules;

/// <summary>
/// The automation rules a company switches on and tunes (Automation → Rules). The same object is validated in the
/// browser and enforced on the server; defaults reflect common practice for Iraqi e-commerce delivery.
/// </summary>
public sealed class AutomationPolicy
{
    public const int DefaultMaxAttempts = 3;

    /// <summary>After this many failed delivery attempts the parcel goes back to the seller automatically.</summary>
    [Range(1, 6, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int MaxDeliveryAttempts { get; set; } = DefaultMaxAttempts;

    /// <summary>A customer who refuses the parcel starts a return immediately.</summary>
    public bool ReturnOnRefusal { get; set; } = true;

    /// <summary>A failed attempt is rescheduled for the next working day (Fridays skipped).</summary>
    public bool AutoReschedule { get; set; } = true;

    /// <summary>Parcels received at a hub that is not their destination are planned onto the next truck automatically.</summary>
    public bool AutoPlanTransfers { get; set; } = true;

    /// <summary>Simple customer requests (new date, phone, address in the same governorate) are applied without staff.</summary>
    public bool AutoApplyCustomerRequests { get; set; } = true;

    /// <summary>Queue a WhatsApp/SMS message to the customer at key moments.</summary>
    public bool NotifyCustomers { get; set; } = true;

    [Range(1, 168, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int PickupSlaHours { get; set; } = 24;

    [Range(1, 336, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int WarehouseDwellHours { get; set; } = 48;

    [Range(1, 72, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int OutForDeliveryHours { get; set; } = 12;

    /// <summary>Extra hours allowed on top of the normal road time before a truck counts as late.</summary>
    [Range(1, 72, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int LineHaulGraceHours { get; set; } = 6;

    [Range(1, 30, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int ReturnCollectionDays { get; set; } = 5;

    [Range(1, 168, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int RequestSlaHours { get; set; } = 24;

    /// <summary>Which new orders wait for the customer to confirm before pickup (cuts refusals and returns).</summary>
    public OrderConfirmationMode OrderConfirmation { get; set; } = OrderConfirmationMode.RiskyCustomers;

    /// <summary>Hours a confirmation may stay open before staff are alerted to call.</summary>
    [Range(1, 72, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int ConfirmationHours { get; set; } = 12;

    /// <summary>Cancel orders still unconfirmed after this many hours (0 = never).</summary>
    [Range(0, 336, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public int AutoCancelUnconfirmedHours { get; set; }

    /// <summary>Whether an order to a customer with this history waits for confirmation.</summary>
    public bool NeedsConfirmation(CustomerRisk risk) => OrderConfirmation switch
    {
        OrderConfirmationMode.AllOrders => true,
        OrderConfirmationMode.RiskyCustomers => risk is CustomerRisk.Medium or CustomerRisk.High,
        _ => false,
    };

    public uint Version { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public AutomationPolicy Clone() => (AutomationPolicy)MemberwiseClone();
}

/// <summary>What happens automatically after a failed delivery attempt.</summary>
public enum FailureFollowUp
{
    None,
    Reschedule,
    Return,
}

public sealed record FailureDecision(FailureFollowUp FollowUp, ReturnReason? ReturnReason = null, DateTimeOffset? NextAttempt = null);

public static class DeliveryRules
{
    /// <summary>
    /// Decides the follow-up for a failed attempt: refused parcels and parcels that used up their attempts go back to
    /// the seller; others are retried on the next working day.
    /// </summary>
    public static FailureDecision AfterFailure(int attemptsSoFar, DeliveryFailureReason reason, AutomationPolicy policy, DateTimeOffset now)
    {
        if (reason == DeliveryFailureReason.CustomerRefused && policy.ReturnOnRefusal)
        {
            return new(FailureFollowUp.Return, ReturnReason.CustomerRefused);
        }

        if (attemptsSoFar >= policy.MaxDeliveryAttempts)
        {
            return new(FailureFollowUp.Return, ReturnReason.MaxAttemptsReached);
        }

        return policy.AutoReschedule
            ? new(FailureFollowUp.Reschedule, NextAttempt: DeliveryPromise.NextWorkingDay(now))
            : new(FailureFollowUp.None);
    }
}
