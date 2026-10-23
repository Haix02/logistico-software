using System.Security.Claims;
using Logistico.Modules.Audit;
using Logistico.Modules.Merchants;
using Logistico.Modules.Merchants.Contracts;
using Logistico.Modules.Shipments;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Logistico.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Web.Infrastructure;

/// <summary>
/// Applies migrations when enabled by configuration (Development by default), then performs explicitly
/// configured bootstrap work. Production applies migrations from CI/CD bundles but can still provision its
/// first administrator through environment-backed configuration.
/// </summary>
internal static class DatabaseInitializer
{
    /// <summary>Development accounts for trying each role (Seed:DemoTeam), signed in with the administrator's password.</summary>
    private static readonly (string Email, string DisplayName, string Role, string? CourierCode)[] DemoTeam =
    [
        ("ops@logistico.local", "حيدر — العمليات", StaffRoles.Operations, null),
        ("support@logistico.local", "نور — خدمة الزبائن", StaffRoles.CustomerSupport, null),
        ("finance@logistico.local", "زينب — المالية", StaffRoles.Finance, null),
        ("viewer@logistico.local", "مصطفى — مشاهد", StaffRoles.Viewer, null),
        ("courier@logistico.local", "مصطفى — مندوب", StaffRoles.Courier, "Baghdad-01"), // the demo courier "مصطفى (Baghdad-01)"
    ];

    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        var config = app.Configuration;
        if (config.GetValue<bool>("Database:MigrateOnStartup"))
        {
            await using (var scope = app.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
            }

            await app.Services.MigrateAuditDatabaseAsync();
            await app.Services.MigrateMerchantsDatabaseAsync();
            await app.Services.MigrateShipmentsDatabaseAsync();
        }

        // What start-up writes comes from configuration, not from someone's change: it stays out of the audit trail.
        using var unaudited = AuditScope.Suppress();

        // The staff roles are reference data: every company needs them before anyone can be given a role.
        await EnsureStaffRolesAsync(app.Services);

        if (config["Seed:AdminEmail"] is { Length: > 0 } email && config["Seed:AdminPassword"] is { Length: > 0 } password)
        {
            await SeedStaffAsync(app.Services, email, password, "مدير النظام", StaffRoles.Admin, app.Logger);
            if (config.GetValue<bool>("Seed:DemoTeam"))
            {
                foreach (var (demoEmail, name, role, courierCode) in DemoTeam)
                {
                    await SeedStaffAsync(app.Services, demoEmail, password, name, role, app.Logger,
                        courierCode is null ? null : new Claim(LogisticoClaimTypes.CourierCode, courierCode));
                }
            }
        }

        var demoShipments = config.GetValue<int>("Seed:DemoShipments");
        if (demoShipments > 0)
        {
            await app.Services.SeedMerchantsDemoDataAsync();
            await app.Services.SeedShipmentsDemoDataAsync(demoShipments);
            if (config.GetValue<bool>("Seed:DemoTeam") && config["Seed:AdminPassword"] is { Length: > 0 } sellerPassword)
            {
                await SeedDemoSellerAsync(app, sellerPassword);
            }
        }
    }

    /// <summary>A seller-portal account for the demo shop "بوتيك لمار" (its orders are part of the demo data).</summary>
    private static async Task SeedDemoSellerAsync(WebApplication app, string password)
    {
        IReadOnlyList<MerchantLookupItem> shops;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            shops = await scope.ServiceProvider.GetRequiredService<IMerchantDirectory>().ListActiveAsync(CancellationToken.None);
        }

        if ((shops.FirstOrDefault(m => m.InstagramHandle?.TrimStart('@') == "lamar.boutique") ?? shops.FirstOrDefault()) is { } shop)
        {
            await SeedStaffAsync(app.Services, "seller@logistico.local", password, "لمار — " + shop.Name, StaffRoles.Seller, app.Logger,
                new Claim(LogisticoClaimTypes.MerchantId, shop.Id.ToString()));
        }
    }

    private static async Task EnsureStaffRolesAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in StaffRoles.Assignable)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new IdentityRole(role));
            }
        }
    }

    /// <summary>Creates the account if missing and makes sure it has the role (an existing account keeps its password).</summary>
    private static async Task SeedStaffAsync(IServiceProvider services, string email, string password, string displayName, string role, ILogger logger, Claim? link = null)
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = displayName };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Seeding the {role} account failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            logger.LogInformation("Seeded {Role} account {Email}", role, email);
        }

        if (!await users.IsInRoleAsync(user, role))
        {
            var roleResult = await users.AddToRoleAsync(user, role);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException($"Assigning the {role} role failed: " + string.Join("; ", roleResult.Errors.Select(e => e.Description)));
            }
        }

        // A courier or seller account is linked to its courier or shop.
        if (link is not null && !(await users.GetClaimsAsync(user)).Any(c => c.Type == link.Type))
        {
            await users.AddClaimAsync(user, link);
        }
    }
}
