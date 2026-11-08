using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Logistico.Web.Client;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Logistico.ArchitectureTests;

/// <summary>Every UI text must resolve at run time (raw keys must never reach the screen). Key parity: <see cref="UiConventionTests"/>.</summary>
public partial class LocalizationTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string ResourceFolder = Path.Combine(RepoRoot, "src", "Logistico.Web.Client", "Resources");

    [Theory]
    [InlineData("en-US", "Logistico")]
    [InlineData("ar-IQ", "لوجستيكو")]
    public void Ui_texts_resolve_with_the_registered_resources_path(string culture, string expectedAppName)
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions { ResourcesPath = UiText.ResourcesPath }),
            NullLoggerFactory.Instance);
        var localizer = factory.Create(typeof(UiText));

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var text = localizer["AppName"];
            Assert.False(text.ResourceNotFound, "UI resources were not found; check UiText.ResourcesPath and the resx location.");
            Assert.Equal(expectedAppName, text.Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Every_text_key_used_by_a_page_exists()
    {
        var keys = Keys(Path.Combine(ResourceFolder, "UiText.resx"));
        var clientFolder = Path.Combine(RepoRoot, "src", "Logistico.Web.Client");
        var missing = Directory.EnumerateFiles(clientFolder, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => KeyUsage().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .Where(u => !keys.Contains(u.Key))
            .Select(u => $"{u.File}: {u.Key}")
            .Distinct()
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// A refusal the API sends (a rule, a conflict, a stale edit…) carries a code; the person reads the message stored
    /// under <c>Problem_&lt;code&gt;</c>. Without one, the raw code reaches the screen (a customer saw
    /// "shipment.rating_not_possible").
    /// </summary>
    [Fact]
    public void Every_problem_code_has_a_message_in_both_languages()
    {
        var source = Path.Combine(RepoRoot, "src");
        static bool Built(string f) => f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

        var english = new HashSet<string>(StringComparer.Ordinal);
        var arabic = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resx in Directory.EnumerateFiles(source, "*.resx", SearchOption.AllDirectories).Where(f => !Built(f)))
        {
            var target = resx.EndsWith(".ar.resx", StringComparison.Ordinal) ? arabic : english;
            target.UnionWith(Keys(resx));
        }

        var codes = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(f => !Built(f))
            .SelectMany(f => ProblemCode().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Code: m.Groups[1].Value)))
            .ToList();
        Assert.True(codes.Count > 40, "The search for problem codes found almost none; check the pattern.");

        var missing = codes
            .Where(c => !english.Contains("Problem_" + c.Code) || !arabic.Contains("Problem_" + c.Code))
            .Select(c => $"{c.File}: {c.Code}")
            .Distinct()
            .ToList();
        Assert.Empty(missing);
    }

    private static HashSet<string> Keys(string resx) =>
        XDocument.Load(resx).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet(StringComparer.Ordinal);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Logistico.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (Logistico.slnx) not found.");
    }

    // Rule("shipment.x"), new DomainException("courier.x", …), Api.Conflict("area.taken", …), Refused("auth.invalid"): the codes the API answers with.
    [GeneratedRegex(@"\b(?:Rule|Api\.[A-Za-z]+|Refused|DomainException)\(\s*""([a-z_]+\.[a-z_.]+)""")]
    private static partial Regex ProblemCode();

    // L["Key"] and count texts L.Count("Key", …) (their plural variants fall back to the base key).
    [GeneratedRegex("""L(?:\["|\.Count\(")([A-Za-z0-9_]+)"(?=[\],])""")]
    private static partial Regex KeyUsage();
}
