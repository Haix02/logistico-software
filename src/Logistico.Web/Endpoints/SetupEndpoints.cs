using System.Security.Claims;
using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Logistico.SharedKernel.Setup;
using Logistico.Web.Components.Account;
using Logistico.Web.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Logistico.Web.Endpoints;

/// <summary>
/// After the first run (docs/19): the company profile, the getting-started guide worked out from the company's data,
/// and the installation's health checks.
/// </summary>
internal static class SetupEndpoints
{
    /// <summary>The Team page deactivates an account by locking it until the year 9999.</summary>
    private static readonly DateTimeOffset Deactivated = new(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        var company = app.MapGroup("/" + SetupApiRoutes.Company).WithTags("Company");
        company.MapGet("/", GetCompanyAsync).WithSummary("The company's profile, with an ETag.").RequireAuthorization(StaffPolicies.Staff);
        company.MapPut("/", SaveCompanyAsync).WithSummary("Saves the company's profile (If-Match required once it exists).").RequireAuthorization(StaffPolicies.Administer);

        var guide = app.MapGroup("/" + SetupApiRoutes.GettingStarted).WithTags("Getting started");
        guide.MapGet("/", GuideAsync).WithSummary("The steps of setting up the company that the signed-in person can do, and which are done.")
            .RequireAuthorization(StaffPolicies.Staff);
        guide.MapPost("/steps/{step}/skip", (string step, ClaimsPrincipal user, ApplicationDbContext db, ITenantContext tenant, TimeProvider clock, CancellationToken ct) =>
                SkipAsync(step, true, user, db, tenant, clock, ct))
            .WithSummary("Marks a step as not needed by this company.").RequireAuthorization(StaffPolicies.Operate);
        guide.MapDelete("/steps/{step}/skip", (string step, ClaimsPrincipal user, ApplicationDbContext db, ITenantContext tenant, TimeProvider clock, CancellationToken ct) =>
                SkipAsync(step, false, user, db, tenant, clock, ct))
            .WithSummary("Brings a skipped step back.").RequireAuthorization(StaffPolicies.Operate);

        app.MapGet("/" + SetupApiRoutes.SystemChecks, ChecksAsync).WithTags("Installation health")
            .WithSummary("What the installation needs before real customers' data goes in, checked now.")
            .RequireAuthorization(StaffPolicies.Administer);
        return app;
    }

    private static async Task<Ok<CompanyProfileView>> GetCompanyAsync(HttpResponse response, ApplicationDbContext db, ITenantContext tenant, CancellationToken ct)
    {
        var record = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.TenantId == tenant.TenantId, ct);
        if (record is not null)
        {
            response.Headers.ETag = $"\"{record.Version}\"";
        }

