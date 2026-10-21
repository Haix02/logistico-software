using System.Threading.RateLimiting;
using Logistico.Modules.Merchants;
using Logistico.Modules.Shipments;
using Logistico.SharedKernel;
using Logistico.Web.Components;
using Logistico.Web.Components.Account;
using Logistico.Web.Data;
using Logistico.Web.Endpoints;
using Logistico.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

var connectionString = builder.Configuration.GetConnectionString("Logistico");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'Logistico' is not configured (ConnectionStrings__Logistico).");
}

// The container image has no Kerberos libraries, so Npgsql's default attempt at GSS encryption only fails and logs an
// error; skip it unless the connection string asks for it.
var givenSettings = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };
if (!givenSettings.Keys.Cast<string>().Any(k => k.Replace(" ", "").Equals("GssEncryptionMode", StringComparison.OrdinalIgnoreCase)))
{
    connectionString = new Npgsql.NpgsqlConnectionStringBuilder(connectionString) { GssEncryptionMode = Npgsql.GssEncryptionMode.Disable }.ConnectionString;
}

// UI: Blazor WebAssembly client + server-rendered account pages
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();

// Languages: Arabic (default) and English
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("Logistico");
if (builder.Configuration["DataProtection:KeyRingPath"] is { Length: > 0 } keyRingPath)
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
}
else if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
{
    throw new InvalidOperationException(
        "DataProtection:KeyRingPath must point to a durable shared volume outside Development so authentication survives restarts and scale-out.");
}

// Identity (cookie authentication for the browser)
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "logistico.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(10);
    // API calls get 401/403 instead of a redirect to the login page.
    options.Events.OnRedirectToLogin = context => ApiAwareRedirect(context, StatusCodes.Status401Unauthorized);
    options.Events.OnRedirectToAccessDenied = context => ApiAwareRedirect(context, StatusCodes.Status403Forbidden);
});
// Roles: every module endpoint needs a staff role (see StaffAuthorization and SharedKernel/Identity/StaffRoles.cs).
builder.Services.AddAuthorizationBuilder().AddStaffPolicies();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, AppShellAuthorizationHandler>();

// Roles live in the sign-in cookie: re-check the account every minute so role changes and locks apply quickly.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.AddDbContext<ApplicationDbContext>(options => options
    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", ApplicationDbContext.Schema))
    .UseSnakeCaseNamingConvention());
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        options.Password.RequiredLength = 10;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders()
    .AddErrorDescriber<LocalizedIdentityErrors>()
    .AddClaimsPrincipalFactory<LogisticoClaimsPrincipalFactory>();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// Platform services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(new PostgresHealthCheck(connectionString));
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<PostgresHealthCheck>("postgres", failureStatus: HealthStatus.Unhealthy, tags: ["ready"], timeout: TimeSpan.FromSeconds(5));
// Behind a reverse proxy or load balancer the client address arrives in X-Forwarded-For. Without it every customer
// shares the proxy's address, and with it the per-address rate limits below. Only the listed proxies are trusted.
var trustedProxyNetworks = builder.Configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [];
if (trustedProxyNetworks.Length > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var network in trustedProxyNetworks)
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
    });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, ct) => new ValueTask(TooManyRequestsAsync(context.HttpContext, ct));
    // Sign-in, registration and password forms: 30 submissions a minute per client address slows password spraying
    // across accounts (each account also locks after 5 wrong passwords) while an office behind one address still works.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/Account")
            ? RateLimitPartition.GetFixedWindowLimiter(
                "account:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) })
            : RateLimitPartition.GetNoLimiter(""));
    // Public tracking is anonymous: 60 lookups per minute per client address.
    options.AddPolicy(ShipmentsModule.PublicTrackingRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
    // Customer self-service requests change orders: 10 per 10 minutes per client address.
    options.AddPolicy(ShipmentsModule.PublicRequestsRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            "requests:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(10) }));
});

