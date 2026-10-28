using System.ComponentModel.DataAnnotations;
using Logistico.Web.Data;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Logistico.Web.Endpoints;

/// <summary>
/// Sign-in for the mobile app (ADR-031): an e-mail and password give a short-lived bearer token and a refresh token,
/// with the same rules as the web sign-in (confirmed account, lockout after five wrong passwords, two-factor). There is
/// no registration here: accounts are made by the company's administrators.
/// </summary>
internal static class AuthTokenEndpoints
{
    public static IEndpointRouteBuilder MapAuthTokenEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth").WithTags("App sign-in").AllowAnonymous();
        auth.MapPost("/token", TokenAsync).WithSummary("Signs in with e-mail and password (and a two-factor code when the account has one); answers with tokens.");
        auth.MapPost("/refresh", RefreshAsync).WithSummary("Exchanges a refresh token for new tokens while the account is still active and unchanged.");
        return app;
    }

    private static async Task<IResult> TokenAsync(TokenRequest request, SignInManager<ApplicationUser> signIn, IStringLocalizer<ServerText> texts)
    {
        ProblemHttpResult Refused(string code) => Problem(code, texts);

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return Refused("auth.invalid");
        }

        signIn.AuthenticationScheme = IdentityConstants.BearerScheme;
        var result = await signIn.PasswordSignInAsync(request.Email.Trim(), request.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.RequiresTwoFactor)
        {
            if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
            {
                return Refused("auth.two_factor_required");
            }

            result = await signIn.TwoFactorAuthenticatorSignInAsync(request.TwoFactorCode.Replace(" ", "", StringComparison.Ordinal), isPersistent: false, rememberClient: false);
        }

        // On success the bearer handler has already written the tokens to the response.
        return result.Succeeded ? TypedResults.Empty
            : result.IsLockedOut ? Refused("auth.locked_out")
            : result.IsNotAllowed ? Refused("auth.not_allowed")
            : Refused("auth.invalid");
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request, SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, IOptionsMonitor<BearerTokenOptions> options,
        TimeProvider clock, IStringLocalizer<ServerText> texts)
    {
        var ticket = options.Get(IdentityConstants.BearerScheme).RefreshTokenProtector.Unprotect(request.RefreshToken);
        if (ticket?.Properties.ExpiresUtc is not { } expires || clock.GetUtcNow() >= expires
            || await signIn.ValidateSecurityStampAsync(ticket.Principal) is not { } user || await users.IsLockedOutAsync(user))
        {
            return Problem("auth.expired", texts);
        }

        var principal = await signIn.CreateUserPrincipalAsync(user);
        return TypedResults.SignIn(principal, authenticationScheme: IdentityConstants.BearerScheme);
    }

    private static ProblemHttpResult Problem(string code, IStringLocalizer<ServerText> texts) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: texts["Problem_" + code],
            type: $"https://logistico.app/problems/{code}",
            extensions: new Dictionary<string, object?> { ["code"] = code });

    internal sealed class TokenRequest
    {
        [Required]
        public string Email { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";

        public string? TwoFactorCode { get; set; }
    }

    internal sealed class RefreshRequest
    {
        [Required]
        public string RefreshToken { get; set; } = "";
    }
}
