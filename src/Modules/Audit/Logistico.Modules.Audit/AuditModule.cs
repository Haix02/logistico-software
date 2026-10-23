using Logistico.Modules.Audit.Endpoints;
using Logistico.Modules.Audit.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Logistico.Modules.Audit;

/// <summary>
/// The only public entry point of the Audit module: who changed what, for every record the other modules save. A
/// module's database context records its changes by adding the registered save interceptors:
/// <c>options.AddInterceptors(services.GetServices&lt;ISaveChangesInterceptor&gt;())</c>.
/// </summary>
public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AuditDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", AuditDbContext.Schema))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<ISaveChangesInterceptor, AuditInterceptor>();
        return services;
    }

    public static IEndpointRouteBuilder MapAuditModule(this IEndpointRouteBuilder app) => app.MapAuditEndpoints();

    public static async Task MigrateAuditDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.MigrateAsync(ct);
    }
}
