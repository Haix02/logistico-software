using Logistico.Modules.Merchants.Contracts;
using Logistico.Modules.Merchants.Endpoints;
using Logistico.Modules.Merchants.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistico.Modules.Merchants;

/// <summary>The only public entry point of the Merchants (sellers) module.</summary>
public static class MerchantsModule
{
    public static IServiceCollection AddMerchantsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<MerchantsDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", MerchantsDbContext.Schema))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IMerchantDirectory, MerchantDirectory>();
        services.AddScoped<MerchantsDemoSeeder>();
        return services;
    }

    public static IEndpointRouteBuilder MapMerchantsModule(this IEndpointRouteBuilder app) => app.MapMerchantEndpoints();

    public static async Task MigrateMerchantsDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MerchantsDbContext>().Database.MigrateAsync(ct);
    }

    public static async Task SeedMerchantsDemoDataAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MerchantsDemoSeeder>().SeedAsync(ct);
    }
}
