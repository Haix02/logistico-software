namespace Logistico.Modules.Shipments.Domain;

/// <summary>
/// A note staff leave on an order for each other (call desk, hub, finance): who wrote it and when. Comments are only
/// added, never edited, so the conversation stays as it happened.
/// </summary>
internal sealed class ShipmentComment
{
    public const int MaxLength = 1000;

    private ShipmentComment()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ShipmentId { get; private set; }
    public string Author { get; private set; } = "";
    public string? AuthorName { get; private set; }
    public string Text { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }

    public static ShipmentComment Write(Guid tenantId, Guid shipmentId, string author, string? authorName, string text, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        TenantId = tenantId,
        ShipmentId = shipmentId,
        Author = author,
        AuthorName = string.IsNullOrWhiteSpace(authorName) ? null : authorName.Trim(),
        Text = text.Trim(),
        CreatedAt = now,
    };
}
