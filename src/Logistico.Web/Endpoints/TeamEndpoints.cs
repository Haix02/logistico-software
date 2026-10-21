using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Logistico.Web.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Logistico.Web.Endpoints;

/// <summary>
/// The company's staff accounts, for administrators: add people with a role, change roles, lock and unlock, and set a
/// new password for someone who forgot theirs. Only the administrator's own company is visible. Nobody can change
/// their own access here, and a company always keeps at least one active administrator.
/// </summary>
/// <remarks>
/// Every change runs in one transaction that first takes a per-company lock, so two administrators acting at the same
/// moment are applied one after the other (two of them cannot lock each other out), and a change that fails half-way
/// leaves the account as it was.
/// </remarks>
internal static class TeamEndpoints
{
    public static IEndpointRouteBuilder MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var team = app.MapGroup("/" + TeamApiRoutes.Team)
            .WithTags("Team")
            .RequireAuthorization(StaffPolicies.Administer);
        team.MapGet("/", ListAsync).WithSummary("Staff accounts of the company with their role.");
        team.MapPost("/", CreateAsync).WithSummary("Adds a staff member with a role and a first password.");
        team.MapPut("/{id}/role", ChangeRoleAsync).WithSummary("Changes a member's role, or removes their access (role null).");
        team.MapPost("/{id}/lock", LockAsync).WithSummary("Blocks a member from signing in; their open sessions end within a minute.");
        team.MapPost("/{id}/unlock", UnlockAsync).WithSummary("Lets a locked member sign in again (also clears a lockout after wrong passwords).");
        team.MapPost("/{id}/password", ResetPasswordAsync).WithSummary("Sets a new password for a member; their open sessions end within a minute.");
        return app;
    }

    private static async Task<Ok<TeamList>> ListAsync(ApplicationDbContext db, ITenantContext tenant, ClaimsPrincipal user, TimeProvider clock, CancellationToken ct)
    {
        var members = await MembersAsync(db, tenant.TenantId, ct);
        var me = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var now = clock.GetUtcNow();
        return TypedResults.Ok(new TeamList(members
            .Select(m => new TeamMember(m.Id, m.Email, m.DisplayName, m.Role, IsDeactivated(m.LockoutEnd), m.TwoFactorEnabled, m.Id == me,
                LockedOutUntil: !IsDeactivated(m.LockoutEnd) && m.LockoutEnd > now ? m.LockoutEnd : null,
                CourierCode: m.Role == StaffRoles.Courier ? m.CourierCode : null,
                MerchantId: m.Role == StaffRoles.Seller ? m.MerchantId : null))
            .OrderBy(m => m.Role is null ? int.MaxValue : RoleRank(m.Role))
            .ThenBy(m => m.DisplayName ?? m.Email, StringComparer.CurrentCultureIgnoreCase)
            .ToList()));
    }

    private static async Task<IResult> CreateAsync(
        CreateTeamMemberRequest request,
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        ITenantContext tenant,
        IStringLocalizer<ServerText> texts,
        CancellationToken ct)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        await using var change = await BeginAsync(db, tenant.TenantId, ct);
        var email = request.Email.Trim();
        if (await users.FindByNameAsync(email) is not null || await users.FindByEmailAsync(email) is not null)
        {
            return Problem(StatusCodes.Status409Conflict, "team.email_taken", texts);
        }

        var member = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = request.DisplayName.Trim(),
            TenantId = tenant.TenantId,
        };
        var created = await users.CreateAsync(member, request.Password);
        if (!created.Succeeded)
        {
            return PasswordProblem(created);
        }

        Ensure(await users.AddToRoleAsync(member, request.Role));
        var (courier, merchant) = await LinkAsync(users, member, request.Role, request.CourierCode, request.MerchantId);
        await change.CommitAsync(ct);
        return TypedResults.Created($"/{TeamApiRoutes.Team}/{member.Id}",
            new TeamMember(member.Id, email, member.DisplayName, request.Role, false, false, false, CourierCode: courier, MerchantId: merchant));
    }

    private static async Task<IResult> ChangeRoleAsync(
        string id,
        ChangeTeamRoleRequest request,
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        ITenantContext tenant,
        ClaimsPrincipal user,
        IStringLocalizer<ServerText> texts,
        CancellationToken ct)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        await using var change = await BeginAsync(db, tenant.TenantId, ct);
        var (member, problem) = await FindOtherAsync(users, id, tenant.TenantId, user, texts);
        if (member is null)
        {
            return problem!;
        }

        if (request.Role != StaffRoles.Admin && await IsLastActiveAdminAsync(db, member.Id, tenant.TenantId, ct))
        {
            return Problem(StatusCodes.Status409Conflict, "team.last_admin", texts);
        }

        var current = (await users.GetRolesAsync(member)).Where(StaffRoles.IsKnown).ToList();
        Ensure(await users.RemoveFromRolesAsync(member, current));
        if (request.Role is not null)
        {
            Ensure(await users.AddToRoleAsync(member, request.Role));
        }

        await LinkAsync(users, member, request.Role, request.CourierCode, request.MerchantId);

        // The role travels in the sign-in cookie; a new security stamp makes their sessions pick it up (or end).
        Ensure(await users.UpdateSecurityStampAsync(member));
        await change.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> LockAsync(
        string id,
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        ITenantContext tenant,
        ClaimsPrincipal user,
        IStringLocalizer<ServerText> texts,
        CancellationToken ct)
    {
        await using var change = await BeginAsync(db, tenant.TenantId, ct);
        var (member, problem) = await FindOtherAsync(users, id, tenant.TenantId, user, texts);
        if (member is null)
        {
            return problem!;
        }

        if (await IsLastActiveAdminAsync(db, member.Id, tenant.TenantId, ct))
        {
            return Problem(StatusCodes.Status409Conflict, "team.last_admin", texts);
        }

        Ensure(await users.SetLockoutEnabledAsync(member, true));
        Ensure(await users.SetLockoutEndDateAsync(member, DateTimeOffset.MaxValue));
        Ensure(await users.UpdateSecurityStampAsync(member));
        await change.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> UnlockAsync(
        string id,
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        ITenantContext tenant,
        IStringLocalizer<ServerText> texts,
        CancellationToken ct)
    {
        await using var change = await BeginAsync(db, tenant.TenantId, ct);
        if (await FindAsync(users, id, tenant.TenantId) is not { } member)
        {
            return Problem(StatusCodes.Status404NotFound, "team.not_found", texts);
        }

        Ensure(await users.SetLockoutEndDateAsync(member, null));
        Ensure(await users.ResetAccessFailedCountAsync(member));
        await change.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ResetPasswordAsync(
        string id,
        ResetTeamPasswordRequest request,
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        ITenantContext tenant,
        ClaimsPrincipal user,
        IStringLocalizer<ServerText> texts,
        ILogger<ResetTeamPasswordRequest> logger,
        CancellationToken ct)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        await using var change = await BeginAsync(db, tenant.TenantId, ct);
        var (member, problem) = await FindOtherAsync(users, id, tenant.TenantId, user, texts);
        if (member is null)
        {
            return problem!; // your own password is changed under Account, with the current one
        }

        // ResetPasswordAsync also renews the security stamp, which ends the member's open sessions.
        var reset = await users.ResetPasswordAsync(member, await users.GeneratePasswordResetTokenAsync(member), request.Password);
        if (!reset.Succeeded)
        {
            return PasswordProblem(reset);
        }

        await change.CommitAsync(ct);
        logger.LogInformation("{Admin} set a new password for {Member}", user.Identity?.Name, member.Email);
        return TypedResults.NoContent();
    }

    private sealed record MemberRow(string Id, string Email, string? DisplayName, string? Role, DateTimeOffset? LockoutEnd, bool TwoFactorEnabled, string? CourierCode, Guid? MerchantId);

    private static readonly string[] LinkClaims = [LogisticoClaimTypes.CourierCode, LogisticoClaimTypes.MerchantId];

    /// <summary>
    /// A courier account carries the code of the courier it delivers for, and a seller account the id of its shop (claims
    /// in the sign-in cookie); any other role carries neither. Returns what was set.
    /// </summary>
    private static async Task<(string? CourierCode, Guid? MerchantId)> LinkAsync(
        UserManager<ApplicationUser> users, ApplicationUser member, string? role, string? courierCode, Guid? merchantId)
    {
        var existing = (await users.GetClaimsAsync(member)).Where(c => LinkClaims.Contains(c.Type)).ToList();
        if (existing.Count > 0)
        {
            Ensure(await users.RemoveClaimsAsync(member, existing));
        }

        if (role == StaffRoles.Courier && !string.IsNullOrWhiteSpace(courierCode))
        {
            var code = courierCode.Trim();
            Ensure(await users.AddClaimAsync(member, new Claim(LogisticoClaimTypes.CourierCode, code)));
            return (code, null);
        }

        if (role == StaffRoles.Seller && merchantId is { } shop && shop != Guid.Empty)
        {
            Ensure(await users.AddClaimAsync(member, new Claim(LogisticoClaimTypes.MerchantId, shop.ToString())));
            return (null, shop);
        }

        return (null, null);
    }

    /// <summary>Every account of the company with its highest staff role (one query).</summary>
    private static async Task<List<MemberRow>> MembersAsync(ApplicationDbContext db, Guid tenantId, CancellationToken ct)
    {
        var rows = await (
            from u in db.Users.AsNoTracking()
            where u.TenantId == tenantId
            from ur in db.UserRoles.Where(x => x.UserId == u.Id).DefaultIfEmpty()
            from r in db.Roles.Where(x => ur != null && x.Id == ur.RoleId).DefaultIfEmpty()
            select new { u.Id, u.Email, u.UserName, u.DisplayName, Role = r == null ? null : r.Name, u.LockoutEnd, u.TwoFactorEnabled })
            .ToListAsync(ct);
        var ids = rows.Select(r => r.Id).Distinct().ToList();
        var linkClaims = await db.UserClaims.AsNoTracking()
            .Where(c => ids.Contains(c.UserId) && LinkClaims.Contains(c.ClaimType!))
            .Select(c => new { c.UserId, c.ClaimType, c.ClaimValue })
            .ToListAsync(ct);
        string? Link(string userId, string type) => linkClaims.FirstOrDefault(c => c.UserId == userId && c.ClaimType == type)?.ClaimValue;

        return rows
            .GroupBy(r => r.Id)
            .Select(g =>
            {
                var first = g.First();
                var role = g.Select(r => r.Role).Where(StaffRoles.IsKnown).OrderBy(r => RoleRank(r!)).FirstOrDefault();
                return new MemberRow(first.Id, first.Email ?? first.UserName ?? "", first.DisplayName, role, first.LockoutEnd, first.TwoFactorEnabled,
                    Link(first.Id, LogisticoClaimTypes.CourierCode), Guid.TryParse(Link(first.Id, LogisticoClaimTypes.MerchantId), out var shop) ? shop : null);
            })
            .ToList();
    }

    /// <summary>
    /// True when <paramref name="memberId"/> is the company's only administrator who has not been locked by a colleague.
    /// A lockout after wrong passwords is temporary, so that administrator still counts.
    /// </summary>
    private static async Task<bool> IsLastActiveAdminAsync(ApplicationDbContext db, string memberId, Guid tenantId, CancellationToken ct)
    {
        var admins = (await MembersAsync(db, tenantId, ct)).Where(m => m.Role == StaffRoles.Admin && !IsDeactivated(m.LockoutEnd)).ToList();
        return admins.Count == 1 && admins[0].Id == memberId;
    }

    /// <summary>Locked by an administrator (until further notice), as opposed to a temporary lockout after wrong passwords.</summary>
    private static bool IsDeactivated(DateTimeOffset? lockoutEnd) => lockoutEnd is { } end && end.Year >= 9999;

    /// <summary>
    /// Opens the change's transaction and waits for any other team change of the same company. Every operation of
    /// <see cref="UserManager{TUser}"/> in the request uses the same <see cref="ApplicationDbContext"/>, so they all join it.
    /// </summary>
    private static async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginAsync(ApplicationDbContext db, Guid tenantId, CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        var key = "team:" + tenantId.ToString("N");
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        return transaction;
    }

    private static async Task<ApplicationUser?> FindAsync(UserManager<ApplicationUser> users, string id, Guid tenantId) =>
        await users.FindByIdAsync(id) is { } member && member.TenantId == tenantId ? member : null;

    /// <summary>A colleague of the same company, never the administrator themselves (they cannot lock or demote themselves).</summary>
    private static async Task<(ApplicationUser? Member, ProblemHttpResult? Problem)> FindOtherAsync(
        UserManager<ApplicationUser> users, string id, Guid tenantId, ClaimsPrincipal user, IStringLocalizer<ServerText> texts)
    {
        if (await FindAsync(users, id, tenantId) is not { } member)
        {
            return (null, Problem(StatusCodes.Status404NotFound, "team.not_found", texts));
        }

        return member.Id == user.FindFirstValue(ClaimTypes.NameIdentifier)
            ? (null, Problem(StatusCodes.Status409Conflict, "team.self", texts))
            : (member, null);
    }

    /// <summary>Identity results that can only fail for a database or configuration problem: abort (and roll back) the change.</summary>
    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Updating the account failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    private static int RoleRank(string role)
    {
        var rank = StaffRoles.Assignable.ToList().IndexOf(role);
        return rank < 0 ? int.MaxValue : rank;
    }

    private static ValidationProblem? Validate(object request)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true))
        {
            return null;
        }

        return TypedResults.ValidationProblem(results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [""]).Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(x => x.Member)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "Invalid value.").Distinct().ToArray()));
    }

    /// <summary>Identity's password rules (translated by LocalizedIdentityErrors) as errors on the password field.</summary>
    private static ValidationProblem PasswordProblem(IdentityResult result) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(CreateTeamMemberRequest.Password)] = result.Errors.Select(e => e.Description).ToArray(),
        });

    private static ProblemHttpResult Problem(int status, string code, IStringLocalizer<ServerText> texts) =>
        TypedResults.Problem(
            statusCode: status,
            title: texts["Problem_" + code],
            type: $"https://logistico.app/problems/{code}",
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
