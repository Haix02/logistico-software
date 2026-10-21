using System.Security.Claims;
using System.Text.Encodings.Web;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Logistico.Web.IntegrationTests;

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<LogisticoApiFactory>;

/// <summary>
/// Boots the real host against its own throw-away PostgreSQL database, so test classes can run in parallel
/// without migrating the same database at the same time. Server: LOGISTICO_TEST_DB, else the local development server.
/// The database name gets a unique suffix per factory and is dropped when the test class finishes.
/// </summary>
public sealed class LogisticoApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid OtherTenant = new("01990000-0000-7000-8000-0000000000ff");

    private readonly string _connectionString;
    private readonly string _database;

    /// <summary>The server's clock; tests move it forward to make parcels late or stuck.</summary>
    public TestClock Clock { get; } = new();

    public LogisticoApiFactory()
    {
        var builder = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("LOGISTICO_TEST_DB")
            ?? "Host=localhost;Port=5432;Database=logistico_test;Username=logistico;Password=logistico_dev");
        _database = $"{builder.Database ?? "logistico_test"}_{Guid.NewGuid():N}"[..Math.Min(63, (builder.Database ?? "logistico_test").Length + 33)];
        builder.Database = _database;
        _connectionString = builder.ConnectionString;
    }

    public Task InitializeAsync()
    {
        _ = Services; // start the host: it creates and migrates the database
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        var builder = new NpgsqlConnectionStringBuilder(_connectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// An API client signed in as <paramref name="user"/> of <paramref name="tenant"/> with a staff role (Admin unless given;
    /// "" for none). <paramref name="userId"/> ties it to a real account when the test needs "who am I" (the team screen);
    /// <paramref name="courierCode"/> links a courier account to its courier.
    /// </summary>
    public HttpClient CreateUserClient(string user = "agent@logistico.test", Guid? tenant = null, string role = StaffRoles.Admin, string? userId = null, string? courierCode = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user);
        if (userId is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId);
        }

        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, (tenant ?? Tenants.Default).ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        if (courierCode is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.CourierHeader, courierCode);
        }

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Logistico", _connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Seed:DemoShipments", "0");
        builder.UseSetting("Automation:Enabled", "false"); // tests run the automation sweep through the API

        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthorizationOptions>(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder(TestAuthHandler.SchemeName, IdentityConstants.ApplicationScheme)
                    .RequireAuthenticatedUser()
                    .Build();

                // The real policies (roles, tenant claim), also accepting the test scheme.
                foreach (var policyName in StaffPolicies.All)
                {
                    var policy = options.GetPolicy(policyName)
                        ?? throw new InvalidOperationException($"Missing authorization policy {policyName}.");
                    options.AddPolicy(policyName, new AuthorizationPolicyBuilder(policy)
                        .AddAuthenticationSchemes(TestAuthHandler.SchemeName, IdentityConstants.ApplicationScheme)
                        .Build());
                }
            });
        });
    }
}

/// <summary>Authenticates requests that carry the test headers; anonymous otherwise.</summary>
internal sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string TenantHeader = "X-Test-Tenant";
    public const string RoleHeader = "X-Test-Role";
    public const string UserIdHeader = "X-Test-UserId";
    public const string CourierHeader = "X-Test-Courier";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        List<Claim> claims =
        [
            new(ClaimTypes.Name, user.ToString()),
            new(ClaimTypes.NameIdentifier, Request.Headers.TryGetValue(UserIdHeader, out var id) ? id.ToString() : user.ToString()),
            new(LogisticoClaimTypes.TenantId, Request.Headers[TenantHeader].ToString()),
        ];
        if (Request.Headers[RoleHeader].ToString() is { Length: > 0 } role)
        {
            claims.Add(new(ClaimTypes.Role, role));
        }

        if (Request.Headers[CourierHeader].ToString() is { Length: > 0 } courier)
        {
            claims.Add(new(LogisticoClaimTypes.CourierCode, courier));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

/// <summary>The real clock plus an offset that tests can advance.</summary>
public sealed class TestClock : TimeProvider
{
    private TimeSpan _offset;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + _offset;

    public void Advance(TimeSpan by) => _offset += by;
}
