using System.Net;

namespace Logistico.Modules.Audit.Infrastructure;

/// <summary>
/// One row of <c>audit.entries</c> (docs/04-database-design.md §4.6). Rows are only ever inserted, by
/// <see cref="AuditInterceptor"/>; the database refuses updates and deletes.
/// </summary>
internal sealed class AuditEntry
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>The signed-in user's name, or a background actor such as "automation".</summary>
    public string Actor { get; init; } = "";

    public string? ActorId { get; init; }
    public IPAddress? ActorIp { get; init; }

    /// <summary>create, update or delete (see Contracts.AuditActions).</summary>
    public string Action { get; init; } = "";

    /// <summary>The record type, e.g. Shipment, RateCardSettings, ApplicationUser.</summary>
    public string Entity { get; init; } = "";

    public string? EntityId { get; init; }

    /// <summary>How people know the record: tracking number, settlement number, e-mail, name.</summary>
    public string? Label { get; init; }

    /// <summary>JSON array of [field, before, after], in the record's field order.</summary>
    public string Changes { get; init; } = "[]";
}
