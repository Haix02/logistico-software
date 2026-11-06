using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Logistico.SharedKernel.Identity;
using Logistico.SharedKernel.Setup;
using Logistico.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Logistico.Web.IntegrationTests;

/// <summary>A fresh installation with no administrator, set up with a known code.</summary>
public sealed class FreshInstallFactory : LogisticoApiFactory
{
    public const string Code = "K7QF-M2XD";

    protected override IEnumerable<(string Key, string Value)> ExtraSettings => [("Setup:Code", Code)];
}

/// <summary>The first run (docs/19): the setup page, its code, and that it works exactly once.</summary>
public sealed partial class SetupTests(FreshInstallFactory factory) : IClassFixture<FreshInstallFactory>
{
    [Fact]
    public async Task A_new_installation_is_set_up_once_with_the_code_from_the_log()
    {
        var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Nobody can sign in yet: the sign-in page and /setup lead to the setup page.
        var login = await browser.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.EndsWith("/Account/Setup", login.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Equal("/Account/Setup", (await browser.GetAsync("/setup")).Headers.Location!.OriginalString);

        // A wrong code creates nothing and says why.
        var wrong = await SubmitAsync(browser, Form("WRONG-CODE", "owner@rafidain.iq"));
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("رمز الإعداد غير صحيح", WebUtility.HtmlDecode(await wrong.Content.ReadAsStringAsync()), StringComparison.Ordinal); // Arabic by default
        Assert.Null(await FindAsync("owner@rafidain.iq"));

        // A password Identity refuses: nothing is half-created.
        var weak = await SubmitAsync(browser, Form(FreshInstallFactory.Code, "owner@rafidain.iq", password: "short"));
        Assert.Equal(HttpStatusCode.OK, weak.StatusCode);
        Assert.Null(await FindAsync("owner@rafidain.iq"));

        // The right code (typed in lower case, without the dash): an administrator, signed in, sent to the guide.
        var done = await SubmitAsync(browser, Form("k7qfm2xd", "owner@rafidain.iq"));
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        Assert.EndsWith("getting-started", done.Headers.Location!.OriginalString, StringComparison.Ordinal);
        var owner = await FindAsync("owner@rafidain.iq");
        Assert.NotNull(owner);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True(await users.IsInRoleAsync(owner, StaffRoles.Admin));
        }

        // The new administrator's cookie works, and the company is named.
        var company = (await browser.GetFromJsonAsync<CompanyProfileView>("/" + SetupApiRoutes.Company))!;
        Assert.Equal(("الرافدين للتوصيل", "0770 123 4567", true), (company.Profile.Name, company.Profile.Phone, company.IsSet));
        var guide = (await browser.GetFromJsonAsync<GettingStartedView>("/" + SetupApiRoutes.GettingStarted))!;
        Assert.Equal(GettingStartedSteps.All.Count, guide.Steps.Count);
        Assert.True(guide.Steps.Single(s => s.Id == GettingStartedSteps.Company).Done);

        // From now on the page is closed, even with the right code, and sign-in is open.
        var stranger = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Contains("href=\"Account/Login\"", await (await stranger.GetAsync("/Account/Setup")).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var again = await SubmitAsync(stranger, Form(FreshInstallFactory.Code, "intruder@example.com"));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode); // the closed page has no form, so there is nothing to post to
        Assert.Null(await FindAsync("intruder@example.com"));
        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync("/Account/Login")).StatusCode);
    }

    private async Task<ApplicationUser?> FindAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
    }

    internal static Dictionary<string, string> Form(string code, string email, string password = "Rafidain#2026") => new()
    {
        ["Input.Code"] = code,
        ["Input.CompanyName"] = "الرافدين للتوصيل",
        ["Input.CompanyPhone"] = "0770 123 4567",
        ["Input.Country"] = "IQ",
        ["Input.Governorate"] = "IQ-BG",
        ["Input.AdminName"] = "علي حسن",
        ["Input.Email"] = email,
        ["Input.Password"] = password,
        ["Input.ConfirmPassword"] = password,
    };

    /// <summary>Posts the setup form as a browser does: with the page's antiforgery token and cookie.</summary>
    internal static async Task<HttpResponseMessage> SubmitAsync(HttpClient browser, Dictionary<string, string> fields)
    {
        var page = await (await browser.GetAsync("/Account/Setup")).Content.ReadAsStringAsync();
        var token = AntiforgeryToken().Match(page);
        var form = new Dictionary<string, string>(fields) { ["_handler"] = "setup" };
        if (token.Success)
        {
            form["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value);
        }

        return await browser.PostAsync("/Account/Setup", new FormUrlEncodedContent(form));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}

/// <summary>Two people submitting the setup page at the same moment get one administrator.</summary>
public sealed class SetupRaceTests(FreshInstallFactory factory) : IClassFixture<FreshInstallFactory>
{
    [Fact]
    public async Task Simultaneous_setups_create_one_administrator()
    {
        var first = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var second = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await Task.WhenAll(
            SetupTests.SubmitAsync(first, SetupTests.Form(FreshInstallFactory.Code, "first@rafidain.iq")),
            SetupTests.SubmitAsync(second, SetupTests.Form(FreshInstallFactory.Code, "second@rafidain.iq")));

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Single(await users.GetUsersInRoleAsync(StaffRoles.Admin));
    }
}
