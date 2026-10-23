using System.Security.Claims;
using Logistico.Modules.Shipments.Contracts;
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

/// <summary>Each person's saved list views (filters, sort and columns under a name); only the owner sees or removes them.</summary>
internal static class ViewEndpoints
{
    public static IEndpointRouteBuilder MapViewEndpoints(this IEndpointRouteBuilder app)
    {
        var views = app.MapGroup("/" + ShipmentsApiRoutes.Views)
            .WithTags("Saved views")
            .RequireAuthorization(StaffPolicies.Staff)
            .WithValidation();

        views.MapGet("/", ListAsync).WithSummary("The signed-in person's saved views of a list.");
        // A view changes nobody's data, so every office role may save one (read-only roles included).
        views.MapPost("/", SaveAsync).WithSummary("Saves the current filters, sort and columns under a name (the same name replaces it).")
            .RequireAuthorization(StaffPolicies.Personal);
        views.MapDelete("/{id:guid}", DeleteAsync).WithSummary("Removes one of the signed-in person's saved views.")
            .RequireAuthorization(StaffPolicies.Personal);
        return app;
    }

    private static async Task<Ok<List<SavedViewItem>>> ListAsync(string? list, ClaimsPrincipal user, ShipmentsDbContext db, CancellationToken ct)
    {
        var owner = Owner(user);
        var name = list ?? SavedViewLists.Shipments;
        var items = await db.SavedViews.AsNoTracking()
            .Where(v => v.UserId == owner && v.List == name)
            .OrderBy(v => v.CreatedAt)
            .Select(v => new SavedViewItem(v.Id, v.List, v.Name, v.Query))
            .ToListAsync(ct);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> SaveAsync(SaveViewRequest request, ClaimsPrincipal user, ShipmentsDbContext db, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var owner = Owner(user);
        var name = request.Name.Trim();
        var mine = await db.SavedViews.Where(v => v.UserId == owner && v.List == request.List).ToListAsync(ct);
        var view = mine.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (view is not null)
        {
            view.Replace(request.Query);
        }
        else if (mine.Count >= SavedView.MaxPerList)
        {
            return Api.Rule(new DomainException("views.too_many", "You have saved as many views of this list as allowed; remove one first.", SavedView.MaxPerList));
        }
        else
        {
            view = SavedView.Create(tenant.TenantId, owner, request.List, name, request.Query, clock.GetUtcNow());
            db.SavedViews.Add(view);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Api.Stale(); // the same name saved twice at once
        }

        return TypedResults.Ok(new SavedViewItem(view.Id, view.List, view.Name, view.Query));
    }

    private static async Task<IResult> DeleteAsync(Guid id, ClaimsPrincipal user, ShipmentsDbContext db, CancellationToken ct)
    {
        var owner = Owner(user);
        var view = await db.SavedViews.FirstOrDefaultAsync(v => v.Id == id && v.UserId == owner, ct);
        if (view is null)
        {
            return Api.NotFound();
        }

        db.SavedViews.Remove(view);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>The account id (stable across e-mail changes); the name only when there is none.</summary>
    private static string Owner(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.UserName() ?? "";
}
