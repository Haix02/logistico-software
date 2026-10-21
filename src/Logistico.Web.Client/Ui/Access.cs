using System.Security.Claims;
using Logistico.SharedKernel.Identity;
using Microsoft.AspNetCore.Components.Authorization;

namespace Logistico.Web.Client.Ui;

/// <summary>
/// What the signed-in staff member may do, by the same policies the API enforces (StaffPolicies). The browser only
/// hides what a role cannot use; the server refuses it either way.
/// </summary>
public static class Access
{
    public static bool Can(this ClaimsPrincipal user, string policy) => StaffPolicies.RolesFor(policy).Any(user.IsInRole);

    /// <summary>Signed in with a staff role (an account without one waits for an administrator).</summary>
    public static bool IsStaff(this ClaimsPrincipal user) => user.Can(StaffPolicies.Staff);

    /// <summary>The staff role of the user, if any.</summary>
    public static string? StaffRole(this ClaimsPrincipal user) => StaffRoles.Assignable.FirstOrDefault(user.IsInRole);

    /// <summary>For pages: <c>canOperate = await AuthState.CanAsync(StaffPolicies.Operate)</c> with the cascading authentication state.</summary>
    public static async Task<bool> CanAsync(this Task<AuthenticationState>? state, string policy) =>
        state is not null && (await state).User.Can(policy);
}
