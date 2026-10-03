using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MiniPrinter.Gui.Tests;

/// <summary>
/// Keeps the interface texts in <c>Strings.resx</c>: no visible literal in the tray's XAML or code (spec tray-localization),
/// every key used exists, and no key is left unused. A literal that is not a text of the interface (a file filter, a product
/// name, a protocol word) is marked on its line with <c>// i18n-ok</c>.
/// </summary>
public partial class TextUsageTests
{
    private static string TrayDirectory([CallerFilePath] string? self = null) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(self)!, "..", "..", "src", "MiniPrinter.Tray"));

    private static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(TrayDirectory(), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static bool HasLetters(string value) => LettersPattern().IsMatch(value);

    private static readonly string[] AllowedLiterals = ["MiniPrinter"];

    [GeneratedRegex(@"\p{L}{2}")]
    private static partial Regex LettersPattern();

    // XAML: Text="…", Content="…", Header="…", ToolTip="…", Title="…" with a literal that is not a {binding or extension}
    [GeneratedRegex(@"(?<![A-Za-z.])(?:Text|Content|Header|ToolTip|Title)=""(?<value>[^""{][^""]*)""")]
    private static partial Regex XamlAttribute();

    // Code: the properties that show text, assigned a string literal (plain or interpolated)
    [GeneratedRegex(@"\b(?:Text|Content|Header|Title|ToolTip|ShortcutKeyDisplayString)\s*=\s*\$?""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex CodeAssignment();

    // Code: calls whose first literal argument is shown to the user
    [GeneratedRegex(@"\b(?:MessageBox\.Show|SetStatus|ShowBanner|Say|Label|Banner|Notify|_notify|new ToolStripMenuItem|new NameDialog)\((?:this,\s*|_ui\.Owner,\s*)?\$?""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex CodeCall();

    private static IEnumerable<string> Violations(string file, Regex pattern)
    {
        var lines = File.ReadAllLines(file);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Contains("// i18n-ok") || line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("///"))
                continue;
            foreach (Match match in pattern.Matches(line))
            {
                var value = match.Groups["value"].Value;
                if (HasLetters(value) && !AllowedLiterals.Contains(value))
                    yield return $"{Path.GetFileName(file)}:{i + 1}: {value}";
            }
        }
    }

    [Fact]
    public void The_xaml_has_no_visible_literal_texts()
    {
        var found = Files("*.xaml").SelectMany(f => Violations(f, XamlAttribute())).ToList();
        Assert.True(found.Count == 0, "Literal texts in XAML (use {loc:T Key}):\n" + string.Join("\n", found));
    }

    [Fact]
    public void The_code_has_no_visible_literal_texts()
    {
        var found = Files("*.cs").SelectMany(f => Violations(f, CodeAssignment()).Concat(Violations(f, CodeCall()))).ToList();
        Assert.True(found.Count == 0, "Literal texts in code (use Strings.Get, or mark // i18n-ok when it is not interface text):\n" + string.Join("\n", found));
    }

    private static readonly Regex CodeKey = new("""Strings\.Get\("(?<key>[A-Za-z0-9_.]+)"|"(?<key2>(?:Editor|Main|Tray|Quick|QuickNote|Templates|Paper|Update|Recreate|Name|Dialog|Service|Bluetooth|App)\.[A-Za-z0-9_.]+)"|\{loc:T (?<key3>[A-Za-z0-9_.]+)\}""");

    private static IEnumerable<string> LibraryFiles() =>
        Directory.EnumerateFiles(Path.Combine(TrayDirectory(), "..", "MiniPrinter.Gui"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static IEnumerable<string> UsedKeys() =>
        Files("*.cs").Concat(Files("*.xaml")).Concat(LibraryFiles())
            .SelectMany(f => CodeKey.Matches(string.Join("\n", File.ReadAllLines(f).Where(l => !l.TrimStart().StartsWith("//")))).Select(m => m.Groups["key"].Success ? m.Groups["key"].Value : m.Groups["key2"].Success ? m.Groups["key2"].Value : m.Groups["key3"].Value))
            .Distinct();

    [Fact]
    public void Every_key_the_tray_uses_exists_in_the_resource_file()
    {
        var missing = UsedKeys().Where(k => !k.EndsWith('.') && !Strings.Exists(k)).ToList(); // "Job.State." is a prefix completed at run time
        Assert.True(missing.Count == 0, "Keys used but missing from Strings.resx:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Every_key_in_the_resource_file_is_used_somewhere()
    {
        var used = UsedKeys().ToHashSet();
        // Keys built at run time ("Job.State." + state, "Alarm." + name) are used by the library itself.
        var libraryDirectory = Path.Combine(TrayDirectory(), "..", "MiniPrinter.Gui");
        var libraryText = string.Join("\n", Directory.EnumerateFiles(libraryDirectory, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        bool UsedByLibrary(string key) => libraryText.Contains($"\"{key}\"") || Regex.IsMatch(key, @"^(Job\.State|Alarm)\.[A-Za-z]+") && libraryText.Contains(key[..(key.LastIndexOf('.') + 1)] is var prefix ? $"\"{prefix}\"" : "") || key.StartsWith("Diag.", StringComparison.Ordinal) && libraryText.Contains("$\"Diag.");
        var unused = Strings.Keys.Where(k => !used.Contains(k) && !UsedByLibrary(k)).OrderBy(k => k).ToList();
        Assert.True(unused.Count == 0, "Keys in Strings.resx that nothing uses:\n" + string.Join("\n", unused));
    }
}
