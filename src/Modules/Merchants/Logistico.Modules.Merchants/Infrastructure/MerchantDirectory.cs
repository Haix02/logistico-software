using Logistico.Modules.Merchants.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Merchants.Infrastructure;

internal sealed class MerchantDirectory(MerchantsDbContext db) : IMerchantDirectory
{
    public Task<MerchantLookupItem?> FindActiveAsync(Guid id, CancellationToken cancellationToken) =>
        db.Merchants.AsNoTracking()
            .Where(m => m.Id == id && m.IsActive)
            .Select(m => new MerchantLookupItem(m.Id, m.Name, m.InstagramHandle, m.Governorate, m.Phone))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<MerchantLookupItem>> ListActiveAsync(CancellationToken cancellationToken) =>
        await db.Merchants.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.Name)
            .Select(m => new MerchantLookupItem(m.Id, m.Name, m.InstagramHandle, m.Governorate, m.Phone))
            .ToListAsync(cancellationToken);
}
