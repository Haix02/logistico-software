using Logistico.SharedKernel.Identity;

namespace Logistico.Web.Client.Layout;

/// <summary>
/// One page in the side menu and command palette. <see cref="Shortcut"/> is the key after "g"; <see cref="Policy"/>
/// is the staff policy needed to use the page (pages that only change data are hidden from read-only roles).
/// </summary>
public sealed record NavEntry(string Href, string TextKey, string Icon, string? Shortcut = null, string? Keywords = null, bool Exact = false, string Policy = StaffPolicies.Staff);

public sealed record NavGroup(string TitleKey, IReadOnlyList<NavEntry> Items);

/// <summary>The application's pages, grouped as in the side menu.</summary>
public static class AppNavigation
{
    public static readonly IReadOnlyList<NavGroup> Groups =
    [
        new("Nav_Operations",
        [
            new("", "Nav_Dashboard", "dashboard", "d", "home overview لوحة", Exact: true),
            new("network", "Nav_Network", "map", "i", "iraq map hubs خريطة العراق شبكة"),
            new("shipments", "Nav_Shipments", "th", "s", "orders parcels list الشحنات الطلبات", Exact: true),
            new("orders/new", "Nav_NewOrder", "add", null, "instagram new order طلب جديد", Policy: StaffPolicies.Operate),
            new("shipments/new", "Nav_NewInternational", "globe", null, "freight international شحنة دولية", Policy: StaffPolicies.Operate),
        ]),
        new("Nav_IntelligenceSection",
        [
            new("insights", "Nav_Insights", "predictive-analysis", "e", "insights demand intelligence analytics data open data customers products رؤى ذكاء الطلب تحليلات بيانات زبائن منتجات"),
        ]),
        new("Nav_HubsSection",
        [
            new("warehouse", "Nav_Warehouse", "barcode", "w", "scan station hub مخزن مسح", Policy: StaffPolicies.Operate),
            new("warehouses", "Nav_Warehouses", "office", "h", "warehouses branches hubs coverage provinces مخازن فروع تغطية محافظات"),
            new("transfers", "Nav_Transfers", "drive-time", "t", "line haul truck شاحنة نقل"),
            new("returns", "Nav_Returns", "undo", "r", "returns rto مرتجعات"),
            new("shipments/labels", "Nav_PrintLabels", "print", null, "labels awb print بوليصة طباعة", Policy: StaffPolicies.Operate),
        ]),
        new("Nav_CareSection",
        [
            new("confirmations", "Nav_Confirmations", "phone-call", "k", "confirm orders call customer cod تأكيد الطلبات اتصال", Policy: StaffPolicies.Care),
            new("requests", "Nav_Requests", "inbox", "q", "requests customer service طلبات خدمة"),
            new("automation", "Nav_Automation", "automatic-updates", "a", "alerts rules automation تنبيهات قواعد أتمتة"),
            new("track", "Nav_Track", "path-search", null, "public tracking تتبع"),
        ]),
        new("Nav_BusinessSection",
        [
            new("merchants", "Nav_Sellers", "shop", "m", "sellers instagram shops متاجر"),
            new("finance", "Nav_Finance", "bank-account", "f", "cod settlements money مالية تسوية", Policy: StaffPolicies.Finance),
            new("pricing", "Nav_Pricing", "tag", "p", "prices rates fees delivery cost أسعار أجور التوصيل"),
            new("fleet", "Nav_Fleet", "people", "c", "couriers vehicles مندوبين أسطول"),
            new("customs", "Nav_Customs", "endorsed", "u", "asycuda customs جمارك"),
        ]),
        new("Nav_AdminSection",
        [
            new("team", "Nav_Team", "key", "x", "team users staff roles access permissions الفريق المستخدمين الصلاحيات الأدوار", Policy: StaffPolicies.Administer),
        ]),
    ];

    public static IEnumerable<NavEntry> All => Groups.SelectMany(g => g.Items);

    public static NavEntry? ByShortcut(string key) => All.FirstOrDefault(e => e.Shortcut == key);

    /// <summary>The groups with only the pages the user may use (empty groups dropped).</summary>
    public static IEnumerable<NavGroup> For(System.Security.Claims.ClaimsPrincipal user) =>
        Groups.Select(g => g with { Items = g.Items.Where(e => Ui.Access.Can(user, e.Policy)).ToList() }).Where(g => g.Items.Count > 0);
}
