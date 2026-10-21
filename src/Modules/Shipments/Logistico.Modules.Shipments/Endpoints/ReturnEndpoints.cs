using System.Security.Claims;
using Logistico.Modules.Shipments.Application;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Endpoints;

internal static class ReturnEndpoints
{
    public static IEndpointRouteBuilder MapReturnEndpoints(this IEndpointRouteBuilder app)
    {
        var returns = app.MapGroup("/" + ShipmentsApiRoutes.Returns)
            .WithTags("Returns")
            .RequireAuthorization(StaffPolicies.Staff)
            .WithValidation();

        returns.MapGet("/", OverviewAsync)
            .WithSummary("Returns in progress, on trucks and waiting at hubs (grouped by seller), plus reasons.");
        returns.MapPost("/hand-over", HandOverAsync)
            .WithSummary("Hands returned parcels back to the seller and issues a manifest for signature.")
            .RequireAuthorization(StaffPolicies.Operate);
        returns.MapGet("/manifests/{number}", ManifestAsync)
            .WithSummary("Reprints a return hand-over manifest.");
        return app;
    }

    private static async Task<Ok<ReturnsOverview>> OverviewAsync(ShipmentsDbContext db, AutomationPolicies policies, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var policy = await policies.GetAsync(tenant.TenantId, ct);
        var collectBefore = now.AddDays(-policy.ReturnCollectionDays);
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var shipments = db.Shipments.AsNoTracking();

        var counts = await shipments.GroupBy(_ => 1).Select(g => new
        {
            Initiated = g.Count(s => s.Status == ShipmentStatus.ReturnInitiated),
            OnTrucks = g.Count(s => s.Status == ShipmentStatus.ReturnInTransit),
            AtHub = g.Count(s => s.Status == ShipmentStatus.ReturnAtHub),
            Returned = g.Count(s => s.Status == ShipmentStatus.Returned && s.ReturnedAt >= monthStart),
            Overdue = g.Count(s => s.Status == ShipmentStatus.ReturnAtHub && s.LastStatusAt < collectBefore),
        }).SingleOrDefaultAsync(ct);

        var groups = await shipments
            .Where(s => s.Status == ShipmentStatus.ReturnAtHub)
            .GroupBy(s => new { s.MerchantId, s.MerchantName, s.ShipperName, Hub = s.CurrentHubCode ?? s.DestinationHubCode })
            .OrderBy(g => g.Min(s => s.LastStatusAt))
            .Select(g => new ReturnSellerGroup(g.Key.MerchantId, g.Key.MerchantName ?? g.Key.ShipperName, g.Key.Hub, g.Count(),
                g.Min(s => s.LastStatusAt), g.Sum(s => s.CodCurrency == "IQD" ? s.CodAmount ?? 0 : 0)))
            .ToListAsync(ct);

        var reasons = await shipments
            .Where(s => s.ReturnReason != null && (ShipmentLifecycle.ReturnStatuses.Contains(s.Status) || (s.Status == ShipmentStatus.Returned && s.ReturnedAt >= monthStart)))
            .GroupBy(s => s.ReturnReason!.Value)
            .OrderByDescending(g => g.Count())
            .Select(g => new ReturnReasonCount(g.Key, g.Count()))
            .ToListAsync(ct);

        return TypedResults.Ok(new ReturnsOverview(counts?.Initiated ?? 0, counts?.OnTrucks ?? 0, counts?.AtHub ?? 0, counts?.Returned ?? 0, counts?.Overdue ?? 0, groups, reasons));
    }

    private static async Task<IResult> HandOverAsync(HandOverReturnsRequest request, ClaimsPrincipal user, ShipmentsDbContext db, ReturnDesk returns, TimeProvider clock, CancellationToken ct)
    {
        try
        {
            var (number, shipments) = await returns.HandOverAsync(request, user.UserName(), ct);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(await LoadManifestAsync(number, db, ct));
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Api.Stale();
        }
    }

    private static async Task<IResult> ManifestAsync(string number, ShipmentsDbContext db, CancellationToken ct)
    {
        var manifest = await LoadManifestAsync(number, db, ct);
        return manifest.Items.Count == 0 ? Api.NotFound("return.manifest_not_found") : TypedResults.Ok(manifest);
    }

    private static async Task<ReturnManifest> LoadManifestAsync(string number, ShipmentsDbContext db, CancellationToken ct)
    {
        var items = await db.Shipments.AsNoTracking()
            .Where(s => s.ReturnManifestNumber == number)
            .OrderBy(s => s.TrackingNumber)
            .Select(ShipmentProjections.ToListItem)
            .ToListAsync(ct);
        var handOver = await db.ShipmentEvents.AsNoTracking()
            .Where(e => e.Status == ShipmentStatus.Returned && e.Notes != null && e.Notes.StartsWith(number))
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new { e.OccurredAt, e.RecordedBy, e.Notes, e.Location })
            .FirstOrDefaultAsync(ct);
        var receivedBy = handOver?.Notes is { } notes && notes.Contains(" · ") ? notes[(notes.IndexOf(" · ", StringComparison.Ordinal) + 3)..] : null;
        return new ReturnManifest(number, handOver?.Location ?? items.FirstOrDefault()?.DestinationHubCode ?? "", items.FirstOrDefault()?.MerchantName,
            receivedBy, handOver?.RecordedBy, handOver?.OccurredAt ?? DateTimeOffset.MinValue, items);
    }
}
