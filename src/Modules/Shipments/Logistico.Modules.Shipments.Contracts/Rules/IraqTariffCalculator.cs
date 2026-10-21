namespace Logistico.Modules.Shipments.Contracts.Rules;

/// <summary>
/// Standard Iraqi delivery prices, used where no company price list is at hand (e.g. while parsing pasted orders).
/// Companies set their own prices in a <see cref="RateCard"/>; the order form re-quotes with it.
/// </summary>
public static class IraqTariffCalculator
{
    private static readonly RateCard Standard = new();

    public static decimal CalculateDeliveryFeeIqd(string? originCode, string? destinationCode, decimal grossWeightKg = 1m) =>
        Standard.Quote(originCode ?? "IQ-BG", destinationCode ?? originCode ?? "IQ-BG", grossWeightKg).Fee;
}
