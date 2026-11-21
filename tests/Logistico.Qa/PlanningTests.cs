using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Logistico.Qa;

/// <summary>
/// The optimization engine as an office uses it, in a real browser on the demo company: each planner is run from its
/// tab, its plan shows with the solver's proof and the checker's verdict, and nothing on the page fails. Screenshots of
/// every planner land in TestResults/qa for review. Plans are made but not approved, so the other QA journeys keep
/// their data.
/// </summary>
[Collection("Qa")]
[Trait("Category", "Qa")]
public sealed class PlanningTests(QaServer server, Browser browser, ITestOutputHelper output) : IClassFixture<Browser>
{
    [Theory]
    [InlineData("ar-IQ")]
    [InlineData("en-US")]
    public async Task Every_planner_makes_a_checked_plan_in_the_browser(string culture)
    {
        var person = await browser.OpenAsync(server.BaseAddress, culture);
        await person.SignInAsync("admin@logistico.local");
        await person.Page.GotoAsync("/planning");
        await person.SettleAsync();

        foreach (var tab in new[] { "trucks", "couriers", "zones" })
        {
            await person.Page.Locator($"#tab-{tab}").ClickAsync();
            var panel = person.Page.Locator($"#panel-{tab}");
            await panel.Locator(".lx-plan-bar button.bp6-intent-primary").ClickAsync();
            await panel.Locator(".lx-plan-bar .bp6-loading").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 90_000 });
            await panel.Locator(".lx-plan-head").WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
            await person.SettleAsync();
            await person.InspectAsync(sidewaysScroll: true);

            var proof = await panel.Locator(".lx-plan-proof .lx-strong").InnerTextAsync();
            output.WriteLine($"{culture} {tab}: {proof}");
            await person.Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(QaServer.ResultsFolder, $"planning-{tab}-{culture}.png"), FullPage = true });

            // The independent checker accepted the plan.
            Assert.Equal(0, await panel.Locator(".lx-plan-head .bp6-intent-danger").CountAsync());
        }

        Assert.True(person.Problems.Count == 0, string.Join(Environment.NewLine, person.Problems));
    }
}
