namespace Logistico.Modules.Shipments.Contracts.Rules;

/// <summary>
/// Chargeable weight = max(actual weight, volumetric weight).
/// Air uses 6000 cm³/kg, courier 5000, road 3000; sea (FCL/LCL) uses weight/measure where 1 m³ = 1000 kg.
/// </summary>
public static class ChargeableWeight
{
    public static decimal VolumetricDivisor(TransportMode mode) => mode switch
    {
        TransportMode.Air => 6000m,
        TransportMode.Courier => 5000m,
        TransportMode.Road => 3000m,
        _ => 0m,
    };

    public static decimal VolumetricWeightKg(TransportMode mode, decimal volumeCbm) => mode switch
    {
        TransportMode.SeaFcl or TransportMode.SeaLcl => volumeCbm * 1000m,
        _ => volumeCbm * 1_000_000m / VolumetricDivisor(mode),
    };

    public static decimal Calculate(TransportMode mode, decimal grossWeightKg, decimal volumeCbm)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(grossWeightKg);
        ArgumentOutOfRangeException.ThrowIfNegative(volumeCbm);
        var chargeable = Math.Max(grossWeightKg, VolumetricWeightKg(mode, volumeCbm));
        return Math.Round(chargeable, 3, MidpointRounding.AwayFromZero);
    }
}
