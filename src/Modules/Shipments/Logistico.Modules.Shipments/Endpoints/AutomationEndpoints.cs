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

internal static class AutomationEndpoints
{
    public static IEndpointRouteBuilder MapAutomationEndpoints(this IEndpointRouteBuilder app)
    {
        var automation = app.MapGroup("/" + ShipmentsApiRoutes.Automation)
            .WithTags("Automation & alerts")
            .RequireAuthorization(StaffPolicies.Staff)
            .WithValidation();

        automation.MapGet("/overview", OverviewAsync).WithSummary("Today's automation in numbers.");
        automation.MapGet("/policy", GetPolicyAsync).WithSummary("The company's automation rules.");
        automation.MapPut("/policy", UpdatePolicyAsync).WithSummary("Changes the automation rules. Requires If-Match.")
            .RequireAuthorization(StaffPolicies.Administer);
        automation.MapGet("/alerts", AlertsAsync).WithSummary("Exceptions: late, stuck, missing and misrouted parcels, late trucks, overdue requests.");
        automation.MapPost("/alerts/{id:guid}/resolve", ResolveAlertAsync).WithSummary("Marks an alert as handled.")
            .RequireAuthorization(StaffPolicies.Operate);
        automation.MapGet("/log", LogAsync).WithSummary("What the automation engine did, newest first.");
        automation.MapGet("/messages", MessagesAsync).WithSummary("Customer messages queued automatically (WhatsApp).");
        automation.MapPost("/messages/{id:guid}", MarkMessageAsync).WithSummary("Marks a message as sent or skipped.")
            .RequireAuthorization(StaffPolicies.Care);
        automation.MapPost("/run", RunAsync).WithSummary("Runs the automation sweep now for this company.")
            .RequireAuthorization(StaffPolicies.Operate);

        app.MapGet("/" + ShipmentsApiRoutes.Counters, CountersAsync)
            .WithTags("Automation & alerts")
            .WithSummary("Small counters for the side menu (alerts, requests, returns, trucks, messages).")
            .RequireAuthorization(StaffPolicies.Staff);
        return app;
    }

    private static async Task<Ok<AutomationOverview>> OverviewAsync(ShipmentsDbContext db, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var startOfDay = Api.StartOfBaghdadDay(clock.GetUtcNow());
        var alerts = await db.Alerts.AsNoTracking().Where(a => a.ResolvedAt == null)
            .GroupBy(_ => 1).Select(g => new { Open = g.Count(), Critical = g.Count(a => a.Severity == AlertSeverity.Critical) }).SingleOrDefaultAsync(ct);
        var log = await db.AutomationLog.AsNoTracking().Where(l => l.At >= startOfDay)
            .GroupBy(l => l.Rule).Select(g => new { Rule = g.Key, Count = g.Count() }).ToListAsync(ct);
        var pending = await db.Notifications.AsNoTracking().CountAsync(n => n.Status == NotificationStatus.Pending, ct);
        var lastSweep = await db.AutomationSettings.AsNoTracking().Select(s => s.LastSweepAt).SingleOrDefaultAsync(ct);
        int Count(AutomationRule rule) => log.FirstOrDefault(l => l.Rule == rule)?.Count ?? 0;

        return TypedResults.Ok(new AutomationOverview(alerts?.Open ?? 0, alerts?.Critical ?? 0, pending, log.Sum(l => l.Count),
            Count(AutomationRule.AutoReturn), Count(AutomationRule.AutoReschedule), Count(AutomationRule.RequestApplied), Count(AutomationRule.PlanTransfer), lastSweep));
    }

    private static async Task<IResult> GetPolicyAsync(HttpResponse response, AutomationPolicies policies, ITenantContext tenant, CancellationToken ct)
    {
        var policy = await policies.GetAsync(tenant.TenantId, ct);
        response.Headers.ETag = Api.ETag(policy.Version);
        return TypedResults.Ok(policy);
    }

    private static async Task<IResult> UpdatePolicyAsync(AutomationPolicy policy, HttpRequest http, ClaimsPrincipal user, ShipmentsDbContext db, AutomationPolicies policies, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        if (!Api.TryReadIfMatch(http, out var expected))
        {
            return Api.IfMatchRequired();
        }

        var settings = await policies.GetForUpdateAsync(tenant.TenantId, ct);
        if (db.Entry(settings).State != EntityState.Added && settings.Version != expected)
        {
            return Api.Stale("automation.stale");
        }

        settings.Update(policy, user.UserName(), clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Api.Stale("automation.stale");
        }

        http.HttpContext.Response.Headers.ETag = Api.ETag(settings.Version);
        return TypedResults.Ok(settings.ToPolicy());
    }

