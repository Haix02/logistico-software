using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Iraq;

namespace Logistico.Modules.Shipments.Domain;

/// <summary>
/// A company's warehouse in one governorate: a sorting hub, a branch or a fulfilment store. Its routing tag ("NJF") is
/// printed on labels and identifies it for ever (code "HUB-NJF"); the governorates it covers decide which parcels it
/// delivers and collects.
/// </summary>
internal sealed class Warehouse
{
    private Warehouse()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = "";
    public string RoutingTag { get; private set; } = "";
    public string NameAr { get; private set; } = "";
    public string NameEn { get; private set; } = "";
    public HubKind Kind { get; private set; }
    public string Governorate { get; private set; } = "IQ-BG";
    public string? Address { get; private set; }
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public string? Phone { get; private set; }
    public string? ManagerName { get; private set; }
    public string? OpeningHours { get; private set; }
    public int? ShelfCapacity { get; private set; }
    public List<string> Covers { get; private set; } = [];
    public bool IsActive { get; private set; } = true;
    public bool IsPrimary { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }
    public uint Version { get; private set; }

    public static Warehouse Create(Guid tenantId, WarehouseRequest request, string? by, DateTimeOffset now)
    {
        var tag = NormalizeTag(request.RoutingTag);
        var warehouse = new Warehouse
        {
            Id = Guid.CreateVersion7(now),
            TenantId = tenantId,
            RoutingTag = tag,
            Code = IraqHubs.CodeFor(tag),
            CreatedAt = now,
        };
        warehouse.Apply(request, by, now);
        return warehouse;
    }

    /// <summary>The standard hubs a new company starts with.</summary>
    public static Warehouse FromStandard(Guid tenantId, HubInfo hub, bool primary, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        TenantId = tenantId,
        Code = hub.Code,
        RoutingTag = hub.RoutingTag,
        NameAr = hub.ArabicName,
        NameEn = hub.EnglishName,
        Kind = hub.Kind,
        Governorate = hub.Governorate,
        Latitude = hub.Latitude,
        Longitude = hub.Longitude,
        Covers = [.. hub.Covers],
        IsPrimary = primary,
        CreatedAt = now,
        UpdatedAt = now,
        UpdatedBy = "system",
    };

    /// <summary>Edits everything except the routing tag, which parcels, trucks and printed labels refer to.</summary>
    public void Update(WarehouseRequest request, string? by, DateTimeOffset now)
    {
        if (!string.Equals(NormalizeTag(request.RoutingTag), RoutingTag, StringComparison.Ordinal))
        {
            throw new DomainException("warehouse.tag_locked", "The routing tag cannot change once labels are printed with it.", RoutingTag);
        }

        Apply(request, by, now);
    }

    /// <summary>Hands governorates over to another warehouse (it now covers them).</summary>
    public void GiveUp(IReadOnlyCollection<string> governorates, DateTimeOffset now)
    {
        if (Covers.RemoveAll(g => governorates.Contains(g, StringComparer.OrdinalIgnoreCase)) > 0)
        {
            UpdatedAt = now;
        }
    }

    public void MakePrimary(bool primary, DateTimeOffset now)
    {
        if (IsPrimary != primary)
        {
            IsPrimary = primary;
            UpdatedAt = now;
        }
    }

    public HubInfo ToHubInfo() => new(Code, RoutingTag, Governorate, NameAr, NameEn)
    {
        Kind = Kind,
        Latitude = Latitude,
        Longitude = Longitude,
        Covers = IsActive ? [.. Covers] : [],
    };

    public static string NormalizeTag(string? tag) => (tag ?? "").Trim().ToUpperInvariant();

    private void Apply(WarehouseRequest request, string? by, DateTimeOffset now)
    {
        if (!IraqGovernorates.IsKnown(request.Governorate))
        {
            throw new DomainException("warehouse.invalid_governorate", "Unknown governorate.", request.Governorate);
        }

        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : IraqPhone.Normalize(request.Phone)
            ?? throw new DomainException("warehouse.invalid_phone", "A valid Iraqi phone number is required.");
        if (!request.IsActive && request.IsPrimary)
        {
            throw new DomainException("warehouse.primary_inactive", "The main warehouse must stay active.");
        }

        NameAr = request.NameAr.Trim();
        NameEn = request.NameEn.Trim();
        Kind = request.Kind;
        Governorate = request.Governorate.Trim().ToUpperInvariant();
        Address = Clean(request.Address);
        Latitude = Math.Round(request.Latitude, 5);
        Longitude = Math.Round(request.Longitude, 5);
        Phone = phone;
        ManagerName = Clean(request.ManagerName);
        OpeningHours = Clean(request.OpeningHours);
        ShelfCapacity = request.ShelfCapacity is > 0 ? request.ShelfCapacity : null;
        Covers = request.IsActive
            ? [.. request.Covers.Select(c => c.Trim().ToUpperInvariant()).Where(IraqGovernorates.IsKnown).Distinct().Order(StringComparer.Ordinal)]
            : [];
        IsActive = request.IsActive;
        IsPrimary = request.IsPrimary;
        Notes = Clean(request.Notes);
        UpdatedAt = now;
        UpdatedBy = by;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
