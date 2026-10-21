namespace Logistico.SharedKernel;

/// <summary>Resolves the tenant (logistics company) the current request or job works for.</summary>
public interface ITenantContext
{
    Guid TenantId { get; }
}

public static class Tenants
{
    /// <summary>The tenant used until multi-company onboarding exists.</summary>
    public static readonly Guid Default = new("01990000-0000-7000-8000-000000000001");
}

/// <summary>
/// Runs background work (automation sweeps, imports) for one tenant: <c>using (TenantScope.Begin(id)) { … }</c>.
/// Tenant contexts honour the override before looking at the signed-in user.
/// </summary>
public static class TenantScope
{
    private static readonly AsyncLocal<Guid?> Current = new();

    public static Guid? Override => Current.Value;

    public static IDisposable Begin(Guid tenantId)
    {
        var previous = Current.Value;
        Current.Value = tenantId;
        return new Restore(previous);
    }

    private sealed class Restore(Guid? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
