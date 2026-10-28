using System.Globalization;
using System.Security.Claims;
using System.Text;
using Logistico.Modules.Merchants.Contracts;
using Logistico.Modules.Shipments.Application;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Logistico.SharedKernel.Iraq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Endpoints;

internal static class ShipmentEndpoints
{
    private const int ExportLimit = 50_000;

    public static IEndpointRouteBuilder MapShipmentEndpoints(this IEndpointRouteBuilder app)
    {
        var shipments = app.MapGroup("/" + ShipmentsApiRoutes.Shipments)
            .WithTags("Shipments")
            .RequireAuthorization(StaffPolicies.Staff)
            .WithValidation();

        shipments.MapGet("/", ListAsync)
            .WithSummary("Shipments with filters (status, service, seller, governorate, hub, late, needs truck), search, sorting and paging.");
        shipments.MapGet("/summary", GetSummaryAsync)
            .WithSummary("Counts per status, warehouse/courier workload and cash-on-delivery totals. Optional seller filter.");
        shipments.MapGet("/export.csv", ExportAsync)
            .WithSummary("CSV export (UTF-8 with BOM for Excel) of the filtered list, up to 50,000 rows.")
            .RequireAuthorization(StaffPolicies.Operate); // customers' names, phones and addresses in bulk
        shipments.MapPost("/exact", GetExactAsync)
            .WithSummary("Returns up to 200 shipments by exact IDs or tracking numbers, preserving the requested order (label printing).");
        shipments.MapGet("/{id:guid}/proofs", ProofsAsync)
            .WithSummary("Proof of delivery from the courier app for each attempt: who took the parcel, the signature, where the courier was.");
        shipments.MapGet("/{id:guid}", GetByIdAsync)
            .WithSummary("Shipment with its milestone history. Returns an ETag for optimistic concurrency.");
        shipments.MapPost("/", CreateAsync)
            .WithSummary("Books an international shipment or a domestic delivery order and allocates its tracking number.")
            .RequireAuthorization(StaffPolicies.Operate);
        shipments.MapPost("/bulk-book", BulkBookAsync)
            .WithSummary("Books a batch of domestic delivery orders for a seller in a single atomic transaction.")
            .RequireAuthorization(StaffPolicies.Operate);
        shipments.MapPost("/{id:guid}/events", RecordEventAsync)
            .WithSummary("Records a milestone by hand. Failed attempts apply the automation rules. Requires If-Match with the current ETag.")
            .RequireAuthorization(StaffPolicies.Operate);
        shipments.MapPost("/{id:guid}/return", StartReturnAsync)
            .WithSummary("Starts returning the parcel to the seller (it travels back to the seller's hub). Requires If-Match.")
            .RequireAuthorization(StaffPolicies.Operate);
        shipments.MapPut("/{id:guid}", UpdateOrderAsync)
            .WithSummary("Corrects a domestic order (customer, address, contents, amounts) until it is out for delivery. Requires If-Match.")
            .RequireAuthorization(StaffPolicies.Operate);
        shipments.MapPost("/{id:guid}/cancel", CancelOrderAsync)
            .WithSummary("Cancels an order before collection, or sends it back to the seller afterwards. Requires If-Match.")
            .RequireAuthorization(StaffPolicies.Operate);

        return app;
    }

