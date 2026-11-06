namespace Logistico.Modules.Shipments.Domain;

/// <summary>Which country a company delivers in. Missing row means Iraq, so existing companies stay as they are.</summary>
internal sealed class OperatingCountry
{
    public Guid TenantId { get; set; }

    public string Country { get; set; } = "IQ";
}

/// <summary>A region a company outside Iraq delivers to. Iraqi companies use the built-in governorates instead.</summary>
internal sealed class ServiceArea
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Code { get; set; } = "";

    public string NameEn { get; set; } = "";

    public string NameAr { get; set; } = "";

    public double Latitude { get; set; }

    public double Longitude { get; set; }
}
