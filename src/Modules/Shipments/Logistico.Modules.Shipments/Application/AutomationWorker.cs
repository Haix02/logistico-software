using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Logistico.Modules.Shipments.Application;

/// <summary>
/// Runs the automation sweep for every company on a timer (Automation:IntervalSeconds, default 60).
/// Disable with Automation:Enabled=false (integration tests trigger sweeps through the API instead).
/// </summary>
internal sealed class AutomationWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<AutomationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Automation:Enabled", true))
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Automation:IntervalSeconds", 60), 10, 3600));
        using var timer = new PeriodicTimer(interval);
        try
        {
            do
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Automation sweep failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown. PeriodicTimer throws when its cancellation token is signalled.
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        List<Guid> tenants;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShipmentsDbContext>();
            tenants = await db.Shipments.IgnoreQueryFilters().Select(s => s.TenantId).Distinct().ToListAsync(ct);
        }

        foreach (var tenantId in tenants)
        {
            using (TenantScope.Begin(tenantId))
            using (AuditScope.As("automation"))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var result = await scope.ServiceProvider.GetRequiredService<AutomationEngine>().SweepAsync(tenantId, ct);
                    if (result.Raised + result.Resolved + result.Planned + result.ReturnsReady > 0)
                    {
                        logger.LogInformation(
                            "Automation for tenant {TenantId}: {Raised} alerts raised, {Resolved} resolved, {Planned} parcels planned, {Ready} returns ready",
                            tenantId, result.Raised, result.Resolved, result.Planned, result.ReturnsReady);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    // One company's failure must not stop the sweep for the others.
                    logger.LogError(ex, "Automation sweep failed for tenant {TenantId}", tenantId);
                }
            }
        }
    }
}
