using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Modules.Shipments.Tests;

public class ChargeableWeightTests
{
    [Theory]
    [InlineData(TransportMode.Air, 40, 0.5, 83.333)]       // 0.5 m³ × 1,000,000 / 6000
    [InlineData(TransportMode.Air, 100, 0.3, 100)]         // actual weight wins
    [InlineData(TransportMode.Courier, 10, 0.1, 20)]       // divisor 5000
    [InlineData(TransportMode.Road, 200, 1, 333.333)]      // divisor 3000
    [InlineData(TransportMode.SeaLcl, 850, 3.2, 3200)]     // W/M: 1 m³ = 1000 kg
    [InlineData(TransportMode.SeaFcl, 18000, 10, 18000)]
    public void Uses_the_greater_of_actual_and_volumetric_weight(TransportMode mode, double gross, double cbm, double expected)
    {
        Assert.Equal((decimal)expected, ChargeableWeight.Calculate(mode, (decimal)gross, (decimal)cbm));
    }

    [Fact]
    public void Rejects_negative_inputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChargeableWeight.Calculate(TransportMode.Air, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChargeableWeight.Calculate(TransportMode.Air, 1, -0.1m));
    }
}
