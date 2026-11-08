using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Logistico.Qa;

/// <summary>One Chrome for the suite (the machine's own Chrome: nothing to download). Each person gets a clean context.</summary>
public sealed partial class Browser : IAsyncLifetime
{
    private IPlaywright? playwright;

    public IBrowser Chrome { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        playwright = await Playwright.CreateAsync();
        Chrome = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = "chrome", Headless = true });
    }

    public async Task DisposeAsync()
    {
        await Chrome.CloseAsync();
        playwright?.Dispose();
    }

    /// <summary>A new visitor; <paramref name="culture"/> is en-US or ar-IQ.</summary>
    public async Task<Visitor> OpenAsync(Uri site, string culture, int width = 1440, int height = 900)
    {
        var context = await Chrome.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = site.ToString(),
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        var page = await context.NewPageAsync();
        var visitor = new Visitor(page);
        await page.GotoAsync($"/culture?c={culture}&returnUrl=/track", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        return visitor;
    }
}

/// <summary>A person in the browser, with everything that went wrong on the pages they saw.</summary>
public sealed partial class Visitor
{
    public Visitor(IPage page)
    {
        Page = page;
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                Problems.Add($"{Where}: console error: {message.Text}");
            }
        };
        page.PageError += (_, error) => Problems.Add($"{Where}: script error: {error}");
        page.Response += (_, response) =>
        {
            var path = new Uri(response.Url).AbsolutePath;
            if (response.Status >= 500 || (response.Status is 401 or 403 && path.StartsWith("/api/", StringComparison.Ordinal)))
            {
                Problems.Add($"{Where}: {response.Request.Method} {path} answered {response.Status}");
            }
        };
    }

    public IPage Page { get; }

    public List<string> Problems { get; } = [];

    private string Where => new Uri(Page.Url).PathAndQuery;

    public async Task SignInAsync(string email, string password = QaServer.Password)
    {
        await Page.GotoAsync("/Account/Login");
        await Page.FillAsync("input[name='Input.Email']", email);
        await Page.FillAsync("input[name='Input.Password']", password);
        await Page.ClickAsync("button[type=submit]");
        await Page.WaitForURLAsync(url => !url.Contains("/Account/", StringComparison.Ordinal));
        await SettleAsync();
    }

    /// <summary>The WebAssembly app has started and the page's data has arrived.</summary>
    public async Task SettleAsync()
    {
        // The start-up splash stays in the page; CSS hides it once one of the app's frames is there (app.css).
        await Page.WaitForFunctionAsync("() => !!document.querySelector('.lx-app, .lx-public, .lx-account')", null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>What a person would notice on the page in front of them.</summary>
    public async Task InspectAsync(bool sidewaysScroll)
    {
        var text = await Page.InnerTextAsync("body");
        foreach (Match key in RawTextKey().Matches(text))
        {
            Problems.Add($"{Where}: untranslated text key {key.Value}");
        }

        if (await Page.IsVisibleAsync("#blazor-error-ui"))
        {
            Problems.Add($"{Where}: the error bar is showing");
        }

        if (sidewaysScroll)
        {
            var overflow = await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
            if (overflow > 1)
            {
                Problems.Add($"{Where}: scrolls sideways by {overflow}px at {Page.ViewportSize?.Width}px wide");
            }
        }
    }

    /// <summary>A resource key shown as text means the translation is missing: "SortPlan_Rules", "Step_company_Why".</summary>
    [GeneratedRegex(@"\b(?:[A-Z][a-z]+)+_[A-Za-z0-9]+(?:_[A-Za-z0-9]+)*\b")]
    private static partial Regex RawTextKey();
}