    private static async Task<Ok<AlertList>> AlertsAsync([AsParameters] AlertQuery query, ShipmentsDbContext db, CancellationToken ct)
    {
        var all = db.Alerts.AsNoTracking();
        var filtered = query.State switch
        {
            "closed" => all.Where(a => a.ResolvedAt != null),
            "all" => all,
            _ => all.Where(a => a.ResolvedAt == null),
        };
        if (query.Kind is { } kind)
        {
            filtered = filtered.Where(a => a.Kind == kind);
        }

        if (query.Severity is { } severity)
        {
            filtered = filtered.Where(a => a.Severity == severity);
        }

        if (!string.IsNullOrWhiteSpace(query.Hub))
        {
            filtered = filtered.Where(a => a.HubCode == query.Hub);
        }

        var total = await filtered.CountAsync(ct);
        var items = await filtered
            .OrderBy(a => a.ResolvedAt == null ? 0 : 1)
            .ThenBy(a => a.Severity == AlertSeverity.Critical ? 0 : a.Severity == AlertSeverity.Warning ? 1 : 2)
            .ThenByDescending(a => a.Hours)
            .ThenByDescending(a => a.RaisedAt)
            .Skip((query.EffectivePage - 1) * query.EffectivePageSize)
            .Take(query.EffectivePageSize)
            .Select(a => new OpsAlertItem(a.Id, a.Kind, a.Severity, a.ShipmentId, a.TrackingNumber, a.TransferId, a.TransferNumber, a.RequestId,
                a.RequestNumber, a.HubCode, a.Governorate, a.Hours, a.Detail, a.RaisedAt, a.ResolvedAt, a.ResolvedBy, a.AutoResolved))
            .ToListAsync(ct);
        var counts = await all.Where(a => a.ResolvedAt == null).GroupBy(a => a.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count(), Critical = g.Count(a => a.Severity == AlertSeverity.Critical) })
            .ToListAsync(ct);

        return TypedResults.Ok(new AlertList(items, total, query.EffectivePage, query.EffectivePageSize,
            counts.Sum(c => c.Critical), counts.Sum(c => c.Count - c.Critical), counts.Select(c => new AlertKindCount(c.Kind, c.Count)).ToList()));
    }

    private static async Task<IResult> ResolveAlertAsync(Guid id, ClaimsPrincipal user, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (alert is null)
        {
            return Api.NotFound("alert.not_found");
        }

        alert.Resolve(user.UserName(), automatic: false, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<AutomationLogPage>> LogAsync(int? page, int? pageSize, AutomationRule? rule, ShipmentsDbContext db, CancellationToken ct)
    {
        var size = Math.Clamp(pageSize ?? 50, 1, 200);
        var number = Paging.Page(page);
        var log = db.AutomationLog.AsNoTracking();
        if (rule is { } r)
        {
            log = log.Where(l => l.Rule == r);
        }

        var total = await log.CountAsync(ct);
        var items = await log.OrderByDescending(l => l.At).Skip((number - 1) * size).Take(size)
            .Select(l => new AutomationLogItem(l.Id, l.At, l.Rule, l.ShipmentId, l.TrackingNumber, l.Detail))
            .ToListAsync(ct);
        return TypedResults.Ok(new AutomationLogPage(items, total, number, size));
    }

    private static async Task<Ok<NotificationPage>> MessagesAsync(NotificationStatus? status, int? page, int? pageSize, ShipmentsDbContext db, CancellationToken ct)
    {
        var size = Math.Clamp(pageSize ?? 25, 1, 200);
        var number = Paging.Page(page);
        var all = db.Notifications.AsNoTracking();
        var filtered = status is { } st ? all.Where(n => n.Status == st) : all;
        var total = await filtered.CountAsync(ct);
        var items = await filtered.OrderByDescending(n => n.CreatedAt).Skip((number - 1) * size).Take(size)
            .Select(n => new CustomerNotificationItem(n.Id, n.ShipmentId, n.TrackingNumber, n.Template, n.Phone, n.Body, n.Status, n.CreatedAt, n.SentAt, n.SentBy))
            .ToListAsync(ct);
        var pending = await all.CountAsync(n => n.Status == NotificationStatus.Pending, ct);
        return TypedResults.Ok(new NotificationPage(items, total, number, size, pending));
    }

    private static async Task<IResult> MarkMessageAsync(Guid id, MarkNotificationRequest request, ClaimsPrincipal user, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var message = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (message is null)
        {
            return Api.NotFound("message.not_found");
        }

        message.Mark(request.Status, user.UserName(), clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RunAsync(AutomationEngine engine, ITenantContext tenant, CancellationToken ct)
    {
        var result = await engine.SweepAsync(tenant.TenantId, ct);
        return TypedResults.Ok(result);
    }

    private static async Task<Ok<OpsCounters>> CountersAsync(ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var alerts = await db.Alerts.AsNoTracking().Where(a => a.ResolvedAt == null)
            .GroupBy(_ => 1).Select(g => new { Open = g.Count(), Critical = g.Count(a => a.Severity == AlertSeverity.Critical) }).SingleOrDefaultAsync(ct);
        var requests = await db.Requests.AsNoTracking().Where(r => r.Status == RequestStatus.Open || r.Status == RequestStatus.InProgress)
            .GroupBy(_ => 1).Select(g => new { Open = g.Count(), Overdue = g.Count(r => r.DueAt < now) }).SingleOrDefaultAsync(ct);
        var returnsAtHub = await db.Shipments.AsNoTracking().CountAsync(s => s.Status == ShipmentStatus.ReturnAtHub, ct);
        var waiting = await ShipmentSearch.Filter(db.Shipments.AsNoTracking(), new ShipmentQuery { NeedsTransfer = true }, now).CountAsync(ct);
        var trucks = await db.Transfers.AsNoTracking().CountAsync(t => t.Status == TransferStatus.InTransit, ct);
        var pending = await db.Notifications.AsNoTracking().CountAsync(n => n.Status == NotificationStatus.Pending, ct);
        var confirmations = await db.Shipments.AsNoTracking().CountAsync(s => s.Confirmation == ConfirmationStatus.Pending && s.Status == ShipmentStatus.Booked, ct);
        return TypedResults.Ok(new OpsCounters(alerts?.Open ?? 0, alerts?.Critical ?? 0, requests?.Open ?? 0, requests?.Overdue ?? 0, returnsAtHub, waiting, trucks, pending, confirmations));
    }
}