        return TypedResults.Ok(View(record));
    }

    private static async Task<IResult> SaveCompanyAsync(
        CompanyProfile profile, HttpRequest request, HttpResponse response, ClaimsPrincipal user, ApplicationDbContext db, ITenantContext tenant,
        IOperatingNetwork network, TimeProvider clock, CancellationToken ct)
    {
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(profile, new(profile), results, validateAllProperties: true))
        {
            return TypedResults.ValidationProblem(results
                .SelectMany(r => r.MemberNames.DefaultIfEmpty("").Select(m => (Member: m, r.ErrorMessage)))
                .GroupBy(x => x.Member)
                .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "").ToArray()));
        }

        var record = await db.CompanyProfiles.FirstOrDefaultAsync(p => p.TenantId == tenant.TenantId, ct);
        if (record is null)
        {
            record = new CompanyProfileRecord { TenantId = tenant.TenantId };
            db.CompanyProfiles.Add(record);
        }
        else
        {
            // Never over a colleague's newer changes.
            if (!EntityTagHeaderValue.TryParse(request.Headers.IfMatch.ToString(), out var etag) || !uint.TryParse(etag.Tag.ToString().Trim('"'), out var expected))
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "If-Match required");
            }

            if (record.Version != expected)
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Changed by someone else");
            }

            db.Entry(record).Property(r => r.Version).OriginalValue = expected;
        }

        record.Write(profile, user.Identity?.Name, clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Changed by someone else");
        }

        await network.SetCountryAsync(tenant.TenantId, record.Country, ct);
        response.Headers.ETag = $"\"{record.Version}\"";
        return TypedResults.Ok(View(record));
    }

    private static async Task<Ok<GettingStartedView>> GuideAsync(
        ClaimsPrincipal user, ApplicationDbContext db, IEnumerable<IGettingStartedFacts> modules, IServiceProvider services, ITenantContext tenant,
        TimeProvider clock, CancellationToken ct)
    {
        var role = user.FindFirstValue(ClaimTypes.Role) ?? "";
        var mine = GettingStartedSteps.All.Where(s => GettingStartedSteps.For(role, s.Roles)).ToList();
        var profile = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.TenantId == tenant.TenantId, ct);

        var done = new HashSet<string>();
        foreach (var module in modules)
        {
            done.UnionWith(await module.DoneAsync(ct));
        }

        if (profile?.IsSet == true)
        {
            done.Add(GettingStartedSteps.Company);
        }

        if (await ActiveAccounts(db, tenant.TenantId).CountAsync(ct) >= 2)
        {
            done.Add(GettingStartedSteps.Team);
        }

        if (await db.MobileAppSettings.AnyAsync(s => s.TenantId == tenant.TenantId, ct))
        {
            done.Add(GettingStartedSteps.MobileApp);
        }

        if (mine.Any(s => s.Id == GettingStartedSteps.Health) && !(await CheckAsync(services, db, tenant.TenantId, clock, ct)).HasProblems)
        {
            done.Add(GettingStartedSteps.Health);
        }

        var skipped = profile?.SkippedSteps ?? [];
        return TypedResults.Ok(new GettingStartedView([.. mine.Select(s => new GettingStartedStep(s.Id, s.Href, s.Icon, done.Contains(s.Id), skipped.Contains(s.Id)))]));
    }

    private static async Task<IResult> SkipAsync(string step, bool skip, ClaimsPrincipal user, ApplicationDbContext db, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        if (!GettingStartedSteps.IsKnown(step))
        {
            return TypedResults.NotFound();
        }

        var record = await db.CompanyProfiles.FirstOrDefaultAsync(p => p.TenantId == tenant.TenantId, ct);
        if (record is null)
        {
            record = new CompanyProfileRecord { TenantId = tenant.TenantId, UpdatedAt = clock.GetUtcNow(), UpdatedBy = user.Identity?.Name };
            db.CompanyProfiles.Add(record);
        }

        record.SkippedSteps.Remove(step);
        if (skip)
        {
            record.SkippedSteps.Add(step);
        }

        // A list column is compared by reference: tell EF Core it changed.
        db.Entry(record).Property(r => r.SkippedSteps).IsModified = true;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two people skipping at the same moment: one of them wins, which is the same answer.
        }

        return TypedResults.NoContent();
    }

    private static async Task<Ok<SystemChecksView>> ChecksAsync(IServiceProvider services, ApplicationDbContext db, ITenantContext tenant, TimeProvider clock, CancellationToken ct) =>
        TypedResults.Ok(await CheckAsync(services, db, tenant.TenantId, clock, ct));

    /// <summary>The installation health checks (docs/15 and docs/19 §4).</summary>
    private static async Task<SystemChecksView> CheckAsync(IServiceProvider services, ApplicationDbContext db, Guid tenantId, TimeProvider clock, CancellationToken ct)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var environment = services.GetRequiredService<IWebHostEnvironment>();
        var request = services.GetRequiredService<IHttpContextAccessor>().HttpContext?.Request;
        var checks = new List<SystemCheck>();
        void Add(string id, SystemCheckState state, params string[] values) => checks.Add(new SystemCheck(id, state, values));

        var version = await db.Database.SqlQueryRaw<string>("SELECT current_setting('server_version') AS \"Value\"").SingleAsync(ct);
        Add("database", int.TryParse(version.Split('.')[0], out var major) && major >= 16 ? SystemCheckState.Ok : SystemCheckState.Warning, version);

        var pending = new List<string>(await db.Database.GetPendingMigrationsAsync(ct));
        foreach (var module in services.GetServices<IDatabaseHealth>())
        {
            pending.AddRange(await module.PendingMigrationsAsync(ct));
        }

        Add("migrations", pending.Count == 0 ? SystemCheckState.Ok : SystemCheckState.Problem, pending.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var collation = await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_collation WHERE collname = 'natural'").SingleAsync(ct);
        Add("collation", collation > 0 ? SystemCheckState.Ok : SystemCheckState.Problem);

        var development = environment.IsDevelopment();
        Add("https", request?.IsHttps == true ? SystemCheckState.Ok : development ? SystemCheckState.Info : SystemCheckState.Problem);

        var proxies = config.GetSection("ReverseProxy:KnownNetworks").GetChildren().Any(c => !string.IsNullOrWhiteSpace(c.Value));
        var forwarded = request?.Headers.ContainsKey("X-Forwarded-For") == true;
        Add("proxy", proxies ? SystemCheckState.Ok : forwarded ? SystemCheckState.Warning : SystemCheckState.Info);

        var keys = config["DataProtection:KeyRingPath"];
        Add("keys", !string.IsNullOrWhiteSpace(keys) ? SystemCheckState.Ok : development ? SystemCheckState.Info : SystemCheckState.Problem);

        var email = services.GetRequiredService<IEmailSender<ApplicationUser>>() is IdentityNoOpEmailSender;
        Add("email", email ? SystemCheckState.Warning : SystemCheckState.Ok);

        Add("bootstrap", string.IsNullOrEmpty(config["Seed:AdminPassword"]) ? SystemCheckState.Ok : development ? SystemCheckState.Info : SystemCheckState.Warning);
        Add("demo", config.GetValue<int>("Seed:DemoShipments") > 0 && !development ? SystemCheckState.Warning : SystemCheckState.Ok);

        var admins = await (from user in ActiveAccounts(db, tenantId)
                            join link in db.UserRoles on user.Id equals link.UserId
                            join role in db.Roles on link.RoleId equals role.Id
                            where role.Name == StaffRoles.Admin
                            select user.Id).CountAsync(ct);
        Add("admins", admins >= 2 ? SystemCheckState.Ok : SystemCheckState.Warning, admins.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var accounts = await ActiveAccounts(db, tenantId).CountAsync(ct);
        var twoStep = await ActiveAccounts(db, tenantId).CountAsync(u => u.TwoFactorEnabled, ct);
        Add("two-step", twoStep == accounts ? SystemCheckState.Ok : SystemCheckState.Info,
            twoStep.ToString(System.Globalization.CultureInfo.InvariantCulture), accounts.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Add("automation", config.GetValue("Automation:Enabled", true) ? SystemCheckState.Ok : SystemCheckState.Warning);
        return new SystemChecksView(checks, clock.GetUtcNow());
    }

    private static IQueryable<ApplicationUser> ActiveAccounts(ApplicationDbContext db, Guid tenantId) =>
        db.Users.Where(u => u.TenantId == tenantId && (u.LockoutEnd == null || u.LockoutEnd < Deactivated));

    private static CompanyProfileView View(CompanyProfileRecord? record) =>
        new(record?.Read() ?? new CompanyProfile(), record?.IsSet == true, record?.UpdatedAt, record?.UpdatedBy, record?.Version ?? 0);
}
