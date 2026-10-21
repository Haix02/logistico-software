using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Modules.Shipments.Tests;

/// <summary>Routing follows each company's own warehouses, not a fixed list.</summary>
public class HubNetworkTests
{
    private static readonly HubInfo Baghdad = IraqHubs.Default.Find(IraqHubs.Baghdad)!;

    [Fact]
    public void The_standard_network_covers_every_governorate_exactly_once()
    {
        var covered = IraqHubs.Standard.SelectMany(h => h.Covers).ToList();

        Assert.Equal(covered.Count, covered.Distinct().Count());
        Assert.All(Logistico.SharedKernel.Iraq.IraqGovernorates.All, g => Assert.Contains(g.Code, covered));
    }

    [Fact]
    public void A_new_branch_takes_over_the_governorates_it_covers()
    {
        var kirkuk = new HubInfo("HUB-KRK", "KRK", "IQ-KI", "فرع كركوك", "Kirkuk Branch")
        {
            Kind = HubKind.Branch,
            Latitude = 35.468,
            Longitude = 44.392,
            Covers = ["IQ-KI"],
        };
        var network = new HubNetwork([kirkuk, .. IraqHubs.Standard.Select(h => h with { Covers = [.. h.Covers.Where(c => c != "IQ-KI")] })]);

        Assert.Equal("HUB-KRK", network.ResolveDestinationHub("IQ-KI").Code);
        Assert.Equal(IraqHubs.Erbil, network.ResolveDestinationHub("IQ-AR").Code);
        Assert.Equal("HUB-KRK", network.Find("krk")!.Code);
    }

    [Fact]
    public void A_governorate_nobody_covers_goes_to_the_nearest_hub()
    {
        // Najaf retired: its governorates fall to the closest remaining hub by road.
        var network = new HubNetwork(IraqHubs.Standard.Where(h => h.Code != IraqHubs.Najaf));

        Assert.Equal(IraqHubs.Baghdad, network.ResolveDestinationHub("IQ-BB").Code);   // Hillah is ~100 km from Baghdad
        Assert.Equal(IraqHubs.Basra, network.ResolveDestinationHub("IQ-MU").Code);     // still covered by Basra
    }

    [Theory]
    [InlineData(IraqHubs.Baghdad, IraqHubs.Basra, 7)]
    [InlineData(IraqHubs.Baghdad, IraqHubs.Najaf, 3)]
    [InlineData(IraqHubs.Erbil, IraqHubs.Mosul, 2)]
    [InlineData(IraqHubs.Baghdad, IraqHubs.Baghdad, 0)]
    public void Truck_times_come_from_the_distance_between_hubs(string from, string to, int hours) =>
        Assert.Equal(hours, IraqHubs.Default.TransitHours(from, to));

    [Fact]
    public void Ports_belong_to_the_hub_of_their_governorate_and_foreign_origins_to_none()
    {
        Assert.Equal(IraqHubs.Basra, IraqHubs.Default.ResolveDestinationHub("IQUQR").Code);
        Assert.Equal(IraqHubs.Basra, IraqHubs.Default.OriginHubFor("IQ-BA")!.Code);
        Assert.Null(IraqHubs.Default.OriginHubFor("CNSHA"));
    }

    [Fact]
    public void Retired_hubs_still_display_by_their_tag()
    {
        var network = new HubNetwork([Baghdad]);

        var retired = network.FindOrDefault("HUB-OLD");

        Assert.Equal("OLD", retired.RoutingTag);
        Assert.Null(network.Find("HUB-OLD"));
        Assert.False(network.TryParseActionBarcode("CMD:HUB:HUB-OLD", out _));
        Assert.Contains(network.ActionBarcodes, b => b.Code == "CMD:HUB:" + IraqHubs.Baghdad);
    }

    [Theory]
    [InlineData("HUB-NJF", "NJF")]
    [InlineData("hub-bsr", "BSR")]
    [InlineData(null, "")]
    public void Routing_tags_come_from_hub_codes(string? code, string tag) => Assert.Equal(tag, IraqHubs.TagOf(code));
}
