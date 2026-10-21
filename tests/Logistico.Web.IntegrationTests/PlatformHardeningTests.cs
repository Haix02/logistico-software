using System.Net;
using System.Text.RegularExpressions;
using Logistico.Modules.Shipments.Contracts;

namespace Logistico.Web.IntegrationTests;

/// <summary>Browser-facing protections: Content-Security-Policy, compression of API answers and sign-in rate limits.</summary>
[Collection("Api")]
public sealed partial class PlatformHardeningTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task Pages_only_run_scripts_from_this_origin_or_marked_with_the_response_nonce()
    {
        using var client = factory.CreateClient();

        var first = await client.GetAsync("/Account/Login");
        var second = await client.GetAsync("/Account/Login");
        var policy = string.Join(' ', first.Headers.GetValues("Content-Security-Policy"));
        var html = await first.Content.ReadAsStringAsync();

        var nonce = NoncePattern().Match(policy).Groups[1].Value;
        Assert.NotEmpty(nonce);
        Assert.DoesNotContain(nonce, string.Join(' ', second.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-inline'", policy.Split(';').Single(d => d.Trim().StartsWith("script-src", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", policy, StringComparison.Ordinal);

        // Every inline script (theme bootstrap, import map) carries the nonce; everything else is loaded from a file.
        var inline = InlineScriptPattern().Matches(html).Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(inline);
        Assert.All(inline, attributes => Assert.Contains($"nonce=\"{nonce}\"", attributes, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Api_answers_are_compressed_when_the_browser_accepts_it()
    {
        var client = factory.CreateUserClient();
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("br");

        var response = await client.GetAsync("/" + ShipmentsApiRoutes.Shipments + "?pageSize=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("br", Assert.Single(response.Content.Headers.ContentEncoding));
        Assert.Equal("no-store, max-age=0", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Sign_in_forms_are_rate_limited_per_client_address()
    {
        using var client = factory.CreateClient();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 31; i++)
        {
            last = await client.PostAsync("/Account/Login", new FormUrlEncodedContent([new("Input.Email", "nobody@logistico.test"), new("Input.Password", "wrong")]));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        Assert.Equal("60", last.Headers.RetryAfter?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(await last.Content.ReadAsStringAsync()));
    }

    [GeneratedRegex("'nonce-([^']+)'")]
    private static partial Regex NoncePattern();

    [GeneratedRegex(@"<script(?![^>]*\bsrc=)([^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScriptPattern();
}
