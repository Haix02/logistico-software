using System.Linq.Expressions;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Domain;

namespace Logistico.Modules.Shipments.Infrastructure;

internal static class ShipmentProjections
{
    /// <summary>Flat row for lists, translated to SQL.</summary>
    public static readonly Expression<Func<Shipment, ShipmentListItem>> ToListItem = s => new ShipmentListItem(
        s.Id,
        s.TrackingNumber,
        s.ServiceType,
        s.Mode,
        s.Status,
        s.MerchantName,
        s.ShipperName,
        s.ConsigneeName,
        s.ConsigneePhone,
        s.OriginCode,
        s.DestinationCode,
        s.DestinationCity,
        s.Pieces,
        s.ChargeableWeightKg,
        s.CodAmount,
        s.CodCurrency,
        s.WarehouseBin,
        s.CourierName,
        s.DeliveryAttempts,
        s.ExternalReference,
        s.EtaAt,
        s.DeliveredAt,
        s.CreatedAt,
        s.OriginHubCode,
        s.DestinationHubCode,
        s.CurrentHubCode,
        s.RescheduledTo,
        s.ReturnReason,
        s.LastStatusAt,
        s.Confirmation);

    public static ShipmentDetails ToDetails(Shipment s) => new(
        s.Id,
        s.TrackingNumber,
        s.ServiceType,
        s.HouseBillNumber,
        s.Mode,
        s.Status,
        s.Incoterm,
        s.MerchantId,
        s.MerchantName,
        s.ExternalReference,
        s.ShipperName,
        s.ConsigneeName,
        s.ConsigneePhone,
        s.OriginCode,
        s.DestinationCode,
        s.DestinationCity,
        s.DeliveryAddress,
        s.Landmark,
        s.ItemsDescription,
        s.PickupRequired,
        s.Pieces,
        s.GrossWeightKg,
        s.VolumeCbm,
        s.ChargeableWeightKg,
        s.DeclaredValue,
        s.DeclaredCurrency,
        s.CodAmount,
        s.CodCurrency,
        s.CodCollectedAt,
        s.DeliveryFee,
        s.WarehouseBin,
        s.CourierName,
        s.DeliveryAttempts,
        s.LastFailureReason,
        s.PromisedAt,
        s.EtaAt,
        s.DeliveredAt,
        s.Notes,
        s.CreatedAt,
        s.CreatedBy,
        s.Version,
        s.Events
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.RecordedAt)
            .Select(e => new ShipmentEventItem(e.Id, e.Status, e.OccurredAt, e.RecordedAt, e.Location, e.WarehouseBin, e.CourierName, e.FailureReason, e.Notes, e.Source, e.RecordedBy))
            .ToList(),
        s.OriginHubCode,
        s.DestinationHubCode,
        s.CurrentHubCode,
        s.TransferId,
        s.ReturnReason,
        s.ReturnRequestedAt,
        s.ReturnedAt,
        s.ReturnManifestNumber,
        s.RescheduledTo,
        s.LastStatusAt,
        s.Confirmation,
        s.ConfirmationRequestedAt,
        s.ConfirmationAttempts,
        s.ConfirmationBy,
        s.Rating,
        s.RatingComment,
        s.CodSettledAt,
        ReturnFee: s.ReturnFee);

    public static PublicTrackingView ToPublicView(Shipment s, DateTimeOffset now, int openRequests)
    {
        // Anonymous tracking is intentionally coarse. A tracking number may be forwarded or photographed,
        // so it must not reveal the seller, COD value, delivery area or the parcel's exact hub route.
        var ordered = s.Events.OrderBy(e => e.OccurredAt).ThenBy(e => e.RecordedAt).ToList();
        var visible = ordered
            .Where((e, i) => i == 0 || e.Status != ordered[i - 1].Status)
            .Reverse()
            .Select(e => new PublicTrackingEvent(e.Status, e.OccurredAt, null, e.FailureReason))
            .ToList();

        return new PublicTrackingView(
            s.TrackingNumber,
            s.ServiceType,
            s.Mode,
            s.Status,
            null,
            s.OriginCode,
            s.DestinationCode,
            null,
            null,
            null,
            s.DeliveryAttempts,
            s.PromisedAt,
            s.EtaAt,
            s.DeliveredAt,
            visible,
            null,
            null,
            null,
            s.RescheduledTo,
            Contracts.Rules.RequestRules.Allowed(s.Status, s.DeliveredAt, now),
            openRequests,
            [],
            s.Confirmation == ConfirmationStatus.Pending,
            s.CanBeRated(now),
            s.Rating);
    }

}
