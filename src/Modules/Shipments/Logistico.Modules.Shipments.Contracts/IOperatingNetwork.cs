namespace Logistico.Modules.Shipments.Contracts;

/// <summary>The country a company operates in. The shipments module owns it, because routing depends on it.</summary>
public interface IOperatingNetwork
{
    Task<string> CountryAsync(Guid tenantId, CancellationToken cancellationToken);

    Task SetCountryAsync(Guid tenantId, string country, CancellationToken cancellationToken);
}