    private static async Task<Ok<IReadOnlyList<ShipmentListItem>>> GetExactAsync(
        ShipmentReferencesRequest request,
        ShipmentsDbContext db,
        CancellationToken ct)
    {
        var references = request.References!
            .OfType<string>()
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var ids = references.Where(value => Guid.TryParse(value, out _)).Select(Guid.Parse).ToList();
        var trackingNumbers = references
            .Where(value => !Guid.TryParse(value, out _))
            .Select(TrackingNumber.Normalize)
            .ToList();

        var rows = await db.Shipments.AsNoTracking()
            .Where(shipment => ids.Contains(shipment.Id) || trackingNumbers.Contains(shipment.TrackingNumber))
            .Select(ShipmentProjections.ToListItem)
            .ToListAsync(ct);
        var byReference = rows
            .SelectMany(row => new[] { (Key: row.Id.ToString(), Row: row), (Key: row.TrackingNumber, Row: row) })
            .ToDictionary(item => item.Key, item => item.Row, StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<ShipmentListItem> ordered = references
            .Where(byReference.ContainsKey)
            .Select(reference => byReference[reference])
            .ToList();
        return TypedResults.Ok(ordered);
    }

    private static async Task<Ok<ShipmentListResult>> ListAsync([AsParameters] ShipmentQuery query, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var filtered = ShipmentSearch.Filter(db.Shipments.AsNoTracking(), query, clock.GetUtcNow());
        var total = await filtered.CountAsync(ct);
        var cod = await filtered
            .Where(s => s.CodAmount != null && s.CodCurrency != null)
            .GroupBy(s => s.CodCurrency!)
            .Select(g => new MoneyTotal(g.Key, g.Sum(s => s.CodAmount!.Value)))
            .ToListAsync(ct);

        var items = await ShipmentSearch.Sort(filtered, query)
            .Skip((query.EffectivePage - 1) * query.EffectivePageSize)
            .Take(query.EffectivePageSize)
            .Select(ShipmentProjections.ToListItem)
            .ToListAsync(ct);

        return TypedResults.Ok(new ShipmentListResult(items, total, query.EffectivePage, query.EffectivePageSize, cod));
    }

    private static async Task<Ok<ShipmentsSummary>> GetSummaryAsync(Guid? merchantId, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var shipments = db.Shipments.AsNoTracking();
        if (merchantId is { } id)
        {
            shipments = shipments.Where(s => s.MerchantId == id);
        }

        var counts = await shipments
            .GroupBy(s => new { s.ServiceType, s.Status })
            .Select(g => new ShipmentStatusCount(g.Key.ServiceType, g.Key.Status, g.Count()))
            .ToListAsync(ct);

        var startOfDay = Api.StartOfBaghdadDay(clock.GetUtcNow());
        var today = await shipments
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Delivered = g.Count(s => s.DeliveredAt >= startOfDay),
                Booked = g.Count(s => s.CreatedAt >= startOfDay),
            })
            .SingleOrDefaultAsync(ct);

        var codToCollect = await shipments
            .Where(s => s.CodAmount != null && s.CodCurrency != null && s.CodCollectedAt == null
                && s.Status != ShipmentStatus.Cancelled && !ShipmentLifecycle.ReturnStatuses.Contains(s.Status) && s.Status != ShipmentStatus.Returned)
            .GroupBy(s => s.CodCurrency!)
            .Select(g => new MoneyTotal(g.Key, g.Sum(s => s.CodAmount!.Value)))
            .ToListAsync(ct);
        var codCollected = await shipments
            .Where(s => s.CodCollectedAt != null && s.CodCurrency != null)
            .GroupBy(s => s.CodCurrency!)
            .Select(g => new MoneyTotal(g.Key, g.Sum(s => s.CodAmount!.Value)))
            .ToListAsync(ct);

        int Sum(params ShipmentStatus[] statuses) => counts.Where(c => statuses.Contains(c.Status)).Sum(c => c.Count);

