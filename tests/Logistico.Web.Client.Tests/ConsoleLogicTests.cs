using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Security.Claims;
using Logistico.SharedKernel.Identity;
using Logistico.SharedKernel.Setup;
using Logistico.Web.Client.Layout;
using Logistico.Web.Client.Ui;
using Microsoft.AspNetCore.Components;

namespace Logistico.Web.Client.Tests;

/// <summary>The console's own rules: plurals, who sees which page, and that every link leads somewhere.</summary>
public sealed class ConsoleLogicTests
{
    private static readonly ResourceManager Texts = new("Logistico.Web.Client.Resources.UiText", typeof(UiText).Assembly);

    /// <summary>Every page route of the console ("" for the dashboard), without route parameters.</summary>
    private static readonly HashSet<string> Routes = [.. typeof(UiText).Assembly.GetTypes()
        .SelectMany(t => t.GetCustomAttributes<RouteAttribute>())
        .Select(r => r.Template.Trim('/'))];

    [Theory]
    [InlineData(1, "_One")]
    [InlineData(2, "_Two")]
    [InlineData(3, "_Few")]
    [InlineData(10, "_Few")]
    [InlineData(11, "_Many")]
    [InlineData(99, "_Many")]
    [InlineData(100, "")]
    [InlineData(103, "_Few")]
    [InlineData(0, "")]
    public void Arabic_counts_take_the_right_plural_form(int count, string suffix) =>
        Assert.Equal(suffix, Plural.Suffix(count, CultureInfo.GetCultureInfo("ar-IQ")));

    [Theory]
    [InlineData(1, "_One")]
    [InlineData(2, "")]
    [InlineData(21, "")]
    public void English_counts_have_one_and_other(int count, string suffix) =>
        Assert.Equal(suffix, Plural.Suffix(count, CultureInfo.GetCultureInfo("en-US")));

    [Fact]
    public void Each_role_sees_only_the_pages_it_may_use()
    {
        static string[] Pages(string role) => [.. AppNavigation.For(User(role)).SelectMany(g => g.Items).Select(e => e.Href)];

        Assert.Contains("team", Pages(StaffRoles.Admin));
        Assert.Contains("system", Pages(StaffRoles.Admin));
        Assert.DoesNotContain("team", Pages(StaffRoles.Operations));
        Assert.DoesNotContain("system", Pages(StaffRoles.Operations));
        Assert.Contains("warehouse", Pages(StaffRoles.Operations));
        Assert.DoesNotContain("warehouse", Pages(StaffRoles.Viewer)); // the station changes data
        Assert.Contains("getting-started", Pages(StaffRoles.Viewer));
        Assert.DoesNotContain("finance", Pages(StaffRoles.CustomerSupport));
        Assert.Empty(Pages(StaffRoles.Courier)); // couriers and sellers have their own apps, not the console
        Assert.Empty(Pages(StaffRoles.Seller));
        Assert.Empty(Pages(""));
    }

    [Fact]
    public void Every_menu_entry_and_guide_step_leads_to_a_page()
    {
        var links = AppNavigation.All.Select(e => e.Href).Concat(GettingStartedSteps.All.Select(s => s.Href));
        Assert.All(links, href => Assert.True(Routes.Contains(href), $"no page at /{href}"));
    }

    [Fact]
    public void Shortcuts_are_unique()
    {
        var shortcuts = AppNavigation.All.Where(e => e.Shortcut is not null).Select(e => e.Shortcut).ToList();
        Assert.Equal(shortcuts.Distinct().Count(), shortcuts.Count);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ar-IQ")]
    public void Every_guide_step_says_what_it_is_and_why(string culture)
    {
        var language = CultureInfo.GetCultureInfo(culture);
        foreach (var step in GettingStartedSteps.All)
        {
            var key = step.Id.Replace('-', '_');
            Assert.False(string.IsNullOrWhiteSpace(Texts.GetString($"Step_{key}_Title", language)), $"{culture}: Step_{key}_Title");
            Assert.False(string.IsNullOrWhiteSpace(Texts.GetString($"Step_{key}_Why", language)), $"{culture}: Step_{key}_Why");
        }
    }

    [Theory]
    [InlineData("database", new[] { "Ok", "Warning" })]
    [InlineData("migrations", new[] { "Ok", "Problem" })]
    [InlineData("collation", new[] { "Ok", "Problem" })]
    [InlineData("https", new[] { "Ok", "Info", "Problem" })]
    [InlineData("proxy", new[] { "Ok", "Info", "Warning" })]
    [InlineData("keys", new[] { "Ok", "Info", "Problem" })]
    [InlineData("email", new[] { "Ok", "Warning" })]
    [InlineData("bootstrap", new[] { "Ok", "Info", "Warning" })]
    [InlineData("demo", new[] { "Ok", "Warning" })]
    [InlineData("admins", new[] { "Ok", "Warning" })]
    [InlineData("two-step", new[] { "Ok", "Info" })]
    [InlineData("automation", new[] { "Ok", "Warning" })]
    public void Every_health_check_explains_each_answer_in_both_languages(string id, string[] states)
    {
        foreach (var culture in new[] { "en-US", "ar-IQ" })
        {
            var language = CultureInfo.GetCultureInfo(culture);
            var key = "Check_" + id.Replace('-', '_');
            Assert.False(string.IsNullOrWhiteSpace(Texts.GetString(key + "_Title", language)), $"{culture}: {key}_Title");
            foreach (var state in states)
            {
                Assert.False(string.IsNullOrWhiteSpace(Texts.GetString($"{key}_{state}", language)), $"{culture}: {key}_{state}");
            }
        }
    }

    [Fact]
    public void Money_reads_naturally_in_each_language()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal("65,000 IQD", Fmt.Money(65_000, "IQD"));
        Assert.Equal("$12.50", Fmt.Money(12.5m, "USD"));
        Assert.Equal("—", Fmt.Money(null, "IQD"));

        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar-IQ");
        Assert.EndsWith("د.ع", Fmt.Money(65_000, "IQD"), StringComparison.Ordinal);
    }

    private static ClaimsPrincipal User(string role) =>
        new(new ClaimsIdentity(role.Length == 0 ? [] : [new Claim(ClaimTypes.Role, role)], "test"));
}
