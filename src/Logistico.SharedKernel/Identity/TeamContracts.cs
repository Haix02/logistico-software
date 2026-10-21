using System.ComponentModel.DataAnnotations;

namespace Logistico.SharedKernel.Identity;

/// <summary>
/// A staff account of the company. <see cref="Role"/> is null for accounts still waiting for access. <see cref="IsLocked"/>
/// means an administrator locked the account; <see cref="LockedOutUntil"/> is a temporary lockout after wrong passwords.
/// </summary>
public sealed record TeamMember(
    string Id,
    string Email,
    string? DisplayName,
    string? Role,
    bool IsLocked,
    bool TwoFactorEnabled,
    bool IsYou,
    DateTimeOffset? LockedOutUntil = null);

public sealed record TeamList(IReadOnlyList<TeamMember> Members);

/// <summary>Adds a staff member with a first password they change after signing in (Account → Password).</summary>
public sealed class CreateTeamMemberRequest : IValidatableObject
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [EmailAddress(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Email))]
    [StringLength(256, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string Email { get; set; } = "";

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(100, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string DisplayName { get; set; } = "";

    public string Role { get; set; } = StaffRoles.Operations;

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(128, MinimumLength = 10, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.PasswordLength))]
    public string Password { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!StaffRoles.IsKnown(Role))
        {
            yield return new(ValidationText.Role, [nameof(Role)]);
        }
    }
}

/// <summary>Gives a member another role, or takes their access away (<see cref="Role"/> null).</summary>
public sealed class ChangeTeamRoleRequest : IValidatableObject
{
    public string? Role { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Role is not null && !StaffRoles.IsKnown(Role))
        {
            yield return new(ValidationText.Role, [nameof(Role)]);
        }
    }
}

/// <summary>An administrator sets a new password for a colleague who forgot theirs (email sending is optional).</summary>
public sealed class ResetTeamPasswordRequest
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(128, MinimumLength = 10, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.PasswordLength))]
    public string Password { get; set; } = "";
}

public static class TeamApiRoutes
{
    public const string Team = "api/v1/team";

    public static string Role(string id) => $"{Team}/{Uri.EscapeDataString(id)}/role";

    public static string Lock(string id) => $"{Team}/{Uri.EscapeDataString(id)}/lock";

    public static string Unlock(string id) => $"{Team}/{Uri.EscapeDataString(id)}/unlock";

    public static string Password(string id) => $"{Team}/{Uri.EscapeDataString(id)}/password";
}
