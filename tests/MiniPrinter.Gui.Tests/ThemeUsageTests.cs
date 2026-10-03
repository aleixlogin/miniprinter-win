using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MiniPrinter.Gui.Tests;

/// <summary>
/// Spec tray-appearance, "Sin colores sueltos": every colour of the tray comes from the active theme, so that switching
/// the theme restyles everything. A colour that must stay the same in every theme (the paper of a preview, the painting of
/// the tray icon) is marked on its line with <c>// theme-ok</c>.
/// </summary>
public partial class ThemeUsageTests
{
    private static string TrayDirectory([CallerFilePath] string? self = null) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(self)!, "..", "..", "src", "MiniPrinter.Tray"));

    private static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(TrayDirectory(), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}"));

    // A colour written in the XAML: #RRGGBB, or a named brush in a colour property
    [GeneratedRegex(@"(?:Foreground|Background|BorderBrush|Fill|Stroke)=""(?<value>#[0-9A-Fa-f]{3,8}|White|Black|Gray|DimGray|LightGray|Red|Green|Blue|Firebrick|DarkGreen|DarkOrange|DarkGoldenrod)""")]
    private static partial Regex XamlColor();

    // A colour written in code: a named brush, a colour from components or a new brush of fixed colour
    [GeneratedRegex(@"\bBrushes\.[A-Z]\w+|\bColors\.[A-Z]\w+|\bColor\.From(?:Rgb|Argb)\(|new SolidColorBrush\(")]
    private static partial Regex CodeColor();

    private static IEnumerable<string> Violations(string file, Regex pattern)
    {
        var lines = File.ReadAllLines(file);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Contains("// theme-ok") || line.TrimStart().StartsWith("//"))
                continue;
            foreach (Match match in pattern.Matches(line))
                yield return $"{Path.GetFileName(file)}:{i + 1}: {match.Value}";
        }
    }

    [Fact]
    public void The_xaml_has_no_literal_colours()
    {
        var found = Files("*.xaml").SelectMany(f => Violations(f, XamlColor())).ToList();
        Assert.True(found.Count == 0, "Literal colours in XAML (use {DynamicResource Brush.…}):\n" + string.Join("\n", found));
    }

    [Fact]
    public void The_code_has_no_literal_colours()
    {
        var found = Files("*.cs").SelectMany(f => Violations(f, CodeColor())).ToList();
        Assert.True(found.Count == 0, "Literal colours in code (use Themed.*, or mark // theme-ok):\n" + string.Join("\n", found));
    }

    [Fact]
    public void The_three_palettes_define_the_same_brushes()
    {
        var themes = Path.Combine(TrayDirectory(), "Themes");
        var key = new Regex(@"x:Key=""(?<k>Brush\.[A-Za-z]+)""");
        HashSet<string> Keys(string name) => key.Matches(File.ReadAllText(Path.Combine(themes, name))).Select(m => m.Groups["k"].Value).ToHashSet();
        var light = Keys("Theme.Light.xaml");
        Assert.NotEmpty(light);
        Assert.Equal(light.OrderBy(k => k), Keys("Theme.Dark.xaml").OrderBy(k => k));
        Assert.Equal(light.OrderBy(k => k), Keys("Theme.HighContrast.xaml").OrderBy(k => k));
    }

    [Fact]
    public void Every_brush_the_tray_refers_to_exists_in_the_palettes()
    {
        var palette = Regex.Matches(File.ReadAllText(Path.Combine(TrayDirectory(), "Themes", "Theme.Light.xaml")), @"x:Key=""(?<k>Brush\.[A-Za-z]+)""")
            .Select(m => m.Groups["k"].Value).ToHashSet();
        var used = new Regex(@"(?:DynamicResource|StaticResource) (?<k>Brush\.[A-Za-z]+)|""(?<k2>Brush\.[A-Za-z]+)""");
        var missing = Directory.EnumerateFiles(TrayDirectory(), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".xaml") || f.EndsWith(".cs")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => used.Matches(File.ReadAllText(f)).Select(m => m.Groups["k"].Success ? m.Groups["k"].Value : m.Groups["k2"].Value))
            .Where(k => !palette.Contains(k)).Distinct().ToList();
        Assert.True(missing.Count == 0, "Brushes used but not defined in the palettes:\n" + string.Join("\n", missing));
    }
}
