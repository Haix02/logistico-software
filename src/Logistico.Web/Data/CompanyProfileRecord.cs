using Logistico.SharedKernel.Setup;

namespace Logistico.Web.Data;

/// <summary>A company's profile and its getting-started choices (steps it skipped), one row per company.</summary>
public class CompanyProfileRecord
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Governorate { get; set; }
    public string? Address { get; set; }

    /// <summary>Getting-started steps the company said are not for it.</summary>
    public List<string> SkippedSteps { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public uint Version { get; set; }

    /// <summary>The profile has what customers and staff need to recognise and reach the company.</summary>
    public bool IsSet => Name.Length > 0 && Phone is not null;

    public CompanyProfile Read() => new() { Name = Name, Phone = Phone, Governorate = Governorate, Address = Address };

    public void Write(CompanyProfile profile, string? by, DateTimeOffset now)
    {
        Name = profile.Name.Trim();
        Phone = Clean(profile.Phone);
        Governorate = Clean(profile.Governorate)?.ToUpperInvariant();
        Address = Clean(profile.Address);
        UpdatedAt = now;
        UpdatedBy = by;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
