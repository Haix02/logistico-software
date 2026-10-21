using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Web.Client.Ui;

namespace Logistico.ArchitectureTests;

/// <summary>
/// Guards for the things that broke the Arabic UI before: physical left/right CSS, icons that do not exist,
/// texts missing in one language, and Latin codes scrambled inside Arabic sentences.
/// </summary>
public partial class UiConventionTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string WwwRoot = Path.Combine(RepoRoot, "src", "Logistico.Web", "wwwroot");

    [Theory]
    [InlineData("app.css")]
    [InlineData("account.css")]
    public void Stylesheets_use_logical_properties_so_arabic_mirrors_correctly(string file)
    {
        var offending = File.ReadAllLines(Path.Combine(WwwRoot, file))
            .Select((line, index) => (Line: line, Number: index + 1))
            .Where(l => PhysicalDirection().IsMatch(l.Line))
            .Select(l => $"{file}:{l.Number}: {l.Line.Trim()}")
            .ToList();

        Assert.Empty(offending);
    }

    [Fact]
    public void Every_icon_used_by_the_ui_exists_in_blueprint()
    {
        var available = IconClass().Matches(File.ReadAllText(Path.Combine(WwwRoot, "lib", "blueprint", "blueprint-icons.css")))
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var clientFolder = Path.Combine(RepoRoot, "src", "Logistico.Web.Client");
        var used = SourceFiles(clientFolder)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Services{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => IconUsage().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Icon: m.Groups["icon"].Value)))
            .Where(u => u.Icon.Length > 0 && !u.Icon.StartsWith("lx-", StringComparison.Ordinal) && !available.Contains(u.Icon))
            .Select(u => $"{u.File}: {u.Icon}")
            .Distinct()
            .ToList();

        Assert.True(available.Count > 600, "Blueprint icon classes were not found.");
        Assert.Empty(used);
    }

    /// <summary>
    /// Both languages have the same keys, except the extra Arabic plural forms (<c>_Two</c>, <c>_Few</c>, <c>_Many</c>)
    /// of keys that exist in both. An English key missing in Arabic would show English inside the Arabic UI.
    /// </summary>
    /// <summary>Blueprint 6 hides the native box only when the input has <c>bp6-control-input</c>; without it two boxes show.</summary>
    [Fact]
    public void Blueprint_checkboxes_and_switches_hide_the_native_input()
    {
        var offending = SourceFiles(Path.Combine(RepoRoot, "src"))
            .Where(f => f.EndsWith(".razor", StringComparison.Ordinal))
            .SelectMany(f => BlueprintControl().Matches(File.ReadAllText(f))
                .Where(m => !m.Groups["input"].Value.Contains("bp6-control-input", StringComparison.Ordinal))
                .Select(m => $"{Path.GetFileName(f)}: {m.Groups["input"].Value.Trim()}"))
            .ToList();

        Assert.Empty(offending);
    }

    [Fact]
    public void Every_resource_file_has_the_same_keys_in_english_and_arabic()
    {
        var problems = Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.ar.resx", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(arabic =>
            {
                var english = arabic.Replace(".ar.resx", ".resx", StringComparison.Ordinal);
                var en = Keys(english);
                var ar = Keys(arabic);
                return en.Except(ar).Select(k => $"{Path.GetFileName(arabic)} misses {k}")
                    .Concat(ar.Except(en).Where(k => !IsArabicPluralOf(k, en)).Select(k => $"{Path.GetFileName(english)} misses {k}"));
            })
            .ToList();

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("ar-IQ", 1, "_One")]
    [InlineData("ar-IQ", 2, "_Two")]
    [InlineData("ar-IQ", 3, "_Few")]
    [InlineData("ar-IQ", 10, "_Few")]
    [InlineData("ar-IQ", 11, "_Many")]
    [InlineData("ar-IQ", 99, "_Many")]
    [InlineData("ar-IQ", 100, "")]
    [InlineData("ar-IQ", 103, "_Few")]
    [InlineData("ar-IQ", 0, "")]
    [InlineData("en-US", 1, "_One")]
    [InlineData("en-US", 2, "")]
    [InlineData("en-US", 0, "")]
    public void Counts_pick_the_plural_form_of_the_language(string culture, int count, string suffix) =>
        Assert.Equal(suffix, Plural.Suffix(count, CultureInfo.GetCultureInfo(culture)));

    private static bool IsArabicPluralOf(string key, HashSet<string> englishKeys) =>
        PluralSuffixes.FirstOrDefault(s => key.EndsWith(s, StringComparison.Ordinal)) is { } suffix
        && englishKeys.Contains(key[..^suffix.Length]);

    private static readonly string[] PluralSuffixes = ["_Two", "_Few", "_Many"];

    [Theory]
    [InlineData("en-US")]
    [InlineData("ar-IQ")]
    public void Every_domain_value_has_a_display_text_in_both_languages(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var missing = typeof(ShipmentTexts).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == nameof(ShipmentTexts.Of) && m.GetParameters() is [{ ParameterType.IsEnum: true }])
                .SelectMany(m => Enum.GetValues(m.GetParameters()[0].ParameterType).Cast<object>()
                    .Select(value => (Value: $"{value.GetType().Name}.{value}", Text: (string)m.Invoke(null, [value])!)))
                .Where(t => t.Text.Contains('_', StringComparison.Ordinal) && !t.Text.Contains(' ', StringComparison.Ordinal))
                .Select(t => t.Value)
                .ToList();

            Assert.Empty(missing);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Latin_codes_inside_arabic_text_are_isolated_left_to_right()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ar-IQ");
            var parts = Bidi.Split("علي (Baghdad-03) · 4×6").ToList();

            Assert.Contains(("Baghdad-03", true), parts);
            Assert.Contains(("4×6", true), parts);
            Assert.Contains(parts, p => p.Text.Contains('(', StringComparison.Ordinal) && !p.Ltr); // brackets mirror with the Arabic text
            Assert.Equal("علي (Baghdad-03) · 4×6", string.Concat(parts.Select(p => p.Text)));
            Assert.Equal("arrow-left", Bidi.Forward);

            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            Assert.Equal([("Baghdad-03", false)], Bidi.Split("Baghdad-03"));
            Assert.Equal("arrow-right", Bidi.Forward);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static IEnumerable<string> SourceFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

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

    // margin-left, padding-right, border-left…, a bare left:/right: property, or text-align/float to a physical side.
    [GeneratedRegex(@"(?<![\w-])(?:(?:margin|padding|border)-(?:left|right)(?:-\w+)?|left|right)\s*:|(?:text-align|float)\s*:\s*(?:left|right)")]
    private static partial Regex PhysicalDirection();

    // A <label class="bp6-control bp6-checkbox|switch|radio"> and the first input element inside it.
    [GeneratedRegex("""<label class="[^"]*\bbp6-control\b[^"]*\bbp6-(?:checkbox|switch|radio)\b[^"]*"[^>]*>\s*(?<input><(?:input|InputCheckbox|InputRadio)\b[^>]*>)""")]
    private static partial Regex BlueprintControl();

    [GeneratedRegex(@"\.bp6-icon-([a-z0-9-]+)::before")]
    private static partial Regex IconClass();

    // Icon="…", RightIcon="…", <Icon Name="…">, bp6-icon-… classes, and icon names returned from switch arms.
    [GeneratedRegex("""(?:\b(?:Icon|RightIcon)="(?<icon>[a-z][a-z0-9-]*)"|<Icon Name="(?<icon>[a-z][a-z0-9-]*)"|bp6-icon-(?<icon>(?!standard\b|large\b)[a-z][a-z0-9-]*)|=>\s*"(?<icon>[a-z]+(?:-[a-z0-9]+)*)",)""")]
    private static partial Regex IconUsage();
}
