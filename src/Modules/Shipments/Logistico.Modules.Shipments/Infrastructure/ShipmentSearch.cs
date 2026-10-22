using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Iraq;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Infrastructure;

/// <summary>Turns a <see cref="ShipmentQuery"/> into SQL: filters, free-text search and whitelisted sorting.</summary>
internal static class ShipmentSearch
{
    public static IQueryable<Shipment> Filter(IQueryable<Shipment> source, ShipmentQuery query, DateTimeOffset now)
    {
        if (query.Status is { Length: > 0 } statuses)
        {
            source = source.Where(s => statuses.Contains(s.Status));
        }

        if (query.InWarehouse == true)
        {
            var warehouse = ShipmentLifecycle.WarehouseStatuses;
            source = source.Where(s => warehouse.Contains(s.Status));
        }

        if (query.Service is { } service)
        {
            source = source.Where(s => s.ServiceType == service);
        }

        if (query.MerchantId is { } merchantId)
        {
            source = source.Where(s => s.MerchantId == merchantId);
        }

        if (!string.IsNullOrWhiteSpace(query.Governorate))
        {
            var gov = query.Governorate.ToUpperInvariant();
            source = source.Where(s => s.DestinationCode == gov);
        }

        if (!string.IsNullOrWhiteSpace(query.Hub))
        {
            var hub = query.Hub.Trim().ToUpperInvariant();
            source = source.Where(s => s.CurrentHubCode == hub);
        }

        if (!string.IsNullOrWhiteSpace(query.DestinationHub))
        {
            var hub = query.DestinationHub.Trim().ToUpperInvariant();
            source = source.Where(s => s.DestinationHubCode == hub);
        }

        if (query.Confirmation is { } confirmation)
        {
            source = source.Where(s => s.Confirmation == confirmation);
        }

        if (query.NeedsTransfer == true)
        {
            var returning = ShipmentLifecycle.ReturnStatuses;
            var warehouse = ShipmentLifecycle.WarehouseStatuses;
            source = source.Where(s => s.CurrentHubCode != null
                && (warehouse.Contains(s.Status) || s.Status == ShipmentStatus.ReturnInitiated)
                && (returning.Contains(s.Status) ? (s.OriginHubCode ?? s.DestinationHubCode) : s.DestinationHubCode) != s.CurrentHubCode);
        }

        if (query.Late == true)
        {
            var closed = ShipmentLifecycle.ClosedStatuses;
            var returning = ShipmentLifecycle.ReturnStatuses;
            source = source.Where(s => s.EtaAt != null && s.EtaAt < now && !closed.Contains(s.Status) && !returning.Contains(s.Status));
        }

        if (!string.IsNullOrWhiteSpace(query.Courier))
        {
            var pattern = $"%{query.Courier.Trim()}%";
            source = source.Where(s => s.CourierName != null && EF.Functions.ILike(s.CourierName, pattern));
        }

        return ByText(source, query.Q);
    }

    /// <summary>The folded search column (see <see cref="SearchText"/>), kept by PostgreSQL.</summary>
    public const string Column = "SearchText";

    /// <summary>
    /// Free-text search: a whole tracking number or Iraqi mobile number is an exact match (index lookups); anything else
    /// matches when every word appears in the parcel's folded search text.
    /// </summary>
    public static IQueryable<Shipment> ByText(IQueryable<Shipment> source, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return source;
        }

        var tracking = TrackingNumber.Normalize(text);
        if (TrackingNumber.IsValid(tracking))
        {
            return source.Where(s => s.TrackingNumber == tracking);
        }

        if (IraqPhone.Normalize(text) is { } phone)
        {
            return source.Where(s => s.ConsigneePhone == phone);
        }

        foreach (var word in SearchText.Words(text))
        {
            var pattern = SearchText.Pattern(word);
            source = source.Where(s => EF.Functions.Like(EF.Property<string>(s, Column), pattern));
        }

        return source;
    }

    public static IQueryable<Shipment> Sort(IQueryable<Shipment> source, ShipmentQuery query)
    {
        var desc = query.Desc ?? query.Sort is null;
        return (query.Sort?.ToLowerInvariant()) switch
        {
            "trackingnumber" => desc ? source.OrderByDescending(s => s.TrackingNumber) : source.OrderBy(s => s.TrackingNumber),
            "status" => desc ? source.OrderByDescending(s => s.Status).ThenByDescending(s => s.CreatedAt) : source.OrderBy(s => s.Status).ThenByDescending(s => s.CreatedAt),
            "consignee" => desc ? source.OrderByDescending(s => s.ConsigneeName) : source.OrderBy(s => s.ConsigneeName),
            "merchant" => desc ? source.OrderByDescending(s => s.MerchantName) : source.OrderBy(s => s.MerchantName),
            "cod" => desc ? source.OrderByDescending(s => s.CodAmount) : source.OrderBy(s => s.CodAmount),
            "eta" => desc ? source.OrderByDescending(s => s.EtaAt) : source.OrderBy(s => s.EtaAt),
            "destination" => desc ? source.OrderByDescending(s => s.DestinationCode) : source.OrderBy(s => s.DestinationCode),
            "age" => desc ? source.OrderBy(s => s.LastStatusAt) : source.OrderByDescending(s => s.LastStatusAt),
            "hub" => desc ? source.OrderByDescending(s => s.CurrentHubCode) : source.OrderBy(s => s.CurrentHubCode),
            "bin" => desc ? source.OrderByDescending(s => s.WarehouseBin).ThenBy(s => s.TrackingNumber) : source.OrderBy(s => s.WarehouseBin).ThenBy(s => s.TrackingNumber),
            _ => desc ? source.OrderByDescending(s => s.CreatedAt) : source.OrderBy(s => s.CreatedAt),
        };
    }
}
