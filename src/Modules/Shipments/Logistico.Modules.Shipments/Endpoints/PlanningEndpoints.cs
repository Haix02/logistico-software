using System.Security.Claims;
using Logistico.Modules.Shipments.Application;
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

/// <summary>The optimization engine (docs/20): plan trucks, courier runs and zones, look at the plans, approve one.</summary>
internal static class PlanningEndpoints
{
    public static IEndpointRouteBuilder MapPlanningEndpoints(this IEndpointRouteBuilder app)
    {
        var planning = app.MapGroup("/" + ShipmentsApiRoutes.Planning)
            .WithTags("Planning")
            .RequireAuthorization(StaffPolicies.Staff)
            .WithValidation();

        planning.MapGet("/policy", GetPolicyAsync).WithSummary("Planning settings: trucks and their cost, departure times, courier limits, travel estimates. Returns an ETag.");
        planning.MapPut("/policy", UpdatePolicyAsync).WithSummary("Changes the planning settings. Requires If-Match.")
            .RequireAuthorization(StaffPolicies.Administer);
        planning.MapGet("/runs", ListAsync).WithSummary("Plans made, newest first; filter by kind and hub.");
        planning.MapGet("/runs/{id:guid}", GetAsync).WithSummary("A plan with its figures, today's practice for comparison and the checker's verdict. Returns an ETag.");
        planning.MapPost("/runs", RunAsync)
            .WithSummary("Solves a planner now (trucks for the network, courier runs or zones for one hub) within the company's time limit, checks the plan and keeps it.")
            .RequireAuthorization(StaffPolicies.Operate);
        planning.MapPost("/runs/{id:guid}/apply", ApplyAsync)
            .WithSummary("Approves a plan: loads the next trucks, or hands parcels to couriers in the planned order. Parcels that moved since are skipped. Requires If-Match.")
            .RequireAuthorization(StaffPolicies.Operate);
        planning.MapPost("/runs/{id:guid}/discard", DiscardAsync).WithSummary("Puts a plan aside without applying it. Requires If-Match.")
            .RequireAuthorization(StaffPolicies.Operate);
        return app;
    }

    private static async Task<IResult> GetPolicyAsync(HttpResponse response, PlanningPolicies policies, ITenantContext tenant, CancellationToken ct)
    {
        var policy = await policies.GetAsync(tenant.TenantId, ct);
        response.Headers.ETag = Api.ETag(policy.Version);
        return TypedResults.Ok(policy);
    }

    private static async Task<IResult> UpdatePolicyAsync(
        PlanningPolicy policy, HttpRequest http, HttpResponse response, ClaimsPrincipal user, ShipmentsDbContext db, PlanningPolicies policies, ITenantContext tenant,
        TimeProvider clock, CancellationToken ct)
    {
        if (!Api.TryReadIfMatch(http, out var expected))
        {
            return Api.IfMatchRequired();
        }

        var settings = await policies.GetForUpdateAsync(tenant.TenantId, ct);
        if (db.Entry(settings).State != EntityState.Added && settings.Version != expected)
        {
            return Api.Stale("planning.stale");
        }

        settings.Update(policy, user.UserName(), clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Api.Stale("planning.stale");
        }

        var saved = settings.ToPolicy();
        response.Headers.ETag = Api.ETag(saved.Version);
        return TypedResults.Ok(saved);
    }

    private static async Task<Ok<PagedResult<PlanRunSummary>>> ListAsync(PlanKind? kind, string? hub, int? page, int? pageSize, ShipmentsDbContext db, CancellationToken ct)
    {
        var size = Math.Clamp(pageSize ?? 25, 1, 100);
        var number = Paging.Page(page);
        var runs = db.PlanningRuns.AsNoTracking();
        if (kind is { } k)
        {
            runs = runs.Where(r => r.Kind == k);
        }

        if (!string.IsNullOrWhiteSpace(hub))
        {
            var code = hub.Trim().ToUpperInvariant();
            runs = runs.Where(r => r.HubCode == code);
        }

        var total = await runs.CountAsync(ct);
        var items = (await runs.OrderByDescending(r => r.RequestedAt).Skip((number - 1) * size).Take(size).ToListAsync(ct)).Select(r => r.ToSummary()).ToList();
        return TypedResults.Ok(new PagedResult<PlanRunSummary>(items, total, number, size));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpResponse response, ShipmentsDbContext db, CancellationToken ct)
    {
        var run = await db.PlanningRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null)
        {
            return Api.NotFound("planning.not_found");
        }

        response.Headers.ETag = Api.ETag(run.Version);
        return TypedResults.Ok(Details(run));
    }

    private static async Task<IResult> RunAsync(StartPlanRequest request, HttpResponse response, ClaimsPrincipal user, PlanningDesk desk, CancellationToken ct)
    {
        try
        {
            var run = await desk.RunAsync(request, user.UserName(), ct);
            response.Headers.ETag = Api.ETag(run.Version);
            return TypedResults.Created("/" + ShipmentsApiRoutes.PlanningRun(run.Id), Details(run));
        }
        catch (DomainException ex) when (ex.Code == "planning.busy")
        {
            return Api.Conflict(ex.Code);
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
    }

    private static async Task<IResult> ApplyAsync(Guid id, HttpRequest http, HttpResponse response, ClaimsPrincipal user, ShipmentsDbContext db, PlanningDesk desk, CancellationToken ct)
    {
        if (!Api.TryReadIfMatch(http, out var expected))
        {
            return Api.IfMatchRequired();
        }

        var run = await db.PlanningRuns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null)
        {
            return Api.NotFound("planning.not_found");
        }

        if (run.Version != expected)
        {
            return Api.Stale("planning.stale");
        }

        try
        {
            db.Entry(run).Property(r => r.Version).OriginalValue = expected;
            var result = await desk.ApplyAsync(run, user.UserName(), ct);
            response.Headers.ETag = Api.ETag(run.Version);
            return TypedResults.Ok(result with { Summary = run.ToSummary() });
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.IsUniqueViolation())
        {
            // A truck or parcel in the plan changed at the same moment: nothing was applied.
            return Api.Stale("planning.stale");
        }
    }

    private static async Task<IResult> DiscardAsync(Guid id, HttpRequest http, HttpResponse response, ClaimsPrincipal user, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Api.TryReadIfMatch(http, out var expected))
        {
            return Api.IfMatchRequired();
        }

        var run = await db.PlanningRuns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null)
        {
            return Api.NotFound("planning.not_found");
        }

        if (run.Version != expected)
        {
            return Api.Stale("planning.stale");
        }

        try
        {
            run.Discard(user.UserName(), clock.GetUtcNow());
            db.Entry(run).Property(r => r.Version).OriginalValue = expected;
            await db.SaveChangesAsync(ct);
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Api.Stale("planning.stale");
        }

        response.Headers.ETag = Api.ETag(run.Version);
        return TypedResults.Ok(run.ToSummary());
    }

    private static PlanRunDetails Details(PlanningRun run)
    {
        var payload = run.Payload();
        return new PlanRunDetails(run.ToSummary(), payload.Trucks, payload.Couriers, payload.Zones, payload.Violations);
    }
}
