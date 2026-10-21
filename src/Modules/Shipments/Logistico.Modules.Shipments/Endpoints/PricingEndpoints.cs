using System.Security.Claims;
using Logistico.Modules.Shipments.Application;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Endpoints;

/// <summary>The company's delivery prices.</summary>
internal static class PricingEndpoints
{
    public static IEndpointRouteBuilder MapPricingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/" + ShipmentsApiRoutes.Pricing)
            .WithTags("Pricing")
            .RequireAuthorization(StaffPolicies.Staff)
            .WithValidation();
        group.MapGet("/", GetAsync).WithSummary("Delivery prices: same governorate, per destination governorate, weight and seller discounts.");
        group.MapPut("/", UpdateAsync).WithSummary("Changes the delivery prices for new orders. Requires If-Match.")
            .RequireAuthorization(StaffPolicies.Finance);
        return app;
    }

    private static async Task<IResult> GetAsync(HttpResponse response, Pricing pricing, ITenantContext tenant, CancellationToken ct)
    {
        var card = await pricing.GetAsync(tenant.TenantId, ct);
        response.Headers.ETag = Api.ETag(card.Version);
        return TypedResults.Ok(card);
    }

    private static async Task<IResult> UpdateAsync(
        RateCard card,
        HttpRequest http,
        HttpResponse response,
        ClaimsPrincipal user,
        ShipmentsDbContext db,
        Pricing pricing,
        ITenantContext tenant,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!Api.TryReadIfMatch(http, out var expected))
        {
            return Api.IfMatchRequired();
        }

        var settings = await pricing.GetForUpdateAsync(tenant.TenantId, ct);
        if (db.Entry(settings).State != EntityState.Added && settings.Version != expected)
        {
            return Api.Stale("pricing.stale");
        }

        settings.Update(card, user.UserName(), clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Api.Stale("pricing.stale");
        }

        var saved = settings.ToRateCard();
        response.Headers.ETag = Api.ETag(saved.Version);
        return TypedResults.Ok(saved);
    }
}
