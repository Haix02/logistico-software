namespace Logistico.SharedKernel;

/// <summary>
/// Tells the audit trail who is acting when there is no signed-in user, or that a stretch of work is not worth
/// recording: <c>using (AuditScope.As("automation")) { … }</c>, <c>using (AuditScope.Suppress()) { seed… }</c>.
/// </summary>
public static class AuditScope
{
    private static readonly AsyncLocal<State?> Current = new();

    /// <summary>True inside <see cref="Suppress"/>: changes are not recorded (demo data, reference data).</summary>
    public static bool Suppressed => Current.Value?.Suppressed == true;

    /// <summary>The background actor named by <see cref="As"/>, used when no user is signed in.</summary>
    public static string? Actor => Current.Value?.Actor;

    public static IDisposable As(string actor) => Push(new State(actor, Suppressed));

    public static IDisposable Suppress() => Push(new State(Actor, true));

    private static Restore Push(State state)
    {
        var previous = Current.Value;
        Current.Value = state;
        return new Restore(previous);
    }

    private sealed record State(string? Actor, bool Suppressed);

    private sealed class Restore(State? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
