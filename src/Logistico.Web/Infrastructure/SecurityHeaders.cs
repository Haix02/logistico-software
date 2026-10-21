using System.Security.Cryptography;

namespace Logistico.Web.Infrastructure;

internal static class SecurityHeaders
{
    private const string NonceKey = "logistico.csp-nonce";

    /// <summary>The per-response nonce that lets the page's own inline scripts (theme bootstrap, import map) run.</summary>
    public static string CspNonce(this HttpContext context) => (string)context.Items[NonceKey]!;

    /// <summary>
    /// Baseline headers for every response, including a Content-Security-Policy: scripts only from this origin (plus
    /// the nonce-marked inline bootstrap and import map, and WebAssembly compilation), no plugins, no framing, no
    /// foreign form targets. Styles allow inline attributes, which Blazor markup and Blueprint use for layout.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        var development = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();
        return app.Use(async (context, next) =>
        {
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)); // hex: nothing for HTML to encode
            context.Items[NonceKey] = nonce;

            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=(self), usb=(self), serial=(self)";
            headers["X-Permitted-Cross-Domain-Policies"] = "none";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers.ContentSecurityPolicy = string.Join("; ",
                "default-src 'self'",
                $"script-src 'self' 'nonce-{nonce}' 'wasm-unsafe-eval'",
                "style-src 'self' 'unsafe-inline'",
                "img-src 'self' data: blob:",
                "font-src 'self' data:",
                // Development tools (hot reload, browser refresh) talk to the dev server over web sockets.
                development ? "connect-src 'self' ws: wss:" : "connect-src 'self'",
                "worker-src 'self' blob:",
                "manifest-src 'self'",
                "object-src 'none'",
                "base-uri 'self'",
                "form-action 'self'",
                "frame-ancestors 'none'");
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                // API responses include operational and customer data and must never be stored by shared or browser caches.
                headers.CacheControl = "no-store, max-age=0";
                headers.Pragma = "no-cache";
            }

            await next();
        });
    }
}
