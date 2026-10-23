namespace Logistico.Modules.Audit.Contracts;

public static class AuditApiRoutes
{
    public const string Audit = "api/v1/audit";
}

/// <summary>What happened to a record.</summary>
public static class AuditActions
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";
}

/// <summary>Who did it when nobody was signed in.</summary>
public static class AuditActors
{
    /// <summary>Background work without a name of its own.</summary>
    public const string System = "system";

    /// <summary>Someone not signed in: a customer on the tracking page, or a sign-in attempt.</summary>
    public const string Anonymous = "anonymous";
}

/// <summary>
/// One recorded change: who, when, from where, to which record (<see cref="Entity"/>, <see cref="EntityId"/> and a
/// readable <see cref="Label"/> such as a tracking number) and each changed field with its value before and after.
/// </summary>
public sealed record AuditEntryItem(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Actor,
    string? ActorIp,
    string Action,
    string Entity,
    string? EntityId,
    string? Label,
    IReadOnlyList<AuditChange> Changes);

/// <summary>A field before and after the change (null = empty). Secrets are recorded as changed without their value.</summary>
public sealed record AuditChange(string Field, string? Old, string? New);
