namespace Logistico.Web.Data;

/// <summary>
/// A key of a request that may be retried, and the answer it got (see <c>Infrastructure/Idempotency.cs</c>). Written with
/// plain SQL by <c>IdempotencyStore</c>; mapped here so migrations create the table.
/// </summary>
public sealed class IdempotentRequestRecord
{
    public Guid TenantId { get; set; }

    /// <summary>Who sent it (user id): keys are per person.</summary>
    public string Principal { get; set; } = "";

    public string Key { get; set; } = "";

    /// <summary>SHA-256 of method, path, query and body: the same key with another request is refused.</summary>
    public string Fingerprint { get; set; } = "";

    /// <summary>0 while the first attempt runs; then the status code of its answer.</summary>
    public int StatusCode { get; set; }

    public string? ContentType { get; set; }
    public string? Location { get; set; }
    public string? ETag { get; set; }
    public byte[]? Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
