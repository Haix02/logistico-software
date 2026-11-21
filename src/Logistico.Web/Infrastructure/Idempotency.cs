using System.Security.Claims;
using System.Security.Cryptography;
using Logistico.SharedKernel;
using Npgsql;

namespace Logistico.Web.Infrastructure;

/// <summary>
/// Safe retries for requests that change data (Kleppmann, <i>Designing Data-Intensive Applications</i>, ch. 12: a request
/// id carried end to end; IETF draft "The Idempotency-Key HTTP Header Field"). An authenticated POST or PATCH under
/// /api with an <c>Idempotency-Key</c> runs once per key, company and person; a later attempt with the same key and the
/// same request gets the stored answer (<c>Idempotent-Replayed: true</c>) instead of booking the order twice.
/// <list type="bullet">
/// <item>The same key with a different request is refused (422); a key whose first attempt is still running gets 409.</item>
/// <item>Only an answer that succeeded spends the key: a refused or failed attempt changed nothing, so it can be retried
/// with the same key once the problem is fixed.</item>
/// <item>Keys are kept 24 hours (<see cref="IdempotencyCleanup"/>); an attempt that never finished is given up after 5 minutes.</item>
/// <item>Anonymous requests ignore the header: they are rate limited instead, and cannot fill the store.</item>
/// </list>
/// </summary>
internal sealed class IdempotencyMiddleware(RequestDelegate next, IdempotencyStore store, TimeProvider clock, ILogger<IdempotencyMiddleware> logger)
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(24);
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(5);

    /// <summary>Larger answers are not kept (the key is released); the endpoints that use keys answer with a few kilobytes.</summary>
    private const int MaxStoredBody = 1 << 20;

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (!(HttpMethods.IsPost(request.Method) || HttpMethods.IsPatch(request.Method))
            || !request.Path.StartsWithSegments("/api")
            || !request.Headers.TryGetValue(IdempotencyKey.Header, out var values)
            || context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        // A structured-header string may arrive quoted ("abc"); one header only.
        var key = values.Count == 1 ? values[0]?.Trim().Trim('"') : null;
        if (!IdempotencyKey.IsValid(key))
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, "idempotency.key_invalid");
            return;
        }

        var ct = context.RequestAborted;
        var tenant = context.RequestServices.GetRequiredService<ITenantContext>().TenantId;
        var person = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.Identity.Name ?? "";
        var fingerprint = await FingerprintAsync(request, ct);
        var id = new IdempotencyStore.Id(tenant, person, key!);

        var claim = await store.ClaimAsync(id, fingerprint, clock.GetUtcNow(), ct);
        switch (claim.Outcome)
        {
            case ClaimOutcome.Reused:
                await RefuseAsync(context, StatusCodes.Status422UnprocessableEntity, "idempotency.key_reused");
                return;
            case ClaimOutcome.InProgress:
                context.Response.Headers.RetryAfter = "1";
                await RefuseAsync(context, StatusCodes.Status409Conflict, "idempotency.in_progress");
                return;
            case ClaimOutcome.Replay:
                await ReplayAsync(context, claim.Stored!, ct);
                return;
        }

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
        }
        catch
        {
            context.Response.Body = original;
            await KeepGoingAsync(() => store.ReleaseAsync(id, CancellationToken.None), "release");
            throw;
        }

        context.Response.Body = original;
        var response = context.Response;
        if (response.StatusCode is >= 200 and < 300 && buffer.Length <= MaxStoredBody)
        {
            await KeepGoingAsync(() => store.CompleteAsync(id, new StoredAnswer(response.StatusCode, response.ContentType,
                response.Headers.Location.ToString(), response.Headers.ETag.ToString(), buffer.ToArray()), clock.GetUtcNow(), CancellationToken.None), "store");
        }
        else
        {
            await KeepGoingAsync(() => store.ReleaseAsync(id, CancellationToken.None), "release");
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(original, ct);
    }

    /// <summary>
    /// The work is done (or refused) by now, so the person gets its real answer even if the key store is briefly out of
    /// reach. A key left claimed is given up after <see cref="GiveUpAfter"/>.
    /// </summary>
    private async Task KeepGoingAsync(Func<Task> step, string what)
    {
        try
        {
            await step();
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            logger.LogWarning(ex, "Could not {Step} an idempotency key; the answer is sent anyway", what);
        }
    }

    /// <summary>What makes two attempts "the same request": method, path and query, and the body, byte for byte.</summary>
    private static async Task<string> FingerprintAsync(HttpRequest request, CancellationToken ct)
    {
        request.EnableBuffering();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes($"{request.Method}\n{request.Path}{request.QueryString}\n"));
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            hash.AppendData(chunk, 0, read);
        }

        request.Body.Position = 0;
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task ReplayAsync(HttpContext context, StoredAnswer stored, CancellationToken ct)
    {
        var response = context.Response;
        response.StatusCode = stored.StatusCode;
        response.Headers[IdempotencyKey.ReplayedHeader] = "true";
        if (!string.IsNullOrEmpty(stored.ContentType))
        {
            response.ContentType = stored.ContentType;
        }

        if (!string.IsNullOrEmpty(stored.Location))
        {
            response.Headers.Location = stored.Location;
        }

        if (!string.IsNullOrEmpty(stored.ETag))
        {
            response.Headers.ETag = stored.ETag;
        }

        await response.Body.WriteAsync(stored.Body, ct);
    }

    private static Task RefuseAsync(HttpContext context, int status, string code) =>
        Results.Problem(statusCode: status, title: CommonProblems.Text(code), type: $"https://logistico.app/problems/{code}",
            extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context);
}

