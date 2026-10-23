using System.Text.Json;
using Logistico.Modules.Audit.Contracts;
using Logistico.Modules.Audit.Infrastructure;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Audit.Endpoints;

internal static class AuditEndpoints
{
    /// <summary>Counting a large log exactly is slow and nobody pages that far: totals stop here.</summary>
    private const int MaxCounted = 10_000;

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/" + AuditApiRoutes.Audit, ListAsync)
            .WithTags("Audit")
            .WithSummary("Who changed what: the company's audit trail, newest first, optionally for one record or matching a person, label or record id.")
            .RequireAuthorization(StaffPolicies.Administer);
        return app;
    }

    private static async Task<Ok<PagedResult<AuditEntryItem>>> ListAsync(
        string? q,
        string? entity,
        string? entityId,
        int? page,
        int? pageSize,
        AuditDbContext db,
        ITenantContext tenant,
        CancellationToken ct)
    {
        var size = Math.Clamp(pageSize ?? 50, 1, 200);
        var number = Paging.Page(page);
        var tenantId = tenant.TenantId;
        var query = db.Entries.AsNoTracking().Where(e => e.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(entity))
        {
            query = query.Where(e => e.Entity == entity.Trim());
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            query = query.Where(e => e.EntityId == entityId.Trim());
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var text = q.Trim();
            var pattern = "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            query = query.Where(e => EF.Functions.ILike(e.Actor, pattern) || (e.Label != null && EF.Functions.ILike(e.Label, pattern)) || e.EntityId == text);
        }

        var total = await query.Take(MaxCounted).CountAsync(ct);
        var rows = await query
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Skip((number - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResult<AuditEntryItem>(rows.Select(ToItem).ToList(), total, number, size));
    }

    private static AuditEntryItem ToItem(AuditEntry e) => new(
        e.Id, e.OccurredAt, e.Actor, e.ActorIp?.ToString(), e.Action, e.Entity, e.EntityId, e.Label,
        (JsonSerializer.Deserialize<string?[][]>(e.Changes) ?? [])
            .Where(c => c.Length == 3 && c[0] is not null)
            .Select(c => new AuditChange(c[0]!, c[1], c[2]))
            .ToList());
}
