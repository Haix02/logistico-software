using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Application;

/// <summary>Loads a company's automation rules (defaults when it never changed them), once per request.</summary>
internal sealed class AutomationPolicies(ShipmentsDbContext db, TimeProvider clock)
{
    private readonly Dictionary<Guid, AutomationPolicy> cache = [];

    public async Task<AutomationPolicy> GetAsync(Guid tenantId, CancellationToken ct)
    {
        if (cache.TryGetValue(tenantId, out var cached))
        {
            return cached;
        }

        var settings = await db.AutomationSettings.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        var policy = settings?.ToPolicy() ?? new AutomationPolicy();
        cache[tenantId] = policy;
        return policy;
    }

    /// <summary>The tracked settings row for editing; created with defaults on first use.</summary>
    public async Task<AutomationSettings> GetForUpdateAsync(Guid tenantId, CancellationToken ct)
    {
        var settings = await db.AutomationSettings.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (settings is null)
        {
            settings = AutomationSettings.Default(tenantId, clock.GetUtcNow());
            db.AutomationSettings.Add(settings);
        }

        cache.Remove(tenantId);
        return settings;
    }
}
