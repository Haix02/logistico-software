using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Domain;

/// <summary>
/// A courier hands the cash collected on delivery in at the office. Every parcel whose cash it covers is marked, and the
/// amount counted is kept beside the amount expected, so a shortage (or an extra) stays on record. Only cash the company
/// holds can be paid out to sellers.
/// </summary>
internal sealed class CashHandover
{
    private CashHandover()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Number { get; private set; } = "";
    public Guid CourierId { get; private set; }
    public string CourierLabel { get; private set; } = "";
    public int ParcelCount { get; private set; }
    public decimal ExpectedIqd { get; private set; }
    public decimal ReceivedIqd { get; private set; }

    /// <summary>Received minus expected: negative is a shortage.</summary>
    public decimal DifferenceIqd { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public string? ReceivedBy { get; private set; }

    public static CashHandover Create(
        Guid tenantId,
        string number,
        Courier courier,
        IReadOnlyCollection<Shipment> parcels,
        decimal receivedIqd,
        string? notes,
        string? receivedBy,
        DateTimeOffset now)
    {
        if (parcels.Count == 0)
        {
            throw new DomainException("cash.nothing_to_hand_in", "This courier has no collected cash to hand in.");
        }

        if (receivedIqd < 0)
        {
            throw new DomainException("cash.negative", "The amount received cannot be negative.");
        }

        var id = Guid.CreateVersion7(now);
        var expected = parcels.Sum(p => p.CodAmount ?? 0m);
        foreach (var parcel in parcels)
        {
            parcel.MarkCashHandedOver(id, now);
        }

        return new CashHandover
        {
            Id = id,
            TenantId = tenantId,
            Number = number,
            CourierId = courier.Id,
            CourierLabel = courier.DisplayLabel,
            ParcelCount = parcels.Count,
            ExpectedIqd = expected,
            ReceivedIqd = receivedIqd,
            DifferenceIqd = receivedIqd - expected,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            ReceivedAt = now,
            ReceivedBy = receivedBy,
        };
    }
}
