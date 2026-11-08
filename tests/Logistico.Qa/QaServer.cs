using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Npgsql;

namespace Logistico.Qa;

[CollectionDefinition("Qa")]
public sealed class QaCollection : ICollectionFixture<QaServer>;

/// <summary>
/// The real server, as its own process on a real port, with a throw-away PostgreSQL database and the demo company
/// (demo shops, orders, staff and a demo seller). Set LOGISTICO_QA_URL to test a server that is already running
/// instead; LOGISTICO_TEST_DB chooses the PostgreSQL server, as for the integration tests.
/// </summary>
public class QaServer : IAsyncLifetime
{
    public const string Password = "Logistico#2026";

    private readonly string connectionString;
    private readonly string database;
    private Process? process;

    public QaServer()
    {
        var builder = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("LOGISTICO_TEST_DB")
            ?? "Host=localhost;Port=5432;Database=logistico_test;Username=logistico;Password=logistico_dev");
        database = $"logistico_qa_{Guid.NewGuid():N}";
        builder.Database = database;
        connectionString = builder.ConnectionString;
    }

    public Uri BaseAddress { get; private set; } = new("http://127.0.0.1/");

    /// <summary>Where the suite leaves its reports and the server's log.</summary>
    public static string ResultsFolder => Directory.CreateDirectory(Path.Combine(RepositoryRoot, "TestResults", "qa")).FullName;

    private static string RepositoryRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Logistico.slnx")))
            {
                folder = folder.Parent;
            }

            return folder?.FullName ?? throw new InvalidOperationException("Logistico.slnx not found above the test output.");
        }
    }

    /// <summary>What the server is started with: a company full of demo data.</summary>
    protected virtual IEnumerable<(string Key, string Value)> Settings =>
    [
        ("Seed__AdminEmail", "admin@logistico.local"),
        ("Seed__AdminPassword", Password),
        ("Seed__DemoShipments", "120"),
        ("Seed__DemoTeam", "true"),
    ];

    protected virtual string LogName => "server.log";

    public HttpClient CreateClient() => new() { BaseAddress = BaseAddress, Timeout = TimeSpan.FromSeconds(60) };

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("LOGISTICO_QA_URL") is { Length: > 0 } url)
        {
            BaseAddress = new Uri(url);
            return;
        }

        var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ? "Release" : "Debug";
        var app = Path.Combine(RepositoryRoot, "src", "Logistico.Web", "bin", configuration, "net10.0", "Logistico.Web.dll");
        var port = FreePort();
        BaseAddress = new Uri($"http://127.0.0.1:{port}/");

        var start = new ProcessStartInfo("dotnet", [app])
        {
            WorkingDirectory = Path.GetDirectoryName(app),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_URLS"] = BaseAddress.ToString().TrimEnd('/');
        start.Environment["ConnectionStrings__Logistico"] = connectionString;
        start.Environment["Database__MigrateOnStartup"] = "true";
        foreach (var (key, value) in Settings)
        {
            start.Environment[key] = value;
        }

        var log = new StreamWriter(Path.Combine(ResultsFolder, LogName)) { AutoFlush = true };
        process = Process.Start(start) ?? throw new InvalidOperationException("The server did not start.");
        process.OutputDataReceived += (_, line) => Write(log, line.Data);
        process.ErrorDataReceived += (_, line) => Write(log, line.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var client = CreateClient();
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException($"The server stopped while starting (exit code {process.ExitCode}); see {LogName} in {ResultsFolder}.");
            }

            try
            {
                if ((await client.GetAsync("/health/ready")).StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"The server was not ready after three minutes; see {LogName} in {ResultsFolder}.");
    }

    public async Task DisposeAsync()
    {
        if (process is null)
        {
            return;
        }

        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        process.Dispose();
        NpgsqlConnection.ClearAllPools();
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" };
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(builder.ConnectionString);
                await connection.OpenAsync();
                await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection);
                await drop.ExecuteNonQueryAsync();
                return;
            }
            catch (PostgresException) when (attempt < 5)
            {
                await Task.Delay(TimeSpan.FromSeconds(1)); // the killed server's connections are still closing
            }
        }
    }

    private static void Write(StreamWriter log, string? line)
    {
        if (line is not null)
        {
            lock (log)
            {
                log.WriteLine(line);
            }
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

/// <summary>A new installation: an empty database, no administrator, and a known setup code.</summary>
public sealed class FreshQaServer : QaServer
{
    public const string SetupCode = "QAFRESH1";

    protected override IEnumerable<(string Key, string Value)> Settings =>
    [
        ("Seed__AdminEmail", ""),
        ("Seed__AdminPassword", ""),
        ("Seed__DemoShipments", "0"),
        ("Seed__DemoTeam", "false"),
        ("Setup__Code", SetupCode),
    ];

    protected override string LogName => "server-fresh.log";
}
