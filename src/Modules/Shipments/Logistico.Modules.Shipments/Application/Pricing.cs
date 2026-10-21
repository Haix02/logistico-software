using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Application;

/// <summary>Loads a company's price list (the standard prices until it saves its own), once per request.</summary>
internal sealed class Pricing(ShipmentsDbContext db, TimeProvider clock)
{
    private readonly Dictionary<Guid, RateCard> cache = [];

    public async Task<RateCard> GetAsync(Guid tenantId, CancellationToken ct)
    {
        if (cache.TryGetValue(tenantId, out var cached))
        {
            return cached;
        }

        var settings = await db.RateCards.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(r => r.TenantId == tenantId, ct);
        var card = settings?.ToRateCard() ?? new RateCard();
        cache[tenantId] = card;
        return card;
    }

    /// <summary>The tracked row for editing; created with the standard prices on first save.</summary>
    public async Task<RateCardSettings> GetForUpdateAsync(Guid tenantId, CancellationToken ct)
    {
        var settings = await db.RateCards.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TenantId == tenantId, ct);
        if (settings is null)
        {
            settings = RateCardSettings.Create(tenantId, new RateCard(), null, clock.GetUtcNow());
            db.RateCards.Add(settings);
        }

        cache.Remove(tenantId);
        return settings;
    }
}
