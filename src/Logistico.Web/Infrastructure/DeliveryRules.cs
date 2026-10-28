using Logistico.SharedKernel;
using Logistico.SharedKernel.Mobile;
using Logistico.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Web.Infrastructure;

/// <summary>Reads the current company's delivery rules from its mobile app settings.</summary>
internal sealed class DeliveryRules(ApplicationDbContext db, ITenantContext tenant) : IDeliveryRules
{
    public async Task<bool> SignatureRequiredAsync(CancellationToken ct) =>
        (await db.MobileAppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenant.TenantId, ct))?.Read().RequireSignature == true;
}
