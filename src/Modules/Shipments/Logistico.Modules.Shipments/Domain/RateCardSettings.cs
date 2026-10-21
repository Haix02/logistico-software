using System.Text.Json;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Modules.Shipments.Domain;

/// <summary>A company's stored price list (one row per company); missing means the standard Iraqi prices.</summary>
internal sealed class RateCardSettings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private RateCardSettings()
    {
    }

    public Guid TenantId { get; private set; }
    public decimal SameGovernorateFee { get; private set; }
    public decimal DefaultFee { get; private set; }
    public decimal IncludedWeightKg { get; private set; }
    public decimal ExtraKgFee { get; private set; }

    /// <summary>Governorate prices as JSON ({"IQ-BA": 7000, …}).</summary>
    public string GovernorateFees { get; private set; } = "{}";

    /// <summary>Seller discounts as JSON ([{ "merchantId": …, "merchantName": …, "percent": 10 }]).</summary>
    public string SellerDiscounts { get; private set; } = "[]";

    public DateTimeOffset UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }
    public uint Version { get; private set; }

    public static RateCardSettings Create(Guid tenantId, RateCard card, string? by, DateTimeOffset now)
    {
        var settings = new RateCardSettings { TenantId = tenantId };
        settings.Update(card, by, now);
        return settings;
    }

    public void Update(RateCard card, string? by, DateTimeOffset now)
    {
        SameGovernorateFee = card.SameGovernorateFee;
        DefaultFee = card.DefaultFee;
        IncludedWeightKg = card.IncludedWeightKg;
        ExtraKgFee = card.ExtraKgFee;
        GovernorateFees = JsonSerializer.Serialize(
            card.GovernorateFees.ToDictionary(f => f.Key.ToUpperInvariant(), f => f.Value), Json);
        SellerDiscounts = JsonSerializer.Serialize(card.SellerDiscounts.Where(d => d.Percent > 0).ToList(), Json);
        UpdatedAt = now;
        UpdatedBy = by;
    }

    public RateCard ToRateCard()
    {
        var fees = JsonSerializer.Deserialize<Dictionary<string, decimal>>(GovernorateFees, Json) ?? [];
        return new RateCard
        {
            SameGovernorateFee = SameGovernorateFee,
            DefaultFee = DefaultFee,
            IncludedWeightKg = IncludedWeightKg,
            ExtraKgFee = ExtraKgFee,
            GovernorateFees = new Dictionary<string, decimal>(fees, StringComparer.OrdinalIgnoreCase),
            SellerDiscounts = JsonSerializer.Deserialize<List<SellerDiscount>>(SellerDiscounts, Json) ?? [],
            Version = Version,
            UpdatedAt = UpdatedAt,
            UpdatedBy = UpdatedBy,
        };
    }
}
