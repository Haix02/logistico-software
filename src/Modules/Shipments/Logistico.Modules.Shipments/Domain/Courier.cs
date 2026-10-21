using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Iraq;

namespace Logistico.Modules.Shipments.Domain;

/// <summary>A delivery courier and assigned vehicle operating in an Iraqi governorate.</summary>
internal sealed class Courier
{
    private Courier()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Phone { get; private set; } = "";
    public string Governorate { get; private set; } = "IQ-BG";
    public VehicleType VehicleType { get; private set; }
    public string? VehiclePlate { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public string DisplayLabel => $"{Name} ({Code})";

    public static Courier Create(Guid tenantId, CourierRequest request, DateTimeOffset now)
    {
        var courier = new Courier
        {
            Id = Guid.CreateVersion7(now),
            TenantId = tenantId,
            CreatedAt = now,
        };
        courier.Apply(request, now);
        return courier;
    }

    public void Update(CourierRequest request, DateTimeOffset now) => Apply(request, now);

    private void Apply(CourierRequest request, DateTimeOffset now)
    {
        var phone = IraqPhone.Normalize(request.Phone)
            ?? throw new DomainException("courier.invalid_phone", "A valid Iraqi mobile number is required.");
        if (!IraqGovernorates.IsKnown(request.Governorate))
        {
            throw new DomainException("courier.invalid_governorate", "Unknown governorate.");
        }

        Code = NormalizeCode(request.Code);
        Name = request.Name.Trim();
        Phone = phone;
        Governorate = request.Governorate.Trim().ToUpperInvariant();
        VehicleType = request.VehicleType;
        VehiclePlate = Clean(request.VehiclePlate)?.ToUpperInvariant();
        Notes = Clean(request.Notes);
        IsActive = request.IsActive;
        UpdatedAt = now;
    }

    public static string NormalizeCode(string code) => code.Trim();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

