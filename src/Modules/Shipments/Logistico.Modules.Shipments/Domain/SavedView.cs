using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Domain;

/// <summary>
/// A person's named list view: the filters, sort and columns of a list, kept as the address query that shows them
/// ("?status=OutForDelivery&amp;governorate=IQ-BA&amp;cols=Status,Courier,Cod"). Only its owner sees it.
/// </summary>
internal sealed class SavedView
{
    public const int MaxPerList = 20;

    private SavedView()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string UserId { get; private set; } = "";
    public string List { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Query { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }

    public static SavedView Create(Guid tenantId, string userId, string list, string name, string query, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        TenantId = tenantId,
        UserId = userId,
        List = list,
        Name = name.Trim(),
        Query = query,
        CreatedAt = now,
    };

    /// <summary>Saving again under the same name replaces what the view shows.</summary>
    public void Replace(string query) => Query = query;
}
