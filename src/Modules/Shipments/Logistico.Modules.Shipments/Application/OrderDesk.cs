using Logistico.Modules.Merchants.Contracts;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Application;

/// <summary>
/// Books one order the same way for everyone who books (the office's order screen and the seller portal): the seller
/// must be active and send from their own governorate, a domestic order without a typed fee gets the company's price,
/// and the order gets the next tracking number and the company's confirmation-call policy.
/// </summary>
internal sealed class OrderDesk(
    ShipmentsDbContext db,
    NumberGenerator numbers,
    IMerchantDirectory merchants,
    HubDirectory hubs,
    ConfirmationDesk confirmations,
    Pricing pricing,
    ITenantContext tenant,
    TimeProvider clock)
{
    /// <exception cref="DomainException">A business rule refused the order.</exception>
    public async Task<Shipment> BookAsync(CreateShipmentRequest request, string? bookedBy, CancellationToken ct)
    {
        MerchantSnapshot? merchant = null;
        if (request.MerchantId is { } merchantId)
        {
            var found = await merchants.FindActiveAsync(merchantId, ct)
                ?? throw new DomainException("shipment.merchant_not_found", "Merchant not found or inactive.");
            merchant = new MerchantSnapshot(found.Id, found.Name, found.Governorate);

            if (request.ServiceType == ServiceType.Domestic
                && !string.Equals(request.OriginCode?.Trim(), found.Governorate, StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainException("shipment.merchant_origin_mismatch", "The order origin must match the merchant's governorate.", found.Governorate);
            }
        }

        var network = await hubs.NetworkAsync(tenant.TenantId, ct);
        if (request.ServiceType == ServiceType.Domestic && request.DeliveryFee is null)
        {
            // No fee typed: the company's price list decides.
            request.DeliveryFee = (await pricing.GetAsync(tenant.TenantId, ct))
                .Quote(merchant?.Governorate ?? request.OriginCode, request.DestinationCode, request.GrossWeightKg, merchant?.Id).Fee;
        }

        var shipment = Shipment.Book(tenant.TenantId, await numbers.TrackingNumberAsync(ct), request, merchant, bookedBy, clock.GetUtcNow(), network);
        db.Shipments.Add(shipment);
        await confirmations.ApplyPolicyAsync([shipment], ct);
        await db.SaveChangesAsync(ct);
        return shipment;
    }
}