// API answers (order lists, insights, CSV exports) are compressed: they shrink 5-10x, which matters on mobile data.
// Safe over HTTPS here because API bodies carry no secrets next to reflected input (no antiforgery tokens); pages are
// left alone, and static assets are served pre-compressed by MapStaticAssets.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    options.MimeTypes = ["application/json", "application/problem+json", "text/csv"];
});

// Modules
builder.Services.AddMerchantsModule(connectionString);
builder.Services.AddShipmentsModule(connectionString);

var app = builder.Build();

if (trustedProxyNetworks.Length > 0)
{
    app.UseForwardedHeaders();
}

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.UseMigrationsEndPoint();
    app.MapOpenApi();
}
else
{
    // API callers always receive RFC 7807 problem details; browser pages retain the friendly error screen.
    app.UseWhen(
        context => context.Request.Path.StartsWithSegments("/api"),
        branch => branch.UseExceptionHandler());
    app.UseWhen(
        context => !context.Request.Path.StartsWithSegments("/api"),
        branch => branch.UseExceptionHandler("/Error", createScopeForErrors: true));
    app.UseHsts();
}

await app.InitializeDatabaseAsync();

app.UseSecurityHeaders();
app.UseWhen(context => context.Request.Path.StartsWithSegments("/api"), branch => branch.UseResponseCompression());
app.UseRequestLocalization(LocalizationSetup.Options());

// Friendly HTML "not found" pages for the UI only; API responses and static assets stay 404.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/health")
        && !context.Request.Path.StartsWithSegments("/_framework")
        && !Path.HasExtension(context.Request.Path.Value),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
// Local development intentionally supports the HTTP launch profile. Test hosts do not expose an HTTPS
// endpoint either; deployed environments do, so redirect only when one is guaranteed to exist.
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

// Routing runs after the status-code page middleware so a re-executed "/not-found" finds its page;
// authentication and authorization must then come after routing explicitly.
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    AllowCachingResponses = false,
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    AllowCachingResponses = false,
}).AllowAnonymous();

app.MapCultureSwitch();
app.MapAccountEndpoints();
app.MapTeamEndpoints();

// Module APIs: reading needs a staff role, changing data needs Operations or Admin unless an endpoint asks for more.
var modules = app.MapGroup("").RequireStaffRoles();
modules.MapMerchantsModule();
modules.MapShipmentsModule();

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Logistico.Web.Client._Imports).Assembly);

// Endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.Run();

static Task ApiAwareRedirect(Microsoft.AspNetCore.Authentication.RedirectContext<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions> context, int apiStatusCode)
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }

    context.Response.StatusCode = apiStatusCode;
    if (apiStatusCode != StatusCodes.Status403Forbidden)
    {
        return Task.CompletedTask;
    }

    // A signed-in user whose role does not allow this: say so in their language (RFC 9457 problem details).
    var texts = context.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<Logistico.Web.ServerText>>();
    return context.Response.WriteAsJsonAsync(
        new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = apiStatusCode,
            Title = texts["Forbidden"],
            Type = "https://logistico.app/problems/auth.forbidden",
            Extensions = { ["code"] = "auth.forbidden" },
        },
        options: null,
        contentType: "application/problem+json");
}

// A rejected request learns why, in the user's language: problem details for the API, plain text for forms.
static Task TooManyRequestsAsync(HttpContext context, CancellationToken ct)
{
    var title = context.RequestServices.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<Logistico.Web.ServerText>>()["TooManyRequests"];
    context.Response.Headers.RetryAfter = "60";
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync(title, ct);
    }

    return context.Response.WriteAsJsonAsync(
        new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = title,
            Type = "https://logistico.app/problems/rate_limited",
            Extensions = { ["code"] = "rate_limited" },
        },
        options: null,
        contentType: "application/problem+json",
        cancellationToken: ct);
}

public partial class Program;
