using System.Globalization;
using Logistico.SharedKernel;
using Microsoft.AspNetCore.Localization;

namespace Logistico.Web.Infrastructure;

/// <summary>
/// Arabic (Iraq) is the default language. The user's choice is kept in the standard culture cookie;
/// API callers without the cookie (partners, mobile apps) may use Accept-Language.
/// </summary>
internal static class LocalizationSetup
{
    public static RequestLocalizationOptions Options()
    {
        var cultures = AppCultures.Supported.Select(AppCultures.Create).ToList();
        var options = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(AppCultures.Default),
            SupportedCultures = cultures,
            SupportedUICultures = cultures,
        };
        options.RequestCultureProviders =
        [
            new QueryStringRequestCultureProvider(),
            new CookieRequestCultureProvider(),
            new ApiAcceptLanguageProvider(),
        ];
        return options;
    }

    /// <summary>GET /culture?c=en-US&amp;returnUrl=/shipments → stores the choice and reloads the page.</summary>
    public static IEndpointRouteBuilder MapCultureSwitch(this IEndpointRouteBuilder app)
    {
        app.MapGet("/culture", (string c, string? returnUrl, HttpContext http) =>
        {
            var culture = AppCultures.Supported.Contains(c) ? c : AppCultures.Default;
            http.Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax, HttpOnly = false });
            var target = string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//", StringComparison.Ordinal) ? "/" : returnUrl;
            return Results.LocalRedirect(target);
        }).AllowAnonymous().ExcludeFromDescription();
        return app;
    }

    private sealed class ApiAcceptLanguageProvider : RequestCultureProvider
    {
        private readonly AcceptLanguageHeaderRequestCultureProvider _inner = new();

        public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext) =>
            httpContext.Request.Path.StartsWithSegments("/api")
                ? _inner.DetermineProviderCultureResult(httpContext)
                : Task.FromResult<ProviderCultureResult?>(null);
    }
}