internal enum ClaimOutcome
{
    /// <summary>This attempt runs the request.</summary>
    Claimed,

    /// <summary>An earlier attempt succeeded: answer with its stored answer.</summary>
    Replay,

    /// <summary>An earlier attempt is still running.</summary>
    InProgress,

    /// <summary>The key belongs to a different request.</summary>
    Reused,
}

internal sealed record StoredAnswer(int StatusCode, string? ContentType, string? Location, string? ETag, byte[] Body);

internal sealed record KeyClaim(ClaimOutcome Outcome, StoredAnswer? Stored = null);

/// <summary>
/// Keys and stored answers in <c>iam.idempotent_requests</c>, on their own connection so that neither the request's
/// transaction nor the audit trail sees them. The table's primary key (company, person, key) makes a claim atomic.
/// </summary>
internal sealed class IdempotencyStore(NpgsqlDataSource database)
{
    internal sealed record Id(Guid Tenant, string Person, string Key);

    public async Task<KeyClaim> ClaimAsync(Id id, string fingerprint, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);

        // A key past its time, or an attempt that never finished, no longer holds the key.
        await ExecuteAsync(connection, """
            DELETE FROM iam.idempotent_requests
            WHERE tenant_id = $1 AND principal = $2 AND key = $3
              AND (created_at < $4 OR (status_code = 0 AND created_at < $5))
            """, ct, id.Tenant, id.Person, id.Key, now - IdempotencyMiddleware.KeepFor, now - IdempotencyMiddleware.GiveUpAfter);

        var claimed = await ExecuteAsync(connection, """
            INSERT INTO iam.idempotent_requests (tenant_id, principal, key, fingerprint, status_code, created_at)
            VALUES ($1, $2, $3, $4, 0, $5)
            ON CONFLICT DO NOTHING
            """, ct, id.Tenant, id.Person, id.Key, fingerprint, now);
        if (claimed == 1)
        {
            return new KeyClaim(ClaimOutcome.Claimed);
        }

        await using var command = new NpgsqlCommand("""
            SELECT fingerprint, status_code, content_type, location, e_tag, body
            FROM iam.idempotent_requests WHERE tenant_id = $1 AND principal = $2 AND key = $3
            """, connection);
        command.Parameters.Add(new() { Value = id.Tenant });
        command.Parameters.Add(new() { Value = id.Person });
        command.Parameters.Add(new() { Value = id.Key });
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Released between the insert and the read: the caller may simply try again.
            return new KeyClaim(ClaimOutcome.InProgress);
        }

        if (reader.GetString(0) != fingerprint)
        {
            return new KeyClaim(ClaimOutcome.Reused);
        }

        var status = reader.GetInt32(1);
        return status == 0
            ? new KeyClaim(ClaimOutcome.InProgress)
            : new KeyClaim(ClaimOutcome.Replay, new StoredAnswer(status, Text(reader, 2), Text(reader, 3), Text(reader, 4),
                reader.IsDBNull(5) ? [] : (byte[])reader.GetValue(5)));
    }

    public async Task CompleteAsync(Id id, StoredAnswer answer, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await ExecuteAsync(connection, """
            UPDATE iam.idempotent_requests
            SET status_code = $4, content_type = $5, location = $6, e_tag = $7, body = $8, completed_at = $9
            WHERE tenant_id = $1 AND principal = $2 AND key = $3
            """, ct, id.Tenant, id.Person, id.Key, answer.StatusCode, Nullable(answer.ContentType), Nullable(answer.Location),
            Nullable(answer.ETag), answer.Body, now);
    }

    public async Task ReleaseAsync(Id id, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await ExecuteAsync(connection, "DELETE FROM iam.idempotent_requests WHERE tenant_id = $1 AND principal = $2 AND key = $3 AND status_code = 0",
            ct, id.Tenant, id.Person, id.Key);
    }

    /// <summary>Removes keys older than <paramref name="before"/>; returns how many.</summary>
    public async Task<int> PurgeAsync(DateTimeOffset before, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        return await ExecuteAsync(connection, "DELETE FROM iam.idempotent_requests WHERE created_at < $1", ct, before);
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
        {
            command.Parameters.Add(new() { Value = value });
        }

        return await command.ExecuteNonQueryAsync(ct);
    }

    private static object Nullable(string? value) => string.IsNullOrEmpty(value) ? DBNull.Value : value;

    private static string? Text(NpgsqlDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetString(column);
}

/// <summary>Steady state (Nygard, <i>Release It!</i>): spent keys are removed every hour, so the store never grows without bound.</summary>
internal sealed class IdempotencyCleanup(IdempotencyStore store, TimeProvider clock, ILogger<IdempotencyCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        do
        {
            try
            {
                var removed = await store.PurgeAsync(clock.GetUtcNow() - IdempotencyMiddleware.KeepFor, stoppingToken);
                if (removed > 0)
                {
                    logger.LogInformation("Removed {Count} spent idempotency keys", removed);
                }
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
            {
                // The database may be starting or briefly unreachable: the next hour tries again.
                logger.LogWarning(ex, "Could not remove spent idempotency keys");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
