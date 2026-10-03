using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniPrinter.Gui;

public enum ThemeChoice
{
    Auto,
    Light,
    Dark,
}

public enum TextSizeChoice
{
    Small,
    Normal,
    Large,
}

/// <summary>How the Templates tab lists the templates.</summary>
public enum TemplatesViewChoice
{
    List,
    Gallery,
}

/// <summary>Position and size of the panel, in device-independent units.</summary>
public sealed record WindowBounds(double Left, double Top, double Width, double Height, bool Maximized = false);

/// <summary>
/// Per-user tray preferences (%LocalAppData%\MiniPrinter\tray.json): the quick-note hotkey, the look of the panel and
/// where the window was left. Reading is tolerant: an old, partial or damaged file gives the defaults for what it lacks.
/// </summary>
public sealed record TrayPreferences
{
    public string QuickNoteHotkey { get; init; } = "Ctrl+Alt+P";
    public float QuickNoteSizePt { get; init; } = 12;

    public ThemeChoice Theme { get; init; } = ThemeChoice.Auto;
    public TextSizeChoice TextSize { get; init; } = TextSizeChoice.Normal;

    /// <summary>Where the panel was last left (null: centre it).</summary>
    public WindowBounds? Window { get; init; }

    /// <summary>The tab the panel was on (index in the tab control).</summary>
    public int Tab { get; init; }

    /// <summary>The Settings section the panel was on.</summary>
    public string? SettingsSection { get; init; }

    public TemplatesViewChoice TemplatesView { get; init; } = TemplatesViewChoice.Gallery;

    /// <summary>Whether the "Details" expander of the Status tab was open.</summary>
    public bool StatusDetailsOpen { get; init; }

    /// <summary>Templates printed from the Templates tab, most recent first (at most <see cref="MaxRecentTemplates"/>).</summary>
    public IReadOnlyList<string> RecentTemplates { get; init; } = [];

    public const int MaxRecentTemplates = 5;

    /// <summary>The file of the user (the MINIPRINTER_TRAY_PREFS variable points tools and tests to another one).</summary>
    public static string DefaultPath { get; } = Environment.GetEnvironmentVariable("MINIPRINTER_TRAY_PREFS") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniPrinter", "tray.json");

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>A copy with <paramref name="name"/> at the front of the recent templates.</summary>
    public TrayPreferences WithRecentTemplate(string name)
    {
        var list = new List<string> { name };
        list.AddRange(RecentTemplates.Where(t => !string.Equals(t, name, StringComparison.OrdinalIgnoreCase)));
        return this with { RecentTemplates = list.Take(MaxRecentTemplates).ToList() };
    }

    public static TrayPreferences Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : new TrayPreferences();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new TrayPreferences();
        }
    }

    /// <summary>Reads each setting on its own, so one bad value does not lose the rest.</summary>
    public static TrayPreferences Parse(string json)
    {
        var result = new TrayPreferences();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return result;
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return result;

            string? Text(string name) => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            double? Number(JsonElement parent, string name) =>
                parent.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;
            bool TryEnum<T>(string name, out T value) where T : struct, Enum
            {
                value = default;
                if (!root.TryGetProperty(name, out var e))
                    return false;
                if (e.ValueKind == JsonValueKind.String)
                    return System.Enum.TryParse(e.GetString(), true, out value) && System.Enum.IsDefined(value);
                if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) && System.Enum.IsDefined(typeof(T), n))
                {
                    value = (T)System.Enum.ToObject(typeof(T), n);
                    return true;
                }
                return false;
            }
            if (Text(nameof(QuickNoteHotkey)) is { Length: > 0 } hotkey)
                result = result with { QuickNoteHotkey = hotkey };
            if (Number(root, nameof(QuickNoteSizePt)) is { } size && size is >= 6 and <= 72)
                result = result with { QuickNoteSizePt = (float)size };
            if (TryEnum<ThemeChoice>(nameof(Theme), out var theme))
                result = result with { Theme = theme };
            if (TryEnum<TextSizeChoice>(nameof(TextSize), out var textSize))
                result = result with { TextSize = textSize };
            if (TryEnum<TemplatesViewChoice>(nameof(TemplatesView), out var view))
                result = result with { TemplatesView = view };
            if (Number(root, nameof(Tab)) is { } tab && tab is >= 0 and < 20)
                result = result with { Tab = (int)tab };
            if (Text(nameof(SettingsSection)) is { Length: > 0 } section)
                result = result with { SettingsSection = section };
            if (root.TryGetProperty(nameof(StatusDetailsOpen), out var details) && details.ValueKind is JsonValueKind.True or JsonValueKind.False)
                result = result with { StatusDetailsOpen = details.GetBoolean() };
            if (root.TryGetProperty(nameof(RecentTemplates), out var recent) && recent.ValueKind == JsonValueKind.Array)
                result = result with
                {
                    RecentTemplates = recent.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)
                        .Where(s => s.Length > 0).Take(MaxRecentTemplates).ToList(),
                };
            if (root.TryGetProperty(nameof(Window), out var window) && window.ValueKind == JsonValueKind.Object
                && Number(window, nameof(WindowBounds.Left)) is { } left && Number(window, nameof(WindowBounds.Top)) is { } top
                && Number(window, nameof(WindowBounds.Width)) is { } width && Number(window, nameof(WindowBounds.Height)) is { } height
                && width > 0 && height > 0)
            {
                var maximized = window.TryGetProperty(nameof(WindowBounds.Maximized), out var m) && m.ValueKind == JsonValueKind.True;
                result = result with { Window = new WindowBounds(left, top, width, height, maximized) };
            }
        }
        return result;
    }

    /// <summary>Writes the file atomically (temporary file and move), so a crash never leaves it half written.</summary>
    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, WriteOptions));
        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>A monitor's working area, in device-independent units.</summary>
public readonly record struct ScreenArea(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

/// <summary>Decides where a remembered window can be shown again.</summary>
public static class WindowPlacement
{
    /// <summary>The part of the title bar that must be on a screen for the window to be reachable.</summary>
    private const double MinVisibleWidth = 120;
    private const double MinVisibleHeight = 40;

    /// <summary>
    /// The saved bounds adjusted to the screens that exist now (never smaller than the minimum size, never bigger than the
    /// screen it is on), or null when the saved position is not on any screen and the window must be centred.
    /// </summary>
    public static WindowBounds? Validate(WindowBounds? saved, IReadOnlyList<ScreenArea> screens, double minWidth, double minHeight)
    {
        if (saved is null || screens.Count == 0)
            return null;

        var width = Math.Max(saved.Width, minWidth);
        var height = Math.Max(saved.Height, minHeight);
        // The title bar: the top strip of the window.
        foreach (var screen in screens)
        {
            var visibleWidth = Math.Min(saved.Left + width, screen.Right) - Math.Max(saved.Left, screen.Left);
            var visibleHeight = Math.Min(saved.Top + MinVisibleHeight, screen.Bottom) - Math.Max(saved.Top, screen.Top);
            if (visibleWidth < MinVisibleWidth || visibleHeight < MinVisibleHeight)
                continue;
            width = Math.Min(width, Math.Max(screen.Width, minWidth));
            height = Math.Min(height, Math.Max(screen.Height, minHeight));
            var left = Math.Clamp(saved.Left, screen.Left, Math.Max(screen.Left, screen.Right - width));
            var top = Math.Clamp(saved.Top, screen.Top, Math.Max(screen.Top, screen.Bottom - height));
            return new WindowBounds(left, top, width, height, saved.Maximized);
        }
        return null;
    }
}
