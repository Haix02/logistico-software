using System.ComponentModel.DataAnnotations;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Iraq;

namespace Logistico.Modules.Shipments.Contracts.Rules;

/// <summary>
/// A company's delivery prices (IQD): one price inside the seller's own governorate, a price per destination
/// governorate otherwise, a weight allowance with a charge per extra kilogram, and discounts for chosen sellers.
/// The order form and the server quote from the same card, so the fee a seller sees is the fee that is booked.
/// </summary>
public sealed class RateCard : IValidatableObject
{
    /// <summary>Fees are rounded to the nearest 250 IQD (the smallest note in daily use).</summary>
    public const decimal Rounding = 250m;

    [Range(0, 1_000_000, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal SameGovernorateFee { get; set; } = 5_000m;

    /// <summary>Price to a governorate that has no price of its own.</summary>
    [Range(0, 1_000_000, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal DefaultFee { get; set; } = 6_000m;

    /// <summary>Price per destination governorate (ISO code → IQD), for deliveries outside the seller's governorate.</summary>
    public Dictionary<string, decimal> GovernorateFees { get; set; } = DefaultGovernorateFees();

    [Range(0, 100, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal IncludedWeightKg { get; set; } = 5m;

    [Range(0, 100_000, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal ExtraKgFee { get; set; } = 1_000m;

    /// <summary>Sellers with a special price: a percentage off every delivery.</summary>
    public List<SellerDiscount> SellerDiscounts { get; set; } = [];

    /// <summary>
    /// Charged to the seller when a parcel goes back to them, as a percentage of its delivery fee (0 = returns are free).
    /// See <see cref="ReturnFee"/> for which returns pay.
    /// </summary>
    [Range(0, 100, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal ReturnFeePercent { get; set; }

    public uint Version { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>The delivery fee for a parcel from <paramref name="origin"/> to <paramref name="destination"/>.</summary>
    public PriceQuote Quote(string? origin, string? destination, decimal weightKg, Guid? merchantId = null)
    {
        var from = origin?.Trim().ToUpperInvariant() ?? "";
        var to = destination?.Trim().ToUpperInvariant() ?? "";
        var local = to.Length == 0 || from == to;
        var baseFee = local ? SameGovernorateFee : GovernorateFees.GetValueOrDefault(to, DefaultFee);
        var extraKg = weightKg > IncludedWeightKg ? Math.Ceiling(weightKg - IncludedWeightKg) : 0m;
        var gross = baseFee + extraKg * ExtraKgFee;
        var discount = merchantId is { } id ? SellerDiscounts.FirstOrDefault(d => d.MerchantId == id)?.Percent ?? 0m : 0m;
        var fee = Math.Round(gross * (100m - discount) / 100m / Rounding, MidpointRounding.AwayFromZero) * Rounding;
        return new PriceQuote(fee, baseFee, extraKg * ExtraKgFee, discount, local);
    }

    /// <summary>
    /// The fee for sending a parcel back to its seller, rounded like delivery fees. Returns on the seller's or their
    /// customer's side pay (refused, unreachable, returned by the customer, cancelled after pickup, wrong address);
    /// damage in the company's care and other reasons do not.
    /// </summary>
    public decimal ReturnFee(decimal? deliveryFee, ReturnReason? reason)
    {
        if (ReturnFeePercent <= 0 || deliveryFee is not > 0 || reason is null or ReturnReason.Damaged or ReturnReason.Other)
        {
            return 0m;
        }

        return Math.Round(deliveryFee.Value * ReturnFeePercent / 100m / Rounding, MidpointRounding.AwayFromZero) * Rounding;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (GovernorateFees.Any(f => !IraqGovernorates.IsKnown(f.Key) || f.Value is < 0 or > 1_000_000))
        {
            yield return new(ValidationText.Governorate, [nameof(GovernorateFees)]);
        }

        if (SellerDiscounts.Any(d => d.Percent is < 0 or > 100) || SellerDiscounts.GroupBy(d => d.MerchantId).Any(g => g.Count() > 1))
        {
            yield return new(string.Format(System.Globalization.CultureInfo.CurrentCulture, ValidationText.Range, "", 0, 100), [nameof(SellerDiscounts)]);
        }
    }

    public RateCard Clone() => new()
    {
        SameGovernorateFee = SameGovernorateFee,
        DefaultFee = DefaultFee,
        GovernorateFees = new(GovernorateFees, StringComparer.OrdinalIgnoreCase),
        IncludedWeightKg = IncludedWeightKg,
        ExtraKgFee = ExtraKgFee,
        SellerDiscounts = [.. SellerDiscounts.Select(d => d with { })],
        Version = Version,
        UpdatedAt = UpdatedAt,
        UpdatedBy = UpdatedBy,
    };

    /// <summary>Common Iraqi prices: north 8,000, south 7,000, central and mid-Euphrates 6,000 IQD.</summary>
    public static Dictionary<string, decimal> DefaultGovernorateFees() => IraqGovernorates.All.ToDictionary(
        g => g.Code,
        g => g.Code switch
        {
            "IQ-AR" or "IQ-SU" or "IQ-DA" or "IQ-KI" or "IQ-NI" or "IQ-HA" => 8_000m,
            "IQ-BA" or "IQ-DQ" or "IQ-MA" or "IQ-MU" => 7_000m,
            _ => 6_000m,
        },
        StringComparer.OrdinalIgnoreCase);
}

public sealed record SellerDiscount(Guid MerchantId, string MerchantName, decimal Percent);

/// <summary>How a fee was made up, for showing the seller.</summary>
public sealed record PriceQuote(decimal Fee, decimal BaseFee, decimal WeightCharge, decimal DiscountPercent, bool SameGovernorate);
