using Logistico.Modules.Merchants;
using Logistico.Modules.Shipments;
using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Geo;
using Logistico.SharedKernel.Identity;
using Logistico.SharedKernel.Setup;
using Logistico.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Web.Infrastructure;

/// <summary>What the setup page sends: the company, its first administrator, and whether to add sample data.</summary>
internal sealed record SetupRequest(CompanyProfile Company, string AdminName, string Email, string Password, bool SampleData);

internal enum SetupOutcome
{
    Done,
    AlreadySetUp,
    Rejected,
}

/// <summary>
/// Creates the first administrator and the company profile, once (docs/19): under a PostgreSQL advisory lock and after
/// checking again that no administrator exists, so two people submitting at the same moment end up with one.
/// </summary>
internal sealed class SetupService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    SetupGate gate,
    IServiceProvider services,
    IOperatingNetwork network,
    TimeProvider clock,
    ILogger<SetupService> logger)
{
    /// <summary>Sample orders added when asked: enough to see every screen at work.</summary>
    public const int SampleOrders = 150;

    /// <summary>An arbitrary, fixed advisory lock key for "setting up the installation".</summary>
    private const long LockKey = 0x4C58_5345_5455_5031; // "LXSETUP1"

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public ApplicationUser? Administrator { get; private set; }

    public async Task<SetupOutcome> RunAsync(SetupRequest request, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        var outcome = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockKey})", ct);
            if (await SetupGate.HasAdministratorAsync(db, ct))
            {
                return SetupOutcome.AlreadySetUp;
            }

            var admin = new ApplicationUser
            {
                UserName = request.Email.Trim(),
                Email = request.Email.Trim(),
                EmailConfirmed = true,
                DisplayName = request.AdminName.Trim(),
                TenantId = Tenants.Default,
            };
            var created = await users.CreateAsync(admin, request.Password);
            if (!created.Succeeded)
            {
                Errors = [.. created.Errors.Select(e => e.Description)];
                return SetupOutcome.Rejected;
            }

            var role = await users.AddToRoleAsync(admin, StaffRoles.Admin);
            if (!role.Succeeded)
            {
                Errors = [.. role.Errors.Select(e => e.Description)];
                return SetupOutcome.Rejected;
            }

            var profile = await db.CompanyProfiles.FirstOrDefaultAsync(p => p.TenantId == Tenants.Default, ct);
            if (profile is null)
            {
                profile = new CompanyProfileRecord { TenantId = Tenants.Default };
                db.CompanyProfiles.Add(profile);
            }

            profile.Write(request.Company, admin.Email, clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            Administrator = admin;
            return SetupOutcome.Done;
        });

        if (outcome != SetupOutcome.Done)
        {
            return outcome;
        }

        gate.Completed();
        await network.SetCountryAsync(Tenants.Default, request.Company.Country, CancellationToken.None);
        logger.LogInformation("Installation set up by {Email} for {Company}", request.Email, request.Company.Name);
        if (request.SampleData && Countries.IsIraq(request.Company.Country))
        {
            // Sample shops, orders, trucks and returns to learn with; seeding is idempotent and stays out of the audit trail.
            using var unaudited = AuditScope.Suppress();
            await services.SeedMerchantsDemoDataAsync();
            await services.SeedShipmentsDemoDataAsync(SampleOrders);
        }

        return SetupOutcome.Done;
    }
}
