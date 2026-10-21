using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Modules.Shipments.Tests;

public class HubRoutingTests
{
    [Theory]
    [InlineData("BGW", "IQ-BG", "IQ-BA", false)] // Baghdad seller → Basra customer, first mile at Baghdad hub: correct
    [InlineData("BSR", "IQ-BG", "IQ-BA", false)] // last mile at Basra hub: correct
    [InlineData("EBL", "IQ-BG", "IQ-BA", true)]  // Erbil hub has nothing to do with it: misrouted
    [InlineData("BGW", "CNSHA", "IQBSR", true)]  // international cargo for Basra scanned in Baghdad: misrouted
    [InlineData("BSR", "CNSHA", "IQBSR", false)]
    [InlineData("BGW", null, "IQ-BA", true)]     // origin unknown: destination hub only
    public void A_parcel_belongs_at_its_origin_or_destination_hub(string scannedHub, string? origin, string destination, bool misrouted)
    {
        Assert.Equal(misrouted, IraqHubs.Default.IsCrossHubOrMisrouted(scannedHub, destination, out _, origin));
    }
}
