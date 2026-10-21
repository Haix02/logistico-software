using Logistico.Modules.Shipments.Contracts;

namespace Logistico.Modules.Shipments.Tests;

public class CustomerRiskTests
{
    [Theory]
    [InlineData(0, 0, 0, 0, CustomerRisk.New)]       // never ordered
    [InlineData(0, 0, 0, 3, CustomerRisk.Medium)]    // three attempts, never reached
    [InlineData(5, 0, 0, 1, CustomerRisk.Low)]       // reliable
    [InlineData(4, 1, 0, 0, CustomerRisk.Low)]       // 20 % back
    [InlineData(3, 1, 0, 0, CustomerRisk.Medium)]    // 25 % back
    [InlineData(4, 1, 1, 0, CustomerRisk.Medium)]    // refused once
    [InlineData(1, 2, 0, 0, CustomerRisk.High)]      // most orders came back
    [InlineData(6, 2, 2, 0, CustomerRisk.High)]      // refused twice
    public void Risk_follows_the_customers_delivery_history(int delivered, int returned, int refused, int failedAttempts, CustomerRisk expected) =>
        Assert.Equal(expected, CustomerRiskRules.Assess(delivered, returned, refused, failedAttempts));
}
