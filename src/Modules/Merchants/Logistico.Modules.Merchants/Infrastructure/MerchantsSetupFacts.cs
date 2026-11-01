using Logistico.SharedKernel.Setup;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Merchants.Infrastructure;

/// <summary>The Merchants module's part of getting started (a seller exists) and of the installation health.</summary>
internal sealed class MerchantsSetupFacts(MerchantsDbContext db) : IGettingStartedFacts, IDatabaseHealth
{
    public string Name => MerchantsDbContext.Schema;

    public async Task<IReadOnlyCollection<string>> DoneAsync(CancellationToken cancellationToken) =>
        await db.Merchants.AnyAsync(cancellationToken) ? [GettingStartedSteps.Sellers] : [];

    public async Task<IReadOnlyList<string>> PendingMigrationsAsync(CancellationToken cancellationToken) =>
        [.. await db.Database.GetPendingMigrationsAsync(cancellationToken)];
}
