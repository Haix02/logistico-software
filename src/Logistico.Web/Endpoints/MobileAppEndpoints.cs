using System.Security.Claims;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Logistico.SharedKernel.Mobile;
using Logistico.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Logistico.Web.Endpoints;

/// <summary>
/// The mobile app's start-up and its settings (ADR-031): the app asks what to show for the signed-in account; the
/// company's administrators decide, per kind of account, which parts exist, how scanning works, and what it says.
/// </summary>
internal static class MobileAppEndpoints
{
    public static IEndpointRouteBuilder MapMobileAppEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/" + MobileApiRoutes.Config, ConfigAsync).WithTags("Mobile app")
            .WithSummary("What the app shows for the signed-in account: its kind, modules, scanning, branding and messages.")
            .RequireAuthorization(StaffPolicies.Member);
        app.MapGet("/" + MobileApiRoutes.Settings, SettingsAsync).WithTags("Mobile app")
            .WithSummary("The company's mobile app settings, with an ETag.")
            .RequireAuthorization(StaffPolicies.Administer);
        app.MapPut("/" + MobileApiRoutes.Settings, SaveAsync).WithTags("Mobile app")
            .WithSummary("Saves the company's mobile app settings (If-Match required once they exist).")
            .RequireAuthorization(StaffPolicies.Administer);
        return app;
    }

    private static async Task<Ok<MobileAppConfig>> ConfigAsync(ClaimsPrincipal user, ApplicationDbContext db, ITenantContext tenant, IAuthorizationService authorization, CancellationToken ct)
    {
        var settings = (await db.MobileAppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenant.TenantId, ct))?.Read() ?? new MobileAppSettings();
        var role = user.FindFirstValue(ClaimTypes.Role) ?? "";
        var kind = AppKinds.Of(role);
        var chosen = kind switch
        {
            AppKinds.Sender => settings.SenderModules,
            AppKinds.Courier => settings.CourierModules,
            _ => settings.ManagerModules,
        };

        // Only what this account may really use, in the app's order.
        var modules = new List<string>();
        foreach (var module in AppModules.Of(kind).Where(chosen.Contains))
        {
            if ((await authorization.AuthorizeAsync(user, AppModules.PolicyFor(module))).Succeeded)
            {
                modules.Add(module);
            }
        }

        IReadOnlyList<ScannerIntent> intents = settings.ScannerAction is { Length: > 0 } action && settings.ScannerExtra is { Length: > 0 } extra
            ? [.. ScannerIntents.Known, new ScannerIntent(action.Trim(), extra.Trim())]
            : ScannerIntents.Known;
        var account = new AppAccount(
            user.Identity?.Name ?? "",
            user.FindFirstValue(LogisticoClaimTypes.DisplayName) ?? user.Identity?.Name ?? "",
            role,
            user.FindFirstValue(LogisticoClaimTypes.CourierCode),
            Guid.TryParse(user.FindFirstValue(LogisticoClaimTypes.MerchantId), out var shop) ? shop : null);
        return TypedResults.Ok(new MobileAppConfig(
            kind, modules, account, string.IsNullOrWhiteSpace(settings.AppName) ? "Logistico" : settings.AppName.Trim(), settings.PrimaryColor,
            settings.CameraScanning, settings.RequireSignature, intents, Clean(settings.Announcement), settings.MinimumVersion, Clean(settings.SupportPhone)));
    }

    private static async Task<Ok<MobileAppSettingsView>> SettingsAsync(HttpResponse response, ApplicationDbContext db, ITenantContext tenant, CancellationToken ct)
    {
        var record = await db.MobileAppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenant.TenantId, ct);
        if (record is not null)
        {
            response.Headers.ETag = $"\"{record.Version}\"";
        }

        return TypedResults.Ok(new MobileAppSettingsView(record?.Read() ?? new MobileAppSettings(), record?.Version, record?.UpdatedAt, record?.UpdatedBy));
    }

    private static async Task<IResult> SaveAsync(
        MobileAppSettings settings, HttpRequest request, HttpResponse response, ClaimsPrincipal user, ApplicationDbContext db, ITenantContext tenant, TimeProvider clock,
        CancellationToken ct)
    {
        if (Invalid(settings) is { } problem)
        {
            return problem;
        }

        var record = await db.MobileAppSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.TenantId, ct);
        if (record is null)
        {
            record = new MobileAppSettingsRecord { TenantId = tenant.TenantId };
            db.MobileAppSettings.Add(record);
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

        record.Write(settings, user.Identity?.Name, clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Changed by someone else");
        }

        response.Headers.ETag = $"\"{record.Version}\"";
        return TypedResults.Ok(new MobileAppSettingsView(record.Read(), record.Version, record.UpdatedAt, record.UpdatedBy));
    }

    private static ValidationProblem? Invalid(MobileAppSettings settings)
    {
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (System.ComponentModel.DataAnnotations.Validator.TryValidateObject(settings, new(settings), results, validateAllProperties: true))
        {
            return null;
        }

        return TypedResults.ValidationProblem(results
            .SelectMany(r => r.MemberNames.DefaultIfEmpty("").Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(x => x.Member)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "").ToArray()));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
