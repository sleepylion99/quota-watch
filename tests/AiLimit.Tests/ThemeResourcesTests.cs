using System.Reflection;
using System.Xml.Linq;
using AiLimit.App.Theming;

namespace AiLimit.Tests;

/// <summary>
/// Verifies that Colors.Dark.xaml, Colors.Light.xaml, and Brushes.xaml are structurally
/// consistent with each other and with the BrushKey constant definitions.
/// All validation is done by plain XML parsing — no WPF runtime required.
/// </summary>
public sealed class ThemeResourcesTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "quota-watch.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static XDocument LoadThemeXaml(string fileName)
    {
        var path = Path.Combine(RepoRoot(), "src", "AiLimit.App", "Theming", "Themes", fileName);
        Assert.True(File.Exists(path), $"Theme file not found: {path}");
        return XDocument.Load(path);
    }

    private static readonly XNamespace Xns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static HashSet<string> GetColorKeys(XDocument doc) =>
        doc.Root!
           .Elements(Xns + "Color")
           .Select(e => (string?)e.Attribute(X + "Key"))
           .Where(k => k is not null)
           .Select(k => k!)
           .ToHashSet();

    private static IReadOnlyList<XElement> GetBrushElements(XDocument doc) =>
        doc.Root!
           .Elements(Xns + "SolidColorBrush")
           .ToList();

    private static IReadOnlyList<string> GetAllBrushKeyValues()
    {
        return typeof(BrushKey)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DarkAndLightColorDictionariesDefineExactlySameKeys()
    {
        var dark = LoadThemeXaml("Colors.Dark.xaml");
        var light = LoadThemeXaml("Colors.Light.xaml");

        var darkKeys = GetColorKeys(dark);
        var lightKeys = GetColorKeys(light);

        var onlyInDark = darkKeys.Except(lightKeys).OrderBy(k => k).ToList();
        var onlyInLight = lightKeys.Except(darkKeys).OrderBy(k => k).ToList();

        Assert.True(onlyInDark.Count == 0,
            $"Keys in Dark but not Light:\n  {string.Join("\n  ", onlyInDark)}");
        Assert.True(onlyInLight.Count == 0,
            $"Keys in Light but not Dark:\n  {string.Join("\n  ", onlyInLight)}");
    }

    [Fact]
    public void BrushesXamlContainsEntryForEveryBrushKeyConstant()
    {
        var brushes = LoadThemeXaml("Brushes.xaml");
        var brushKeys = GetBrushElements(brushes)
            .Select(e => (string?)e.Attribute(X + "Key"))
            .Where(k => k is not null)
            .Select(k => k!)
            .ToHashSet();

        var constants = GetAllBrushKeyValues();
        var missing = constants.Where(c => !brushKeys.Contains(c)).OrderBy(k => k).ToList();

        Assert.True(missing.Count == 0,
            $"BrushKey constants without a SolidColorBrush entry in Brushes.xaml:\n  {string.Join("\n  ", missing)}");
    }

    [Fact]
    public void EveryBrushInBrushesXamlUsesDynamicResourceForColor()
    {
        var brushes = LoadThemeXaml("Brushes.xaml");
        var badBrushes = new List<string>();

        foreach (var el in GetBrushElements(brushes))
        {
            var key = (string?)el.Attribute(X + "Key") ?? "<unknown>";
            var colorAttr = (string?)el.Attribute("Color");

            if (colorAttr is null ||
                !colorAttr.StartsWith("{DynamicResource Color.", StringComparison.Ordinal) ||
                !colorAttr.EndsWith("}"))
            {
                badBrushes.Add($"{key}: Color=\"{colorAttr}\"");
            }
        }

        Assert.True(badBrushes.Count == 0,
            $"Brushes that do not use '{{DynamicResource Color.*}}' syntax:\n  {string.Join("\n  ", badBrushes)}");
    }

    [Fact]
    public void EachBrushColorReferenceExistsInBothColorDictionaries()
    {
        var brushes = LoadThemeXaml("Brushes.xaml");
        var dark = LoadThemeXaml("Colors.Dark.xaml");
        var light = LoadThemeXaml("Colors.Light.xaml");

        var darkKeys = GetColorKeys(dark);
        var lightKeys = GetColorKeys(light);

        var missingFromDark = new List<string>();
        var missingFromLight = new List<string>();

        foreach (var el in GetBrushElements(brushes))
        {
            var brushKey = (string?)el.Attribute(X + "Key") ?? "<unknown>";
            var colorAttr = (string?)el.Attribute("Color") ?? "";

            // Extract "Color.Surface.Window" from "{DynamicResource Color.Surface.Window}"
            if (!colorAttr.StartsWith("{DynamicResource ", StringComparison.Ordinal)) continue;
            var colorKey = colorAttr["{DynamicResource ".Length..^1];

            if (!darkKeys.Contains(colorKey))
                missingFromDark.Add($"Brush '{brushKey}' references Color key '{colorKey}' which is absent from Colors.Dark.xaml");
            if (!lightKeys.Contains(colorKey))
                missingFromLight.Add($"Brush '{brushKey}' references Color key '{colorKey}' which is absent from Colors.Light.xaml");
        }

        Assert.True(missingFromDark.Count == 0,
            $"Color references missing from Colors.Dark.xaml:\n  {string.Join("\n  ", missingFromDark)}");
        Assert.True(missingFromLight.Count == 0,
            $"Color references missing from Colors.Light.xaml:\n  {string.Join("\n  ", missingFromLight)}");
    }
}
