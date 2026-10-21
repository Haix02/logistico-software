using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Domain;

/// <summary>
/// A seller COD remittance batch settling all delivered cash-on-delivery orders for a merchant up to that point.
/// Net payout = Gross COD collected − Delivery fees.
/// </summary>
internal sealed class CodSettlement
{
    private CodSettlement()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string SettlementNumber { get; private set; } = "";
    public Guid MerchantId { get; private set; }
    public string MerchantName { get; private set; } = "";
    public string Currency { get; private set; } = "IQD";
    public int ShipmentCount { get; private set; }
    public decimal GrossCodAmount { get; private set; }
    public decimal DeliveryFeesAmount { get; private set; }
    public decimal NetPayoutAmount { get; private set; }
    public PayoutMethod PayoutMethod { get; private set; }
    public string? PayoutReference { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset SettledAt { get; private set; }
    public string? SettledBy { get; private set; }

    public static CodSettlement Create(
        Guid tenantId,
        string settlementNumber,
        Guid merchantId,
        string merchantName,
        IReadOnlyCollection<Shipment> shipments,
        PayoutMethod payoutMethod,
        string? payoutReference,
        string? notes,
        string? settledBy,
        DateTimeOffset now)
    {
        if (shipments.Count == 0)
        {
            throw new DomainException("finance.nothing_to_settle", "This seller has no unsettled delivered cash-on-delivery orders.");
        }

        var gross = shipments.Sum(s => s.CodAmount ?? 0m);
        var fees = shipments.Sum(s => s.DeliveryFee ?? 0m);
        var net = gross - fees;
        var id = Guid.CreateVersion7(now);

        foreach (var shipment in shipments)
        {
            shipment.MarkCodSettled(id, now);
        }

        return new CodSettlement
        {
            Id = id,
            TenantId = tenantId,
            SettlementNumber = settlementNumber,
            MerchantId = merchantId,
            MerchantName = merchantName,
            Currency = "IQD",
            ShipmentCount = shipments.Count,
            GrossCodAmount = gross,
            DeliveryFeesAmount = fees,
            NetPayoutAmount = net,
            PayoutMethod = payoutMethod,
            PayoutReference = string.IsNullOrWhiteSpace(payoutReference) ? null : payoutReference.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            SettledAt = now,
            SettledBy = settledBy,
        };
    }
}

