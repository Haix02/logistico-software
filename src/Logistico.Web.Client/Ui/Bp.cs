namespace Logistico.Web.Client.Ui;

public enum Intent
{
    None,
    Primary,
    Success,
    Warning,
    Danger,
}

/// <summary>Helpers for composing Blueprint (bp6-*) class names.</summary>
public static class Bp
{
    public static string IntentClass(Intent intent) =>
        intent == Intent.None ? "" : "bp6-intent-" + intent.ToString().ToLowerInvariant();

    public static string Cls(params string?[] parts) =>
        string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    public static string? When(bool condition, string cls) => condition ? cls : null;
}
