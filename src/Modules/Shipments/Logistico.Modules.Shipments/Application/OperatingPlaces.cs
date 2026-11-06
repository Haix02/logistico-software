using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Geo;
using Logistico.SharedKernel.Iraq;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Application;

/// <summary>The areas a company delivers to: Iraq's governorates, or the regions it created for another country.</summary>
internal sealed class OperatingPlaces(ShipmentsDbContext db) : IOperatingNetwork
{
    public async Task<string> CountryAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var country = await db.OperatingCountries.AsNoTracking()
            .Where(row => row.TenantId == tenantId)
            .Select(row => row.Country)
            .FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrEmpty(country) ? Countries.Iraq : country;
    }

    public async Task SetCountryAsync(Guid tenantId, string country, CancellationToken cancellationToken)
    {
        var code = Countries.Normalize(country);
        if (!Countries.IsKnown(code))
        {
            throw new DomainException("company.unknown_country", "Choose a country.", country);
        }

        var row = await db.OperatingCountries.FirstOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);
        if (row is null)
        {
            db.OperatingCountries.Add(new OperatingCountry { TenantId = tenantId, Country = code });
        }
        else if (!string.Equals(row.Country, code, StringComparison.Ordinal))
        {
            row.Country = code;
        }
        else
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlaceInfo>> PlacesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (Countries.IsIraq(await CountryAsync(tenantId, cancellationToken)))
        {
            return IraqGovernorates.All
                .Select(g => new PlaceInfo(g.Code, g.NameEn, g.NameAr, g.Latitude, g.Longitude))
                .ToList();
        }

        return await db.ServiceAreas.AsNoTracking()
            .Where(area => area.TenantId == tenantId)
            .OrderBy(area => area.NameEn)
            .Select(area => new PlaceInfo(area.Code, area.NameEn, area.NameAr, area.Latitude, area.Longitude))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Refuses a warehouse whose area is not one this company delivers to.</summary>
    public async Task EnsureWarehousePlacesAsync(Guid tenantId, WarehouseRequest request, CancellationToken cancellationToken)
    {
        var places = await PlacesAsync(tenantId, cancellationToken);
        var codes = places.Select(place => place.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!codes.Contains(request.Governorate.Trim()))
        {
            throw new DomainException("warehouse.invalid_governorate", "Unknown area.", request.Governorate);
        }

        var unknown = request.Covers.FirstOrDefault(cover => !codes.Contains(cover.Trim()));
        if (unknown is not null)
        {
            throw new DomainException("warehouse.invalid_governorate", "Unknown area.", unknown);
        }
    }
}
