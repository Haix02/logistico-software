using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Logistico.Web.Infrastructure;

/// <summary>Verifies that the shared PostgreSQL database accepts connections and queries.</summary>
internal sealed class PostgresHealthCheck(string connectionString) : IHealthCheck
{
    private readonly string connectionString = WithShortConnectTimeout(connectionString);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("SELECT 1", connection);
            _ = await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is unavailable.", ex);
        }
    }

    private static string WithShortConnectTimeout(string value)
    {
        var builder = new NpgsqlConnectionStringBuilder(value)
        {
            Timeout = 3,
            CommandTimeout = 3,
        };
        return builder.ConnectionString;
    }
}
