using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Logistico.Modules.Audit.Contracts;
using Logistico.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Logistico.Modules.Audit.Infrastructure;

/// <summary>
/// Records every change saved through the module databases (orders, prices, rules, customs, finance, sellers, staff
/// accounts) in <c>audit.entries</c>, in the same transaction as the change: a change is never saved without its
/// entry, nor an entry without its change. Modules pick it up from DI as an <see cref="ISaveChangesInterceptor"/>.
/// </summary>
internal sealed class AuditInterceptor(IHttpContextAccessor http, ITenantContext tenant, TimeProvider clock) : SaveChangesInterceptor
{
    /// <summary>Records that keep their own history (timeline, scans, logs) or are working state, not business data.</summary>
    private static readonly HashSet<string> SkippedEntities =
        ["ShipmentEvent", "HubScan", "TransferItem", "AutomationLogEntry", "CustomerNotification", "OpsAlert", "IdentityUserToken", "SavedView", "DeliveryProof", "ShelfCheckLine"];

    /// <summary>Bookkeeping that changes with every save.</summary>
    private static readonly HashSet<string> SkippedFields =
        ["TenantId", "UpdatedAt", "UpdatedBy", "LastStatusAt", "LastSweepAt", "ConcurrencyStamp", "NormalizedEmail", "NormalizedUserName", "NormalizedName"];

    /// <summary>Secrets: recorded as changed, never with their value.</summary>
    private static readonly HashSet<string> SecretFields = ["PasswordHash", "SecurityStamp"];

    /// <summary>The first of these a record has is how people know it.</summary>
    private static readonly string[] LabelFields = ["TrackingNumber", "SettlementNumber", "TransferNumber", "Number", "Email", "Code", "Name"];

    private const string Hidden = "•••";
    private const int MaxValueLength = 500;
    private const int MaxLabelLength = 256;

