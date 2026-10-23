using Logistico.Modules.Shipments.Application;
using Logistico.Modules.Shipments.Endpoints;
using Logistico.Modules.Shipments.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Logistico.Modules.Shipments;

/// <summary>The only public entry point of the Shipments module.</summary>
public static class ShipmentsModule
{
    /// <summary>Rate-limiting policy the host must register for the anonymous tracking endpoint.</summary>
    public const string PublicTrackingRateLimitPolicy = "public-tracking";

    /// <summary>Rate-limiting policy for anonymous customer requests (stricter: they change orders).</summary>
    public const string PublicRequestsRateLimitPolicy = "public-requests";

    public static IServiceCollection AddShipmentsModule(this IServiceCollection services, string connectionString)
    {
        // Registered save interceptors (the audit trail) record every change this module saves.
        services.AddDbContext<ShipmentsDbContext>((sp, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", ShipmentsDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddMemoryCache();
        services.AddSingleton<CustomerVerification>();
        services.AddScoped<NumberGenerator>();
        services.AddScoped<ShipmentsDemoSeeder>();
        services.AddScoped<AutomationPolicies>();
        services.AddScoped<Activity>();
        services.AddScoped<HubDirectory>();
        services.AddScoped<WarehouseAdmin>();
        services.AddScoped<ConfirmationDesk>();
        services.AddScoped<Pricing>();
        services.AddScoped<TransferPlanner>();
        services.AddScoped<WarehouseScanner>();
        services.AddScoped<RequestDesk>();
        services.AddScoped<ReturnDesk>();
        services.AddScoped<OrderDesk>();
        services.AddScoped<AutomationEngine>();
        services.AddHostedService<AutomationWorker>();
        return services;
    }

    public static IEndpointRouteBuilder MapShipmentsModule(this IEndpointRouteBuilder app) => app
        .MapShipmentEndpoints()
        .MapWarehouseEndpoints()
        .MapTransferEndpoints()
        .MapReturnEndpoints()
        .MapRequestEndpoints()
        .MapAutomationEndpoints()
        .MapNetworkEndpoints()
        .MapWarehousesEndpoints()
        .MapAnalyticsEndpoints()
        .MapInsightsEndpoints()
        .MapCustomerEndpoints()
        .MapConfirmationEndpoints()
        .MapPricingEndpoints()
        .MapPublicEndpoints()
        .MapCustomsEndpoints()
        .MapFleetEndpoints()
        .MapCourierAppEndpoints()
        .MapSellerPortalEndpoints()
        .MapFinanceEndpoints()
        .MapViewEndpoints();

    public static async Task MigrateShipmentsDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ShipmentsDbContext>().Database.MigrateAsync(ct);
        await scope.ServiceProvider.GetRequiredService<NumberGenerator>().AlignSequencesAsync(ct);
    }

    public static async Task SeedShipmentsDemoDataAsync(this IServiceProvider services, int count, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ShipmentsDemoSeeder>().SeedAsync(count, ct);
    }
}
