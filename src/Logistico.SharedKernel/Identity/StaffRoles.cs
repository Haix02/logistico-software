namespace Logistico.SharedKernel.Identity;

/// <summary>
/// The jobs a staff member can have. One role per person keeps it simple for a delivery company:
/// <list type="bullet">
/// <item><see cref="Admin"/>: everything, including company settings (prices, automation rules, warehouses) and the team.</item>
/// <item><see cref="Operations"/>: the daily work — orders, hub scans, trucks, couriers, returns, customer requests, customs.</item>
/// <item><see cref="CustomerSupport"/>: the call desk — confirmation calls, customer requests and messages; reads everything else.</item>
/// <item><see cref="Finance"/>: cash-on-delivery settlements, seller payouts and delivery prices; reads everything else.</item>
/// <item><see cref="Viewer"/>: reads dashboards, lists and insights, changes nothing.</item>
/// </list>
/// A signed-in account without a role (for example one that registered itself) sees no company data until an
/// administrator gives it one.
/// </summary>
public static class StaffRoles
{
    public const string Admin = "Admin";
    public const string Operations = "Operations";
    public const string CustomerSupport = "CustomerSupport";
    public const string Finance = "Finance";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = [Admin, Operations, CustomerSupport, Finance, Viewer];

    public static bool IsKnown(string? role) => role is not null && All.Contains(role, StringComparer.Ordinal);
}

/// <summary>
/// Authorization policies shared by the API (enforcement) and the browser (hiding what a role cannot do).
/// Module endpoints get <see cref="Staff"/> for reading and <see cref="Operate"/> for changes by default; endpoints
/// for the call desk ask for <see cref="Care"/>, and those that touch money or settings for <see cref="Finance"/> or
/// <see cref="Administer"/>.
/// </summary>
public static class StaffPolicies
{
    public const string Staff = "staff";
    public const string Operate = "operate";

    /// <summary>Talking to customers: confirmation calls, customer requests and messages, customer lookup.</summary>
    public const string Care = "care";
    public const string Finance = "finance";
    public const string Administer = "administer";

    public static readonly IReadOnlyList<string> All = [Staff, Operate, Care, Finance, Administer];

    /// <summary>The roles that satisfy a policy.</summary>
    public static IReadOnlyList<string> RolesFor(string policy) => policy switch
    {
        Staff => StaffRoles.All,
        Operate => [StaffRoles.Admin, StaffRoles.Operations],
        Care => [StaffRoles.Admin, StaffRoles.Operations, StaffRoles.CustomerSupport],
        Finance => [StaffRoles.Admin, StaffRoles.Finance],
        Administer => [StaffRoles.Admin],
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown staff policy."),
    };
}
