using System.Globalization;
using Logistico.Modules.Shipments.Contracts.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Logistico.Modules.Shipments.Infrastructure;

/// <summary>
/// Allocates document numbers from PostgreSQL sequences: safe under concurrency (never duplicates, gaps allowed).
/// Tracking numbers carry a check digit; other documents read like TR-2026-000123.
/// </summary>
internal sealed class NumberGenerator(ShipmentsDbContext db, TimeProvider clock)
{
    public async Task<string> TrackingNumberAsync(CancellationToken ct) =>
        TrackingNumber.Create(clock.GetUtcNow().Year, await NextAsync(ShipmentsDbContext.TrackingSequence, ct));

    /// <summary>Line-haul truck manifest, e.g. TR-2026-000042.</summary>
    public Task<string> TransferNumberAsync(CancellationToken ct) => FormattedAsync("TR", ShipmentsDbContext.TransferSequence, 6, ct);

    /// <summary>Customer / seller request, e.g. RQ-2026-000315.</summary>
    public Task<string> RequestNumberAsync(CancellationToken ct) => FormattedAsync("RQ", ShipmentsDbContext.RequestSequence, 6, ct);

    /// <summary>Return hand-over manifest signed by the seller, e.g. RM-2026-000017.</summary>
    public Task<string> ReturnManifestNumberAsync(CancellationToken ct) => FormattedAsync("RM", ShipmentsDbContext.ReturnManifestSequence, 6, ct);

    /// <summary>COD settlement batch, e.g. STL-2026-0007.</summary>
    public Task<string> SettlementNumberAsync(CancellationToken ct) => FormattedAsync("STL", ShipmentsDbContext.SettlementSequence, 4, ct);

    /// <summary>A courier's cash hand-in at the office, e.g. CH-2026-000031.</summary>
    public Task<string> CashHandoverNumberAsync(CancellationToken ct) => FormattedAsync("CH", ShipmentsDbContext.CashHandoverSequence, 6, ct);

    /// <summary>A count of a hub shelf, e.g. SC-2026-000012.</summary>
    public Task<string> ShelfCheckNumberAsync(CancellationToken ct) => FormattedAsync("SC", ShipmentsDbContext.ShelfCheckSequence, 6, ct);

    /// <summary>
    /// Moves every number sequence past the highest number already stored (data imported or seeded before the
    /// sequences existed), so the next document never collides with an old one. Safe to run on every start.
    /// </summary>
    public async Task AlignSequencesAsync(CancellationToken ct)
    {
        const string Formatted = "'^[A-Z]+-[0-9]{4}-[0-9]+$'";
        var schema = ShipmentsDbContext.Schema;
        (string Sequence, string MaxSql)[] sources =
        [
            (ShipmentsDbContext.TrackingSequence,
                $"SELECT MAX(substring(tracking_number from 5 for 8)::bigint) FROM {schema}.shipments WHERE tracking_number ~ '^LX[0-9]{{11}}$'"),
            (ShipmentsDbContext.TransferSequence,
                $"SELECT MAX(split_part(number, '-', 3)::bigint) FROM {schema}.transfers WHERE number ~ {Formatted}"),
            (ShipmentsDbContext.RequestSequence,
                $"SELECT MAX(split_part(number, '-', 3)::bigint) FROM {schema}.service_requests WHERE number ~ {Formatted}"),
            (ShipmentsDbContext.ReturnManifestSequence,
                $"SELECT MAX(split_part(return_manifest_number, '-', 3)::bigint) FROM {schema}.shipments WHERE return_manifest_number ~ {Formatted}"),
            (ShipmentsDbContext.SettlementSequence,
                $"SELECT MAX(split_part(settlement_number, '-', 3)::bigint) FROM {schema}.cod_settlements WHERE settlement_number ~ {Formatted}"),
            (ShipmentsDbContext.CashHandoverSequence,
                $"SELECT MAX(split_part(number, '-', 3)::bigint) FROM {schema}.cash_handovers WHERE number ~ {Formatted}"),
            (ShipmentsDbContext.ShelfCheckSequence,
                $"SELECT MAX(split_part(number, '-', 3)::bigint) FROM {schema}.shelf_checks WHERE number ~ {Formatted}"),
        ];

        // Plain SQL on purpose: sequences are shared by all companies, so the highest number counts across tenants.
        await WithCommandAsync(async command =>
        {
            foreach (var (sequence, maxSql) in sources)
            {
                command.CommandText = $"""
                    SELECT setval('{schema}.{sequence}', m.value)
                    FROM (SELECT ({maxSql}) AS value) m, {schema}.{sequence} s
                    WHERE m.value IS NOT NULL AND m.value > (CASE WHEN s.is_called THEN s.last_value ELSE s.last_value - 1 END)
                    """;
                await command.ExecuteNonQueryAsync(ct);
            }

            return 0L;
        }, ct);
    }

    private async Task<string> FormattedAsync(string prefix, string sequence, int digits, CancellationToken ct)
    {
        var next = await NextAsync(sequence, ct);
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}-{clock.GetUtcNow().Year}-{next.ToString("D" + digits, CultureInfo.InvariantCulture)}");
    }

    private Task<long> NextAsync(string sequence, CancellationToken ct) =>
        WithCommandAsync(async command =>
        {
            command.CommandText = $"SELECT nextval('{ShipmentsDbContext.Schema}.{sequence}')";
            return Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        }, ct);

    /// <summary>Runs ADO commands on the context's connection (and transaction, if any), opening it only when needed.</summary>
    private async Task<long> WithCommandAsync(Func<System.Data.Common.DbCommand, Task<long>> run, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
        {
            await db.Database.OpenConnectionAsync(ct);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            return await run(command);
        }
        finally
        {
            if (openedHere)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
