using System.Reflection;
using Logistico.Modules.Merchants;
using Logistico.Modules.Merchants.Contracts;
using Logistico.Modules.Shipments;
using Logistico.Modules.Shipments.Contracts;

namespace Logistico.ArchitectureTests;

/// <summary>Keeps the modular monolith modular (docs/03-system-architecture.md §4).</summary>
public class ModuleBoundaryTests
{
    private static readonly Assembly ShipmentsImplementation = typeof(ShipmentsModule).Assembly;
    private static readonly Assembly ShipmentsContracts = typeof(ShipmentListItem).Assembly;
    private static readonly Assembly MerchantsImplementation = typeof(MerchantsModule).Assembly;
    private static readonly Assembly MerchantsContracts = typeof(MerchantListItem).Assembly;
    private static readonly Assembly SharedKernel = typeof(Logistico.SharedKernel.ITenantContext).Assembly;
    private static readonly Assembly BrowserClient = typeof(Logistico.Web.Client._Imports).Assembly;

    public static TheoryData<string> ContractAssemblies => [ShipmentsContracts.GetName().Name!, MerchantsContracts.GetName().Name!, SharedKernel.GetName().Name!];

    private static string[] References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

    private static Assembly Load(string name) => Assembly.Load(name);

    [Theory]
    [MemberData(nameof(ContractAssemblies))]
    public void Contracts_and_shared_kernel_have_no_server_dependencies(string assemblyName)
    {
        var refs = References(Load(assemblyName));

        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("Npgsql", StringComparison.Ordinal));
        Assert.DoesNotContain(ShipmentsImplementation.GetName().Name, refs);
        Assert.DoesNotContain(MerchantsImplementation.GetName().Name, refs);
    }

    [Fact]
    public void Browser_client_only_sees_contracts()
    {
        var refs = References(BrowserClient);

        Assert.DoesNotContain(ShipmentsImplementation.GetName().Name, refs);
        Assert.DoesNotContain(MerchantsImplementation.GetName().Name, refs);
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("Npgsql", StringComparison.Ordinal));
        Assert.DoesNotContain("Logistico.Web", refs);
    }

    [Fact]
    public void Modules_talk_to_each_other_only_through_contracts()
    {
        Assert.DoesNotContain(MerchantsImplementation.GetName().Name, References(ShipmentsImplementation));
        Assert.DoesNotContain(ShipmentsImplementation.GetName().Name, References(MerchantsImplementation));
        Assert.Contains(MerchantsContracts.GetName().Name, References(ShipmentsImplementation));
    }

    [Fact]
    public void Modules_do_not_depend_on_the_host()
    {
        Assert.DoesNotContain("Logistico.Web", References(ShipmentsImplementation));
        Assert.DoesNotContain("Logistico.Web", References(MerchantsImplementation));
    }

    [Theory]
    [InlineData(typeof(ShipmentsModule))]
    [InlineData(typeof(MerchantsModule))]
    public void Only_the_module_entry_point_and_migrations_are_public(Type entryPoint)
    {
        var leaked = entryPoint.Assembly.GetExportedTypes()
            .Where(t => t != entryPoint)
            .Where(t => t.Namespace?.EndsWith(".Migrations", StringComparison.Ordinal) != true)
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(leaked);
    }
}
