using System.Security.Claims;
using Logistico.Modules.Merchants.Contracts;
using Logistico.Modules.Shipments.Application;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Endpoints;

/// <summary>
/// Each hub's sort plan: rules that choose the shelf for a parcel scanned in without one (ideas from warehouse putaway
/// rules and parcel sort plans; docs/18), and the load of every shelf.
/// </summary>
internal static class SortPlanEndpoints
{
    public static IEndpointRouteBuilder MapSortPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var plan = app.MapGroup("/" + ShipmentsApiRoutes.SortPlan)
            .WithTags("Sort plan")
            .RequireAuthorization(StaffPolicies.Staff)
            .WithValidation();

        plan.MapGet("/{hub}", GetAsync).WithSummary("A hub's rules, most specific first, and every shelf with what is on it, in natural order.");
        plan.MapGet("/{hub}/suggest/{trackingNumber}", SuggestAsync).WithSummary("The shelf the plan would choose if the parcel were scanned in at the hub now.");
        plan.MapPost("/rules", CreateAsync).WithSummary("Adds a rule to a hub's sort plan.").RequireAuthorization(StaffPolicies.Operate);
        plan.MapPut("/rules/{id:guid}", UpdateAsync).WithSummary("Changes a rule (it stays at its hub).").RequireAuthorization(StaffPolicies.Operate);
        plan.MapDelete("/rules/{id:guid}", DeleteAsync).WithSummary("Removes a rule; parcels already on its shelves stay there.").RequireAuthorization(StaffPolicies.Operate);
        return app;
    }

    private static async Task<IResult> GetAsync(string hub, ShipmentsDbContext db, HubDirectory hubs, ITenantContext tenant, CancellationToken ct)
    {
        if ((await hubs.NetworkAsync(tenant.TenantId, ct)).Find(hub) is not { } found)
        {
            return Api.Rule(new DomainException("scan.unknown_hub", "Unknown hub.", hub));
        }

        var rules = await db.ShelfRules.AsNoTracking().Where(r => r.HubCode == found.Code).ToListAsync(ct);
        var ordered = new HubSortPlan(found.Code, rules, []);
        var onShelves = await db.Shipments.AsNoTracking()
            .Where(s => s.CurrentHubCode == found.Code && ShelfCheck.OnShelf.Contains(s.Status))
            .GroupBy(s => s.WarehouseBin)
            .Select(g => new { Shelf = g.Key, Parcels = g.Count() })
            .ToListAsync(ct);

        // Every shelf in use or in the plan; a shelf listed by several rules shows the capacity of the most specific one.
        var planned = ordered.Rules.SelectMany(r => r.Shelves.Select(shelf => (Shelf: shelf, Rule: r))).DistinctBy(x => x.Shelf).ToDictionary(x => x.Shelf, x => x.Rule);
        var shelves = onShelves.Where(x => x.Shelf is not null).Select(x => x.Shelf!).Union(planned.Keys)
            .Order(ShelfOrder.Instance)
            .Select(shelf => new ShelfLoad(
                shelf,
                onShelves.FirstOrDefault(x => x.Shelf == shelf)?.Parcels ?? 0,
                planned.GetValueOrDefault(shelf)?.CapacityPerShelf,
                planned.GetValueOrDefault(shelf)?.Id))
            .ToList();
        var withoutShelf = onShelves.FirstOrDefault(x => x.Shelf is null)?.Parcels ?? 0;
        return TypedResults.Ok(new SortPlanView(found.Code, [.. ordered.Rules.Select(r => r.ToItem())], shelves, withoutShelf));
    }

    private static async Task<IResult> SuggestAsync(
        string hub, string trackingNumber, ShipmentsDbContext db, HubDirectory hubs, SortPlanner planner, ITenantContext tenant, CancellationToken ct)
    {
        if ((await hubs.NetworkAsync(tenant.TenantId, ct)).Find(hub) is not { } found)
        {
            return Api.Rule(new DomainException("scan.unknown_hub", "Unknown hub.", hub));
        }

        var number = TrackingNumber.Normalize(trackingNumber);
        if (await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.TrackingNumber == number, ct) is not { } shipment)
        {
            return Api.NotFound();
        }

        var placement = (await planner.ForHubAsync(found.Code, ct)).Place(shipment);
        return TypedResults.Ok(new ShelfSuggestion(
            shipment.TrackingNumber,
            placement?.Purpose ?? ShelfParcel.Of(shipment, found.Code).Purpose,
            placement?.Shelf,
            placement?.Rule.Id,
            placement?.Full ?? false,
            placement?.AlreadyThere ?? false));
    }

    private static async Task<IResult> CreateAsync(
        ShelfRuleRequest request, ClaimsPrincipal user, ShipmentsDbContext db, HubDirectory hubs, IMerchantDirectory merchants, ITenantContext tenant,
        TimeProvider clock, CancellationToken ct)
    {
        try
        {
            var (hub, merchantName) = await CheckAsync(request, hubs, merchants, tenant, ct);
            if (await db.ShelfRules.CountAsync(r => r.HubCode == hub, ct) >= SortPlanRules.MaxRulesPerHub)
            {
                throw new DomainException("sort_plan.too_many_rules", "This hub has as many rules as it may have.", SortPlanRules.MaxRulesPerHub);
            }

            request.HubCode = hub;
            var rule = ShelfRule.Create(tenant.TenantId, request, merchantName, user.UserName(), clock.GetUtcNow());
            db.ShelfRules.Add(rule);
            await db.SaveChangesAsync(ct);
            return TypedResults.Created("/" + ShipmentsApiRoutes.SortPlanRule(rule.Id), rule.ToItem());
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, ShelfRuleRequest request, ClaimsPrincipal user, ShipmentsDbContext db, HubDirectory hubs, IMerchantDirectory merchants, ITenantContext tenant,
        TimeProvider clock, CancellationToken ct)
    {
        if (await db.ShelfRules.FirstOrDefaultAsync(r => r.Id == id, ct) is not { } rule)
        {
            return Api.NotFound("sort_plan.not_found");
        }

        try
        {
            var (hub, merchantName) = await CheckAsync(request, hubs, merchants, tenant, ct);
            request.HubCode = hub;
            rule.Update(request, merchantName, user.UserName(), clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(rule.ToItem());
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Api.Stale(); // changed or removed by someone else at the same moment
        }
    }

    private static async Task<IResult> DeleteAsync(Guid id, ShipmentsDbContext db, CancellationToken ct)
    {
        if (await db.ShelfRules.FirstOrDefaultAsync(r => r.Id == id, ct) is not { } rule)
        {
            return Api.NotFound("sort_plan.not_found");
        }

        db.ShelfRules.Remove(rule);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Removed by someone else at the same moment: the result is the same.
        }

        return TypedResults.NoContent();
    }

    /// <summary>The rule's hub as the network spells it and the seller's name; unknown hubs and sellers are refused.</summary>
    private static async Task<(string Hub, string? MerchantName)> CheckAsync(
        ShelfRuleRequest request, HubDirectory hubs, IMerchantDirectory merchants, ITenantContext tenant, CancellationToken ct)
    {
        var network = await hubs.NetworkAsync(tenant.TenantId, ct);
        var hub = network.Find(request.HubCode) ?? throw new DomainException("scan.unknown_hub", "Unknown hub.", request.HubCode);
        if (!string.IsNullOrWhiteSpace(request.ToHubCode))
        {
            var to = network.Find(request.ToHubCode) ?? throw new DomainException("scan.unknown_hub", "Unknown hub.", request.ToHubCode);
            if (to.Code == hub.Code)
            {
                throw new DomainException("sort_plan.same_hub", "Parcels waiting for a truck travel to another hub.");
            }

            request.ToHubCode = to.Code;
        }

        string? merchantName = null;
        if (request.MerchantId is { } merchantId)
        {
            merchantName = (await merchants.FindActiveAsync(merchantId, ct))?.Name
                ?? throw new DomainException("shipment.merchant_not_found", "Merchant not found or inactive.");
        }

        return (hub.Code, merchantName);
    }
}