    private readonly Dictionary<DbContext, Pending> _pending = [];

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && Capture(context) is { Count: > 0 } changes)
        {
            var owned = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
            _pending[context] = new Pending(changes, owned);
        }

        return result;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context && Capture(context) is { Count: > 0 } changes)
        {
            var owned = context.Database.CurrentTransaction is null ? context.Database.BeginTransaction() : null;
            _pending[context] = new Pending(changes, owned);
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && _pending.Remove(context, out var pending))
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync(InsertSql, Parameters(pending.Changes), cancellationToken);
                if (pending.Owned is { } transaction)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            }
            finally
            {
                if (pending.Owned is { } transaction)
                {
                    await transaction.DisposeAsync();
                }
            }
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is { } context && _pending.Remove(context, out var pending))
        {
            try
            {
                context.Database.ExecuteSqlRaw(InsertSql, Parameters(pending.Changes));
                pending.Owned?.Commit();
            }
            finally
            {
                pending.Owned?.Dispose();
            }
        }

        return result;
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default) => AbandonAsync(eventData.Context);

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Abandon(eventData.Context);

    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default) => AbandonAsync(eventData.Context);

    public override void SaveChangesCanceled(DbContextEventData eventData) => Abandon(eventData.Context);

    /// <summary>The save failed: nothing to record, and a transaction opened here is rolled back.</summary>
    private async Task AbandonAsync(DbContext? context)
    {
        if (context is not null && _pending.Remove(context, out var pending) && pending.Owned is { } transaction)
        {
            await transaction.DisposeAsync();
        }
    }

    private void Abandon(DbContext? context)
    {
        if (context is not null && _pending.Remove(context, out var pending))
        {
            pending.Owned?.Dispose();
        }
    }

    /// <summary>The changes about to be saved, worked out before EF accepts them (afterwards the old values are gone).</summary>
    private List<Change> Capture(DbContext context)
    {
        if (AuditScope.Suppressed)
        {
            return [];
        }

        context.ChangeTracker.DetectChanges();
        var changes = new List<Change>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted) || SkippedEntities.Contains(EntityName(entry.Metadata)))
            {
                continue;
            }

            var fields = Fields(entry);
            if (entry.State == EntityState.Modified && fields.Count == 0)
            {
                continue; // only bookkeeping changed
            }

            var action = entry.State switch
            {
                EntityState.Added => AuditActions.Create,
                EntityState.Deleted => AuditActions.Delete,
                _ => AuditActions.Update,
            };
            var (label, owner) = Describe(context, entry);
            changes.Add(new Change(entry, action, TenantOf(entry) ?? TenantOf(owner) ?? CurrentTenant(), label, JsonSerializer.Serialize(fields)));
        }

        return changes;
    }

    /// <summary>[field, before, after] for each field that changed; every set field of a new or deleted record.</summary>
    private static List<string?[]> Fields(EntityEntry entry)
    {
        var fields = new List<string?[]>();
        foreach (var property in entry.Properties)
        {
            var meta = property.Metadata;
            // Keys identify the record; computed columns and row versions are the database's own bookkeeping.
            if (meta.IsPrimaryKey() || meta.IsShadowProperty() || meta.ValueGenerated is ValueGenerated.OnAddOrUpdate or ValueGenerated.OnUpdate
                || SkippedFields.Contains(meta.Name))
            {
                continue;
            }

            var (before, after) = entry.State switch
            {
                EntityState.Added => (null, property.CurrentValue),
                EntityState.Deleted => (property.OriginalValue, null),
                _ => property.IsModified ? (property.OriginalValue, property.CurrentValue) : (null, null),
            };
            if (Equals(before, after) || (entry.State == EntityState.Added && after is false or 0 or 0L or 0m or 0d))
            {
                continue; // unchanged, or a new record's empty default
            }

            if (meta.GetColumnType() == "jsonb" && JsonChanges(meta.Name, before as string, after as string) is { } json)
            {
                fields.AddRange(json);
                continue;
            }

            fields.Add(SecretFields.Contains(meta.Name)
                ? [meta.Name, before is null ? null : Hidden, after is null ? null : Hidden]
                : [meta.Name, Text(before), Text(after)]);
        }

        return fields;
    }

    /// <summary>
    /// A JSON column compared by meaning, not by text (PostgreSQL rewrites jsonb): nothing when equal, one entry per
    /// changed key of an object ("GovernorateFees.IQ-BA"), otherwise null to record the whole value.
    /// </summary>
    private static List<string?[]>? JsonChanges(string field, string? before, string? after)
    {
        try
        {
            using var old = JsonDocument.Parse(before ?? "null");
            using var now = JsonDocument.Parse(after ?? "null");
            if (JsonElement.DeepEquals(old.RootElement, now.RootElement))
            {
                return [];
            }

            if (old.RootElement.ValueKind != JsonValueKind.Object || now.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var was = old.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
            var @is = now.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
            return was.Keys.Union(@is.Keys)
                .Where(key => !(was.TryGetValue(key, out var a) && @is.TryGetValue(key, out var b) && JsonElement.DeepEquals(a, b)))
                .Order(StringComparer.Ordinal)
                .Select(key => new[] { $"{field}.{key}", JsonText(was, key), JsonText(@is, key) })
                .ToList<string?[]>();
        }
        catch (JsonException)
        {
            return null;
        }

        static string? JsonText(Dictionary<string, JsonElement> values, string key) =>
            !values.TryGetValue(key, out var value) || value.ValueKind == JsonValueKind.Null ? null
            : value.ValueKind == JsonValueKind.String ? value.GetString()
            : Truncate(value.GetRawText(), MaxValueLength);
    }

    /// <summary>
    /// The record's own label, or, for a link between records (a user's role or claim), the labels of what it links
    /// (found among the records this save already tracks, so no extra queries).
    /// </summary>
    private static (string? Label, EntityEntry? Owner) Describe(DbContext context, EntityEntry entry)
    {
        if (LabelOf(entry) is { } own)
        {
            return (own, null);
        }

        var linked = new List<(string Label, EntityEntry Principal)>();
        foreach (var foreignKey in entry.Metadata.GetForeignKeys())
        {
            var values = foreignKey.Properties.Select(p => entry.State == EntityState.Deleted ? entry.Property(p.Name).OriginalValue : entry.Property(p.Name).CurrentValue).ToArray();
            if (values.Any(v => v is null))
            {
                continue;
            }

            var principalKey = foreignKey.PrincipalKey.Properties;
            var principal = context.ChangeTracker.Entries().FirstOrDefault(e =>
                e.Metadata == foreignKey.PrincipalEntityType
                && principalKey.Select((p, i) => Equals(e.Property(p.Name).CurrentValue, values[i])).All(match => match));
            if (principal is not null && LabelOf(principal) is { } label)
            {
                linked.Add((label, principal));
            }
        }

        // The record that belongs to a company (the user, not the role) comes first and decides the company.
        linked = [.. linked.OrderBy(l => TenantOf(l.Principal) is null)];
        return linked.Count == 0 ? (null, null) : (Truncate(string.Join(" · ", linked.Select(l => l.Label)), MaxLabelLength), linked[0].Principal);
    }

    private static string? LabelOf(EntityEntry entry)
    {
        foreach (var name in LabelFields)
        {
            if (entry.Metadata.FindProperty(name) is { } property && entry.Property(property.Name).CurrentValue is string { Length: > 0 } value)
            {
                return Truncate(value, MaxLabelLength);
            }
        }

        return null;
    }

    private static Guid? TenantOf(EntityEntry? entry) =>
        entry?.Metadata.FindProperty("TenantId") is { } property && entry.Property(property.Name).CurrentValue is Guid id && id != Guid.Empty ? id : null;

    private Guid CurrentTenant()
    {
        try
        {
            return tenant.TenantId;
        }
        catch (InvalidOperationException)
        {
            return Tenants.Default; // a signed-in account without a company claim; the change itself is refused elsewhere
        }
    }

    private static string EntityName(IEntityType type)
    {
        var name = type.ClrType.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? name : name[..tick];
    }

    private static string KeyOf(EntityEntry entry) =>
        string.Join(":", entry.Metadata.FindPrimaryKey()?.Properties.Select(p => Text(entry.Property(p.Name).CurrentValue)) ?? []);

    private static string? Text(object? value) => value switch
    {
        null => null,
        string s => Truncate(s, MaxValueLength),
        bool b => b ? "true" : "false",
        decimal d => d.ToString("0.############", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Truncate(value.ToString() ?? "", MaxValueLength),
    };

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";

    private const string InsertSql = """
        INSERT INTO audit.entries (id, tenant_id, occurred_at, actor, actor_id, actor_ip, action, entity, entity_id, label, changes)
        SELECT * FROM unnest(@ids, @tenants, @times, @actors, @actor_ids, @actor_ips, @actions, @entities, @entity_ids, @labels, @changes)
        """;

    /// <summary>One insert for the whole save, whatever the number of records (array parameters, unnested).</summary>
    private NpgsqlParameter[] Parameters(List<Change> changes)
    {
        var now = clock.GetUtcNow();
        var (actor, actorId, ip) = Who();
        var n = changes.Count;
        return
        [
            new("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = changes.Select(_ => Guid.CreateVersion7(now)).ToArray() },
            new("tenants", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = changes.Select(c => c.TenantId).ToArray() },
            new("times", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = Enumerable.Repeat(now, n).ToArray() },
            new("actors", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = Enumerable.Repeat(actor, n).ToArray() },
            new("actor_ids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = Enumerable.Repeat(actorId, n).ToArray() },
            new("actor_ips", NpgsqlDbType.Array | NpgsqlDbType.Inet) { Value = Enumerable.Repeat(ip, n).ToArray() },
            new("actions", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = changes.Select(c => c.Action).ToArray() },
            new("entities", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = changes.Select(c => EntityName(c.Entry.Metadata)).ToArray() },
            // Read now: keys the database generates (identity columns) are known only after the save.
            new("entity_ids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = changes.Select(c => KeyOf(c.Entry)).ToArray() },
            new("labels", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = changes.Select(c => c.Label).ToArray() },
            new("changes", NpgsqlDbType.Array | NpgsqlDbType.Jsonb) { Value = changes.Select(c => c.Fields).ToArray() },
        ];
    }

    /// <summary>The signed-in user; otherwise the background actor in <see cref="AuditScope"/>, or anonymous on a public page.</summary>
    private (string Actor, string? ActorId, IPAddress? Ip) Who()
    {
        var context = http.HttpContext;
        var ip = context?.Connection.RemoteIpAddress is { IsIPv4MappedToIPv6: true } mapped ? mapped.MapToIPv4() : context?.Connection.RemoteIpAddress;
        if (context?.User is { Identity.IsAuthenticated: true } user)
        {
            var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
            return (user.Identity?.Name ?? id ?? AuditActors.Anonymous, id, ip);
        }

        return (AuditScope.Actor ?? (context is null ? AuditActors.System : AuditActors.Anonymous), null, ip);
    }

    private sealed record Change(EntityEntry Entry, string Action, Guid TenantId, string? Label, string Fields);

    private sealed record Pending(List<Change> Changes, IDbContextTransaction? Owned);
}
