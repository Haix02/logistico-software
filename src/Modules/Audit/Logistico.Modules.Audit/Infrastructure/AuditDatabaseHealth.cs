using Logistico.SharedKernel.Setup;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Audit.Infrastructure;

/// <summary>The Audit module's part of the installation health.</summary>
internal sealed class AuditDatabaseHealth(AuditDbContext db) : IDatabaseHealth
{
    public string Name => AuditDbContext.Schema;

    public async Task<IReadOnlyList<string>> PendingMigrationsAsync(CancellationToken cancellationToken) =>
        [.. await db.Database.GetPendingMigrationsAsync(cancellationToken)];
}
