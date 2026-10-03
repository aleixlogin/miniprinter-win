using System.Globalization;
using System.Resources;

namespace MiniPrinter.Gui;

/// <summary>
/// The texts of the interface (<c>Strings.resx</c>, Spanish by default). A missing key shows its own name in square
/// brackets, so the gap is visible instead of blank; a test fails before a release if a used key does not exist.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager = new("MiniPrinter.Gui.Strings", typeof(Strings).Assembly);

    public static string Get(string key) => Manager.GetString(key) ?? $"[{key}]";

    public static string Get(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    public static bool Exists(string key) => Manager.GetString(key) is not null;

    /// <summary>Every key in the resource file (for the tests that check the usage of the texts).</summary>
    public static IReadOnlyCollection<string> Keys
    {
        get
        {
            var set = Manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
            return set is null ? [] : set.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToList();
        }
    }
}