        return TypedResults.Ok(new ShipmentsSummary(
            counts,
            counts.Sum(c => c.Count),
            Sum(ShipmentLifecycle.WarehouseStatuses),
            Sum(ShipmentStatus.OutForDelivery),
            Sum(ShipmentStatus.DeliveryFailed),
            today?.Delivered ?? 0,
            today?.Booked ?? 0,
            codToCollect,
            codCollected));
    }

    private static async Task ExportAsync([AsParameters] ShipmentQuery query, HttpResponse response, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await ShipmentSearch.Sort(ShipmentSearch.Filter(db.Shipments.AsNoTracking(), query, now), query)
            .Take(ExportLimit)
            .Select(ShipmentProjections.ToListItem)
            .ToListAsync(ct);

        response.ContentType = "text/csv; charset=utf-8";
        response.Headers.ContentDisposition = $"attachment; filename=\"logistico-shipments-{now:yyyyMMdd-HHmm}.csv\"";

        var csv = new StringBuilder();
        csv.Append('﻿'); // BOM so Excel opens Arabic text correctly
        csv.AppendLine("Tracking number,Service,Status,Seller,Shipper,Customer,Phone,Origin,Destination,City,Current hub,Destination hub,Pieces,Chargeable kg,COD,COD currency,Bin,Courier,Attempts,Order ref,ETA,Delivered,Created");
        foreach (var r in rows)
        {
            csv.AppendJoin(',',
                Api.Csv(r.TrackingNumber), Api.Csv(ShipmentTexts.Of(r.ServiceType)), Api.Csv(ShipmentTexts.Of(r.Status)), Api.Csv(r.MerchantName), Api.Csv(r.ShipperName),
                Api.Csv(r.ConsigneeName), Api.Csv(IraqPhone.Display(r.ConsigneePhone)), Api.Csv(TradeLocations.Name(r.OriginCode)), Api.Csv(TradeLocations.Name(r.DestinationCode)),
                Api.Csv(r.DestinationCity), Api.Csv(IraqHubs.TagOf(r.CurrentHubCode)), Api.Csv(IraqHubs.TagOf(r.DestinationHubCode)),
                r.Pieces.ToString(CultureInfo.InvariantCulture), r.ChargeableWeightKg.ToString(CultureInfo.InvariantCulture),
                r.CodAmount?.ToString(CultureInfo.InvariantCulture) ?? "", Api.Csv(r.CodCurrency), Api.Csv(r.WarehouseBin), Api.Csv(r.CourierName),
                r.DeliveryAttempts.ToString(CultureInfo.InvariantCulture), Api.Csv(r.ExternalReference),
                r.EtaAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", r.DeliveredAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "",
                r.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            csv.AppendLine();
        }

        await response.WriteAsync(csv.ToString(), Encoding.UTF8, ct);
    }

    private static async Task<IResult> GetByIdAsync(Guid id, HttpResponse response, ShipmentsDbContext db, CancellationToken ct)
    {
        var shipment = await db.Shipments.AsNoTracking().Include(s => s.Events).AsSplitQuery().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shipment is null)
        {
            return Api.NotFound();
        }

        response.Headers.ETag = Api.ETag(shipment.Version);
        var details = ShipmentProjections.ToDetails(shipment);
        if (shipment.CodSettlementId is { } settlementId)
        {
            // Which payout carried this order's cash to the seller (the Finance page lists it by number).
            details = details with { CodSettlementNumber = await db.CodSettlements.AsNoTracking().Where(c => c.Id == settlementId).Select(c => c.SettlementNumber).FirstOrDefaultAsync(ct) };
        }

        return TypedResults.Ok(details);
    }

    private static async Task<IResult> CreateAsync(CreateShipmentRequest request, ClaimsPrincipal user, OrderDesk orders, CancellationToken ct)
    {
        try
        {
            var shipment = await orders.BookAsync(request, user.UserName(), ct);
            return TypedResults.Created("/" + ShipmentsApiRoutes.ById(shipment.Id), new CreateShipmentResponse(shipment.Id, shipment.TrackingNumber));
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
    }

    private static async Task<IResult> BulkBookAsync(
        BulkBookOrdersRequest request,
        ClaimsPrincipal user,
        ShipmentsDbContext db,
        NumberGenerator numbers,
        IMerchantDirectory merchants,
        HubDirectory hubs,
        ConfirmationDesk confirmations,
        Pricing pricing,
        ITenantContext tenant,
        TimeProvider clock,
        CancellationToken ct)
    {
        try
        {
            var found = await merchants.FindActiveAsync(request.MerchantId!.Value, ct)
                ?? throw new DomainException("shipment.merchant_not_found", "Merchant not found or inactive.");
            var merchant = new MerchantSnapshot(found.Id, found.Name, found.Governorate);
            var now = clock.GetUtcNow();
            var actor = user.UserName();
            var network = await hubs.NetworkAsync(tenant.TenantId, ct);
            var prices = await pricing.GetAsync(tenant.TenantId, ct);
            var orders = request.Orders!;
            var booked = new List<CreateShipmentResponse>(orders.Count);
            var created = new List<Shipment>(orders.Count);

            foreach (var draft in orders.OfType<ParsedOrderDraft>())
            {
                var single = new CreateShipmentRequest
                {
                    ServiceType = ServiceType.Domestic,
                    Mode = TransportMode.Road,
                    MerchantId = merchant.Id,
                    OriginCode = merchant.Governorate,
                    DestinationCode = draft.DestinationCode,
                    DestinationCity = draft.DestinationCity,
                    DeliveryAddress = string.IsNullOrWhiteSpace(draft.DeliveryAddress)
                        ? IraqGovernorates.NameOrCode(draft.DestinationCode)
                        : draft.DeliveryAddress,
                    Landmark = draft.Landmark,
                    ConsigneeName = draft.ConsigneeName,
                    ConsigneePhone = draft.ConsigneePhone,
                    ItemsDescription = draft.ItemsDescription,
                    PickupRequired = request.PickupRequired,
                    Pieces = 1,
                    GrossWeightKg = 1m,
                    CodAmount = draft.CodAmount,
                    CodCurrency = "IQD",
                    DeliveryFee = draft.DeliveryFee > 0
                        ? draft.DeliveryFee
                        : prices.Quote(merchant.Governorate, draft.DestinationCode, 1m, merchant.Id).Fee,
                };

                var shipment = Shipment.Book(tenant.TenantId, await numbers.TrackingNumberAsync(ct), single, merchant, actor, now, network);
                db.Shipments.Add(shipment);
                created.Add(shipment);
                booked.Add(new CreateShipmentResponse(shipment.Id, shipment.TrackingNumber));
            }

            await confirmations.ApplyPolicyAsync(created, ct);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(new BulkBookOrdersResponse(booked.Count, booked));
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
    }

    private static async Task<IResult> RecordEventAsync(
        Guid id,
        RecordShipmentEventRequest request,
        HttpRequest httpRequest,
        HttpResponse response,
        ClaimsPrincipal user,
        ShipmentsDbContext db,
        AutomationPolicies policies,
        TransferPlanner planner,
        HubDirectory hubs,
        Activity activity,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!Api.TryReadIfMatch(httpRequest, out var expectedVersion))
        {
            return Api.IfMatchRequired();
        }

        var shipment = await db.Shipments.Include(s => s.Events).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shipment is null)
        {
            return Api.NotFound();
        }

        if (shipment.Version != expectedVersion)
        {
            return Api.Stale();
        }

        try
        {
            var now = clock.GetUtcNow();
            var actor = new Actor(user.UserName());
            var policy = await policies.GetAsync(shipment.TenantId, ct);
            var status = request.Status!.Value;
            var network = await hubs.NetworkAsync(shipment.TenantId, ct);
            var hub = network.Find(request.Location);
            if (!string.IsNullOrWhiteSpace(request.Location)
                && hub is null
                && status is ShipmentStatus.AtWarehouse or ShipmentStatus.AtDestinationWarehouse or ShipmentStatus.OutForDelivery)
            {
                throw new DomainException("scan.unknown_hub", "Unknown or inactive hub.", request.Location);
            }

            switch (status)
            {
                case ShipmentStatus.DeliveryFailed:
                    activity.AfterFailure(shipment, shipment.RecordFailedAttempt(request.FailureReason!.Value, request.CourierName, request.Notes, policy, actor, now), policy);
                    break;
                case ShipmentStatus.AtWarehouse or ShipmentStatus.AtDestinationWarehouse:
                    shipment.ReceiveAtHub(hub ?? network.FindOrDefault(shipment.CurrentHubCode ?? DefaultHub(shipment)), request.WarehouseBin, actor, now);
                    if (policy.AutoPlanTransfers)
                    {
                        await planner.PlanAsync(shipment, ct);
                    }

                    break;
                case ShipmentStatus.OutForDelivery:
                    shipment.DispatchToCourier(hub ?? network.FindOrDefault(shipment.CurrentHubCode ?? shipment.DestinationHubCode), request.CourierName, actor, now);
                    activity.Notify(shipment, NotificationTemplate.OutForDelivery, policy);
                    break;
                case ShipmentStatus.ReturnInitiated:
                    shipment.StartReturn(ReturnReason.Other, request.Notes, actor, now);
                    activity.Notify(shipment, NotificationTemplate.ReturnStarted, policy);
                    await planner.ReconcileAsync(shipment, policy.AutoPlanTransfers, ct);
                    break;
                default:
                    shipment.AdvanceTo(status, request.OccurredAt ?? now,
                        new MilestoneDetails(request.Location, request.Notes, request.WarehouseBin, request.CourierName, request.FailureReason), "user", actor.Name, now);
                    if (status == ShipmentStatus.Delivered)
                    {
                        activity.Notify(shipment, NotificationTemplate.Delivered, policy);
                    }

                    break;
            }

            db.Entry(shipment).Property(s => s.Version).OriginalValue = expectedVersion;
            await db.SaveChangesAsync(ct);
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Api.Stale();
        }

        response.Headers.ETag = Api.ETag(shipment.Version);
        return TypedResults.Ok(ShipmentProjections.ToDetails(shipment));
    }

    private static async Task<Ok<List<DeliveryProofItem>>> ProofsAsync(Guid id, ShipmentsDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.DeliveryProofs.AsNoTracking()
            .Where(p => p.ShipmentId == id)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new DeliveryProofItem(p.Id, p.Delivered, p.FailureReason, p.RecordedAt, p.CourierLabel, p.ReceivedBy, p.ReceiverName, p.Signature,
                p.Latitude, p.Longitude, p.AccuracyMeters))
            .ToListAsync(ct));

    private static async Task<IResult> StartReturnAsync(
        Guid id,
        StartReturnRequest request,
        HttpRequest httpRequest,
        HttpResponse response,
        ClaimsPrincipal user,
        ShipmentsDbContext db,
        ReturnDesk returns,
        CancellationToken ct)
    {
        if (!Api.TryReadIfMatch(httpRequest, out var expectedVersion))
        {
            return Api.IfMatchRequired();
        }

        var shipment = await db.Shipments.Include(s => s.Events).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shipment is null)
        {
            return Api.NotFound();
        }

        if (shipment.Version != expectedVersion)
        {
            return Api.Stale();
        }

        try
        {
            await returns.StartAsync(shipment, request.Reason!.Value, request.Notes, user.UserName(), ct);
            db.Entry(shipment).Property(s => s.Version).OriginalValue = expectedVersion;
            await db.SaveChangesAsync(ct);
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Api.Stale();
        }

        response.Headers.ETag = Api.ETag(shipment.Version);
        return TypedResults.Ok(ShipmentProjections.ToDetails(shipment));
    }

    private static async Task<IResult> UpdateOrderAsync(
        Guid id,
        UpdateOrderRequest request,
        HttpRequest httpRequest,
        HttpResponse response,
        ClaimsPrincipal user,
        ShipmentsDbContext db,
        HubDirectory hubs,
        AutomationPolicies policies,
        TransferPlanner planner,
        TimeProvider clock,
        CancellationToken ct) =>
        await MutateAsync(id, httpRequest, response, db, async shipment =>
        {
            var previousTarget = shipment.TargetHubCode;
            shipment.EditOrder(request, await hubs.NetworkAsync(shipment.TenantId, ct), new Actor(user.UserName()), clock.GetUtcNow());

            // A new governorate can move the parcel to another hub: take it off a truck that no longer goes there.
            if (shipment.TargetHubCode != previousTarget)
            {
                await planner.ReconcileAsync(shipment, (await policies.GetAsync(shipment.TenantId, ct)).AutoPlanTransfers, ct);
            }
        }, ct);

    private static async Task<IResult> CancelOrderAsync(
        Guid id,
        CancelOrderRequest request,
        HttpRequest httpRequest,
        HttpResponse response,
        ClaimsPrincipal user,
        ShipmentsDbContext db,
        ReturnDesk returns,
        TimeProvider clock,
        CancellationToken ct) =>
        await MutateAsync(id, httpRequest, response, db, async shipment =>
        {
            if (shipment.Status is ShipmentStatus.Draft or ShipmentStatus.Booked)
            {
                shipment.Cancel(request.Reason, new Actor(user.UserName()), clock.GetUtcNow());
            }
            else
            {
                await returns.StartAsync(shipment, ReturnReason.SellerCancelled, request.Reason, user.UserName(), ct);
            }
        }, ct);

    /// <summary>Loads the order for an optimistic-concurrency change (If-Match), applies it and returns the new details.</summary>
    private static async Task<IResult> MutateAsync(Guid id, HttpRequest httpRequest, HttpResponse response, ShipmentsDbContext db, Func<Shipment, Task> change, CancellationToken ct)
    {
        if (!Api.TryReadIfMatch(httpRequest, out var expectedVersion))
        {
            return Api.IfMatchRequired();
        }

        var shipment = await db.Shipments.Include(s => s.Events).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shipment is null)
        {
            return Api.NotFound();
        }

        if (shipment.Version != expectedVersion)
        {
            return Api.Stale();
        }

        try
        {
            await change(shipment);
            db.Entry(shipment).Property(s => s.Version).OriginalValue = expectedVersion;
            await db.SaveChangesAsync(ct);
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Api.Stale();
        }

        response.Headers.ETag = Api.ETag(shipment.Version);
        return TypedResults.Ok(ShipmentProjections.ToDetails(shipment));
    }

    /// <summary>Before first mile the parcel is received at the seller's hub; afterwards at its destination hub.</summary>
    private static string DefaultHub(Shipment s) =>
        s.Status is ShipmentStatus.Booked or ShipmentStatus.PickedUp ? s.OriginHubCode ?? s.DestinationHubCode : s.DestinationHubCode;
}
