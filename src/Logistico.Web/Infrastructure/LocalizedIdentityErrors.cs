using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace Logistico.Web.Infrastructure;

/// <summary>
/// Identity's messages (password rules, duplicate accounts) in the request language, so Arabic users never see
/// English errors on sign-up, password changes or the team screen. Codes stay the same; only descriptions change.
/// </summary>
internal sealed class LocalizedIdentityErrors(IStringLocalizer<ServerText> text) : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), length);

    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit));

    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower));

    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper));

    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(nameof(PasswordRequiresNonAlphanumeric));

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Error(nameof(PasswordRequiresUniqueChars), uniqueChars);

    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), userName);

    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), email);

    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), email ?? "");

    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), userName ?? "");

    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch));

    public override IdentityError InvalidToken() => Error(nameof(InvalidToken));

    private IdentityError Error(string code, params object[] args) => new()
    {
        Code = code,
        Description = text["Identity_" + code, args],
    };
}
