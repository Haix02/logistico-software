using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Domain;

/// <summary>
/// One line of a hub's sort plan: the parcels it matches go on its shelves, in order (see <see cref="ShelfRuleRequest"/>).
/// Like a warehouse putaway rule, but for parcels: it matches what the parcel is waiting for, where it is going and whose
/// it is, and the shelves it lists overflow into each other when a capacity is set.
/// </summary>
internal sealed class ShelfRule
{
    private ShelfRule()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string HubCode { get; private set; } = "";
    public ShelfPurpose Purpose { get; private set; }
    public string? ToHubCode { get; private set; }
    public string? Governorate { get; private set; }
    public string? Area { get; private set; }

    /// <summary>The area folded for matching (letter variants, digits and marks; see <see cref="SearchText.Fold"/>).</summary>
    public string? AreaFolded { get; private set; }

    public Guid? MerchantId { get; private set; }
    public string? MerchantName { get; private set; }
    public List<string> Shelves { get; private set; } = [];
    public int? CapacityPerShelf { get; private set; }
    public string? Note { get; private set; }
    public int Specificity { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }
    public uint Version { get; private set; }

    public static ShelfRule Create(Guid tenantId, ShelfRuleRequest request, string? merchantName, string? by, DateTimeOffset now)
    {
        var rule = new ShelfRule
        {
            Id = Guid.CreateVersion7(now),
            TenantId = tenantId,
            HubCode = request.HubCode.Trim(),
            CreatedAt = now,
        };
        rule.Apply(request, merchantName, by, now);
        return rule;
    }

    /// <summary>Changes the rule; it stays at its hub (a rule for another hub is a new rule).</summary>
    public void Update(ShelfRuleRequest request, string? merchantName, string? by, DateTimeOffset now)
    {
        if (!string.Equals(request.HubCode.Trim(), HubCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("sort_plan.hub_locked", "A rule stays at its hub; add a new rule at the other hub.");
        }

        Apply(request, merchantName, by, now);
    }

    public bool Matches(in ShelfParcel parcel) =>
        (Purpose == ShelfPurpose.Any || Purpose == parcel.Purpose)
        && (ToHubCode is null || (parcel.Purpose == ShelfPurpose.Forward && ToHubCode == parcel.ToHubCode))
        && (Governorate is null || Governorate == parcel.Governorate)
        && (AreaFolded is null || parcel.AreaFolded.Contains(AreaFolded, StringComparison.Ordinal))
        && (MerchantId is null || MerchantId == parcel.MerchantId);

    public ShelfRuleItem ToItem() =>
        new(Id, HubCode, Purpose, ToHubCode, Governorate, Area, MerchantId, MerchantName, Shelves, CapacityPerShelf, Note, Specificity, UpdatedAt, UpdatedBy);

    private void Apply(ShelfRuleRequest request, string? merchantName, string? by, DateTimeOffset now)
    {
        Purpose = request.Purpose;
        ToHubCode = Clean(request.ToHubCode)?.ToUpperInvariant();
        Governorate = Clean(request.Governorate)?.ToUpperInvariant();
        Area = Clean(request.Area);
        AreaFolded = Area is null ? null : SearchText.Fold(Area);
        MerchantId = request.MerchantId;
        MerchantName = request.MerchantId is null ? null : merchantName;
        Shelves = [.. request.Shelves.Select(SortPlanRules.Shelf).OfType<string>().Distinct(StringComparer.Ordinal)];
        CapacityPerShelf = request.CapacityPerShelf;
        Note = Clean(request.Note);
        Specificity = SortPlanRules.Specificity(Purpose, ToHubCode, Governorate, Area, MerchantId);
        UpdatedAt = now;
        UpdatedBy = by;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>What the sort plan needs to know about a parcel being shelved at a hub.</summary>
internal readonly record struct ShelfParcel(ShelfPurpose Purpose, string? ToHubCode, string Governorate, string AreaFolded, Guid? MerchantId)
{
    /// <summary>
    /// A parcel at <paramref name="hubCode"/>: waiting for a truck when its target hub is elsewhere, a return waiting for
    /// the seller when it is back at the seller's hub, otherwise waiting for a courier here.
    /// </summary>
    public static ShelfParcel Of(Shipment shipment, string hubCode)
    {
        var purpose = shipment.TargetHubCode != hubCode ? ShelfPurpose.Forward
            : shipment.IsReturning ? ShelfPurpose.Return
            : ShelfPurpose.Deliver;
        return new(purpose, purpose == ShelfPurpose.Forward ? shipment.TargetHubCode : null, shipment.DestinationCode,
            SearchText.Fold(shipment.DestinationCity), shipment.MerchantId);
    }
}
