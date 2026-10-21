using Logistico.Modules.Merchants.Contracts;
using Logistico.Modules.Merchants.Domain;
using Logistico.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Merchants.Infrastructure;

/// <summary>Development-only demo sellers: Iraqi Instagram shops.</summary>
internal sealed class MerchantsDemoSeeder(MerchantsDbContext db, ITenantContext tenant, TimeProvider clock)
{
    private static readonly (string Name, string Handle, string Contact, string Phone, string Gov)[] Shops =
    [
        ("بوتيك لمار", "lamar.boutique", "لمار أحمد", "07701234501", "IQ-BG"),
        ("Baghdad Kicks", "baghdad_kicks", "Ali Hassan", "07801234502", "IQ-BG"),
        ("زهرة للتجميل", "zahra.cosmetics", "زهراء علي", "07501234503", "IQ-BA"),
        ("Erbil Gadgets", "erbil.gadgets", "Karwan Aziz", "07501234504", "IQ-AR"),
        ("عطور الفرات", "alfurat.perfumes", "حيدر كاظم", "07811234505", "IQ-NA"),
        ("Little Steps Kids", "littlesteps.iq", "Noor Salim", "07711234506", "IQ-BG"),
        ("مطبخ ماما", "mama.kitchen.iq", "أم يوسف", "07901234507", "IQ-KA"),
        ("Mosul Handmade", "mosul.handmade", "Sara Younis", "07721234508", "IQ-NI"),
        ("دار الأناقة", "dar.alanaqa", "مريم جاسم", "07831234509", "IQ-BB"),
        ("Phone Case Hub", "phonecase.hub", "Mustafa Raad", "07741234510", "IQ-BG"),
        ("كتب ومكتبة الرصيف", "alrasif.books", "عمر فاضل", "07851234511", "IQ-BG"),
        ("Suli Fashion", "suli.fashion", "Rozh Omer", "07501234512", "IQ-SU"),
    ];

    public async Task SeedAsync(CancellationToken ct)
    {
        if (await db.Merchants.AnyAsync(ct))
        {
            return;
        }

        var now = clock.GetUtcNow().AddDays(-120);
        foreach (var (name, handle, contact, phone, gov) in Shops)
        {
            now = now.AddHours(7);
            db.Merchants.Add(Merchant.Create(tenant.TenantId, new MerchantRequest
            {
                Name = name,
                InstagramHandle = handle,
                ContactName = contact,
                Phone = phone,
                Governorate = gov,
                Address = gov == "IQ-BG" ? "الكرادة، شارع 62" : null,
            }, now));
        }

        await db.SaveChangesAsync(ct);
    }
}
