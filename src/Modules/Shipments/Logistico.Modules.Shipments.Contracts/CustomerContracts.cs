using System.Text.Json.Serialization;

namespace Logistico.Modules.Shipments.Contracts;

/// <summary>
/// What we know about a customer (by mobile number) from earlier orders across all sellers: where they live,
/// how often their parcels were delivered or came back, and their orders still in progress.
/// </summary>
public sealed record CustomerProfile(
    string Phone,
    string? Name,
    string? Governorate,
    string? City,
    string? Address,
    string? Landmark,
    int Orders,
    int Delivered,
    int Returned,
    int Refused,
    int FailedAttempts,
    DateTimeOffset? LastOrderAt,
    CustomerRisk Risk,
    IReadOnlyList<CustomerOrderRef> Active,
    IReadOnlyList<CustomerOrderRef> Recent)
{
    /// <summary>Delivered out of the orders that finished, in percent; null for new customers.</summary>
    public decimal? SuccessRate => Delivered + Returned == 0 ? null : Math.Round(Delivered * 100m / (Delivered + Returned), 1);
}

public sealed record CustomerOrderRef(
    Guid Id,
    string TrackingNumber,
    ShipmentStatus Status,
    Guid? MerchantId,
    string? MerchantName,
    decimal? CodAmount,
    DateTimeOffset CreatedAt);

/// <summary>How likely a cash-on-delivery order to this customer is to come back.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CustomerRisk>))]
public enum CustomerRisk
{
    /// <summary>No finished order yet.</summary>
    New,
    Low,
    Medium,
    High,
}

/// <summary>Customer risk from delivery history. Simple, explainable rules, shared by the order form and the server.</summary>
public static class CustomerRiskRules
{
    public static CustomerRisk Assess(int delivered, int returned, int refused, int failedAttempts)
    {
        var finished = delivered + returned;
        if (finished == 0 && refused == 0)
        {
            return failedAttempts >= 3 ? CustomerRisk.Medium : CustomerRisk.New;
        }

        var returnRate = finished == 0 ? 0 : (double)returned / finished;
        if (refused >= 2 || (finished >= 3 && returnRate >= 0.5))
        {
            return CustomerRisk.High;
        }

        if (refused == 1 || (finished >= 2 && returnRate >= 0.25) || (delivered == 0 && failedAttempts >= 3))
        {
            return CustomerRisk.Medium;
        }

        return CustomerRisk.Low;
    }
}
