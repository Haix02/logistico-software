using Xunit.Abstractions;

namespace Logistico.Qa;

/// <summary>
/// Every page in the menu, opened in a real browser by every office role: no script errors, no refused or failed API
/// calls, no untranslated text; and on a phone-sized screen nothing scrolls sideways.
/// </summary>
[Collection("Qa")]
[Trait("Category", "Qa")]
public sealed class EveryPageTests(QaServer server, Browser browser, ITestOutputHelper output) : IClassFixture<Browser>
{
    [Theory]
    [InlineData("admin@logistico.local", "ar-IQ")]
    [InlineData("admin@logistico.local", "en-US")]
    [InlineData("ops@logistico.local", "ar-IQ")]
    [InlineData("support@logistico.local", "ar-IQ")]
    [InlineData("finance@logistico.local", "ar-IQ")]
    [InlineData("viewer@logistico.local", "en-US")]
    public async Task Every_menu_page_opens_cleanly(string email, string culture)
    {
        var person = await browser.OpenAsync(server.BaseAddress, culture);
        await person.SignInAsync(email);

        var links = await MenuAsync(person);
        Assert.NotEmpty(links);
        foreach (var link in links)
        {
            // Through the menu, as people do; a page outside the console (public tracking) has no menu, so go back by address.
            var item = person.Page.Locator($"a.lx-nav-item[href='{link}']");
            if (await item.CountAsync() == 1)
            {
                await item.ClickAsync();
            }
            else
            {
                await person.Page.GotoAsync("/" + link);
            }

            await person.SettleAsync();
            await person.InspectAsync(sidewaysScroll: true);
        }

        output.WriteLine($"{email} ({culture}): {links.Count} pages");
        Assert.True(person.Problems.Count == 0, string.Join(Environment.NewLine, person.Problems));
    }

    [Theory]
    [InlineData("admin@logistico.local", "ar-IQ")]
    [InlineData("admin@logistico.local", "en-US")]
    public async Task Every_page_fits_a_phone(string email, string culture)
    {
        var desktop = await browser.OpenAsync(server.BaseAddress, culture);
        await desktop.SignInAsync(email);
        var links = await MenuAsync(desktop);

        await desktop.Page.SetViewportSizeAsync(390, 844);
        foreach (var link in links)
        {
            await desktop.Page.GotoAsync("/" + link);
            await desktop.SettleAsync();
            await desktop.InspectAsync(sidewaysScroll: true);
        }

        Assert.True(desktop.Problems.Count == 0, string.Join(Environment.NewLine, desktop.Problems));
    }

    private static async Task<List<string>> MenuAsync(Visitor person) =>
        [.. await person.Page.Locator("a.lx-nav-item").EvaluateAllAsync<string[]>("links => links.map(l => l.getAttribute('href'))")];
}
