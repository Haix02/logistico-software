using System.Security.Cryptography;
using System.Text;
using Logistico.SharedKernel.Identity;
using Logistico.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Web.Infrastructure;

/// <summary>
/// Whether the installation still needs its first administrator (docs/19), and the one-time setup code that proves the
/// person setting it up runs the server: it is written to the server's log, never shown on a page. Once an
/// administrator exists the answer is remembered until the app restarts.
/// </summary>
internal sealed class SetupGate(IServiceScopeFactory scopes, IConfiguration config, ILogger<SetupGate> logger)
{
    /// <summary>Letters and digits that cannot be mistaken for each other when read from a log (no 0/O, 1/I/L).</summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private readonly Lazy<string> code = new(() => Normalize(config["Setup:Code"]) is { Length: >= 8 } configured ? configured : Generate());
    /// <summary>The Team page deactivates an account by locking it until the year 9999.</summary>
    private static readonly DateTimeOffset Deactivated = new(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private volatile bool setUp;

    public async Task<bool> NeedsSetupAsync(CancellationToken ct = default)
    {
        if (setUp)
        {
            return false;
        }

        await using var scope = scopes.CreateAsyncScope();
        var exists = await HasAdministratorAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), ct);
        setUp = exists;
        return !exists;
    }

    /// <summary>The setup is done: from now on the setup page is closed.</summary>
    public void Completed() => setUp = true;

    /// <summary>An administrator account that has not been deactivated (a temporary lock-out after wrong passwords still counts).</summary>
    public static Task<bool> HasAdministratorAsync(ApplicationDbContext db, CancellationToken ct) =>
        (from user in db.Users
         join link in db.UserRoles on user.Id equals link.UserId
         join role in db.Roles on link.RoleId equals role.Id
         where role.Name == StaffRoles.Admin && (user.LockoutEnd == null || user.LockoutEnd < Deactivated)
         select user.Id).AnyAsync(ct);

    /// <summary>Compares in constant time, ignoring case, spaces and dashes the way people copy codes.</summary>
    public bool Accepts(string? candidate)
    {
        var expected = Encoding.ASCII.GetBytes(code.Value);
        var given = Encoding.ASCII.GetBytes(Normalize(candidate) ?? "");
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    /// <summary>Writes the setup code to the log when the installation has no administrator yet.</summary>
    public async Task AnnounceAsync(CancellationToken ct = default)
    {
        if (await NeedsSetupAsync(ct))
        {
            logger.LogWarning("Logistico has no administrator yet. Open /setup and enter the setup code {SetupCode}", Display(code.Value));
        }
    }

    private static string Display(string value) => value.Length == 8 ? $"{value[..4]}-{value[4..]}" : value;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new string([.. value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    private static string Generate() => string.Create(8, 0, (chars, _) =>
    {
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
    });
}
