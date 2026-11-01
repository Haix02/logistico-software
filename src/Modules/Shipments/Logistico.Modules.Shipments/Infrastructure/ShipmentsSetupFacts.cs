using Logistico.SharedKernel.Setup;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Infrastructure;

/// <summary>
/// The Shipments module's part of getting started (docs/19): which steps the company's data completes, and of the
/// installation health. Settings written by start-up or sample data ("system", "seed") do not count as reviewed.
/// </summary>
internal sealed class ShipmentsSetupFacts(ShipmentsDbContext db) : IGettingStartedFacts, IDatabaseHealth
{
    private static readonly string[] Bootstrap = ["system", "seed"];

    public string Name => ShipmentsDbContext.Schema;

    public async Task<IReadOnlyCollection<string>> DoneAsync(CancellationToken cancellationToken)
    {
        var done = new List<string>();
        if (await db.Warehouses.AnyAsync(w => w.UpdatedBy != null && !Bootstrap.Contains(w.UpdatedBy), cancellationToken))
        {
            done.Add(GettingStartedSteps.Warehouses);
        }

        if (await db.RateCards.AnyAsync(r => r.UpdatedBy != null && !Bootstrap.Contains(r.UpdatedBy), cancellationToken))
        {
            done.Add(GettingStartedSteps.Prices);
        }

        if (await db.AutomationSettings.AnyAsync(a => a.UpdatedBy != null && !Bootstrap.Contains(a.UpdatedBy), cancellationToken))
        {
            done.Add(GettingStartedSteps.Automation);
        }

        if (await db.Couriers.AnyAsync(cancellationToken))
        {
            done.Add(GettingStartedSteps.Couriers);
        }

        if (await db.ShelfRules.AnyAsync(cancellationToken))
        {
            done.Add(GettingStartedSteps.SortPlan);
        }

        if (await db.Shipments.AnyAsync(cancellationToken))
        {
            done.Add(GettingStartedSteps.FirstOrder);
        }

        if (await db.HubScans.AnyAsync(cancellationToken))
        {
            done.Add(GettingStartedSteps.FirstScan);
        }

        return done;
    }

    public async Task<IReadOnlyList<string>> PendingMigrationsAsync(CancellationToken cancellationToken) =>
        [.. await db.Database.GetPendingMigrationsAsync(cancellationToken)];
}
