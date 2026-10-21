using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Logistico.Modules.Shipments.Infrastructure;

internal static class DbErrors
{
    /// <summary>A unique index refused the row: someone else saved the same thing a moment earlier.</summary>
    public static bool IsUniqueViolation(this DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
