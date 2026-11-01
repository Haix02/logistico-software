using Logistico.SharedKernel.Identity;
using Logistico.SharedKernel.Setup;
using Logistico.Web.Client.Pages.Setup;
using Logistico.Web.Client.Ui;

namespace Logistico.Web.Client.Tests;

/// <summary>The getting-started card and the installation health page, rendered in English.</summary>
public sealed class SetupComponentTests : ClientTestContext
{
    public SetupComponentTests()
        : base("en-US")
    {
        AddAuthorization().SetAuthorized("admin@rafidain.iq").SetRoles(StaffRoles.Admin);
    }

    [Fact]
    public void The_dashboard_card_shows_progress_and_the_next_step()
    {
        Api.Answer(SetupApiRoutes.GettingStarted, new GettingStartedView(
        [
            new(GettingStartedSteps.Company, "settings/company", "office", true, false),
            new(GettingStartedSteps.Warehouses, "warehouses", "home", false, true),
            new(GettingStartedSteps.Prices, "pricing", "tag", false, false),
            new(GettingStartedSteps.Team, "team", "people", false, false),
        ]));

        var card = Render<GettingStartedCard>();

        card.WaitForAssertion(() => Assert.Contains("2 of 4 steps done", card.Markup, StringComparison.Ordinal));
        Assert.Contains("Next: Delivery prices", card.Markup, StringComparison.Ordinal);
        Assert.Equal("pricing", card.Find("a.bp6-intent-primary").GetAttribute("href"));
    }

    [Fact]
    public void The_card_stays_away_once_hidden_or_finished()
    {
        Api.Answer(SetupApiRoutes.GettingStarted, new GettingStartedView([new(GettingStartedSteps.Company, "settings/company", "office", true, false)]));
        Assert.Empty(Render<GettingStartedCard>().Markup.Trim()); // finished

        JSInterop.Setup<string?>("lx.getPref", GettingStartedCard.HiddenPref).SetResult("1");
        Api.Answer(SetupApiRoutes.GettingStarted, new GettingStartedView([new(GettingStartedSteps.Company, "settings/company", "office", false, false)]));
        Assert.Empty(Render<GettingStartedCard>().Markup.Trim()); // hidden by this person
        Assert.Single(Api.Calls, c => c.EndsWith(SetupApiRoutes.GettingStarted, StringComparison.Ordinal)); // hidden: not even asked
    }

    [Fact]
    public void Health_lists_what_to_fix_first_and_says_how()
    {
        Api.Answer(SetupApiRoutes.SystemChecks, new SystemChecksView(
        [
            new("database", SystemCheckState.Ok, ["16.4"]),
            new("email", SystemCheckState.Warning, []),
            new("https", SystemCheckState.Problem, []),
            new("two-step", SystemCheckState.Info, ["1", "4"]),
        ], DateTimeOffset.UtcNow));

        var page = Render<SystemPage>();

        page.WaitForAssertion(() => Assert.Contains("must be fixed before going live", page.Markup, StringComparison.Ordinal));
        var titles = page.FindAll(".lx-list-title").Select(e => e.TextContent).ToList();
        Assert.Equal(["HTTPS", "Email", "Two-step sign-in", "PostgreSQL"], titles);
        Assert.Contains("1 of 4 people use two-step sign-in", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Version 16.4.", page.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.42, 42)]
    [InlineData(1.7, 100)]
    [InlineData(-1, 0)]
    public void Progress_bars_stay_between_empty_and_full(double value, int percent)
    {
        var bar = Render<ProgressBar>(p => p.Add(b => b.Value, value));
        Assert.Equal(percent.ToString(System.Globalization.CultureInfo.InvariantCulture), bar.Find("[role=progressbar]").GetAttribute("aria-valuenow"));
    }
}
