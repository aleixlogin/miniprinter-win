namespace MiniPrinter.Gui.Tests;

public class TrayPreferencesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miniprinter-gui-tests", Guid.NewGuid().ToString("N"));
    private string PathOf(string name = "tray.json") => Path.Combine(_dir, name);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Without_a_file_the_defaults_are_used()
    {
        var prefs = TrayPreferences.Load(PathOf());
        Assert.Equal(("Ctrl+Alt+P", 12f, ThemeChoice.Auto, TextSizeChoice.Normal), (prefs.QuickNoteHotkey, prefs.QuickNoteSizePt, prefs.Theme, prefs.TextSize));
        Assert.Null(prefs.Window);
        Assert.Equal(0, prefs.Tab);
    }

    [Fact]
    public void Saving_and_loading_gives_the_same_preferences()
    {
        var original = new TrayPreferences
        {
            QuickNoteHotkey = "Ctrl+Shift+N",
            QuickNoteSizePt = 16,
            Theme = ThemeChoice.Dark,
            TextSize = TextSizeChoice.Large,
            Window = new WindowBounds(120.5, 80, 900, 640, Maximized: true),
            Tab = 3,
            SettingsSection = "Red",
            TemplatesView = TemplatesViewChoice.List,
            StatusDetailsOpen = true,
            RecentTemplates = ["qr", "label"],
        };
        original.Save(PathOf());
        var loaded = TrayPreferences.Load(PathOf());
        Assert.Equal(original.Window, loaded.Window);
        Assert.Equal(original with { RecentTemplates = [] }, loaded with { RecentTemplates = [] });
        Assert.Equal(original.RecentTemplates, loaded.RecentTemplates);
    }

    [Fact]
    public void A_file_written_by_the_previous_version_still_loads_with_new_fields_defaulted()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathOf(), """{"QuickNoteHotkey":"Ctrl+Alt+K","QuickNoteSizePt":14}""");
        var prefs = TrayPreferences.Load(PathOf());
        Assert.Equal("Ctrl+Alt+K", prefs.QuickNoteHotkey);
        Assert.Equal(14f, prefs.QuickNoteSizePt);
        Assert.Equal(ThemeChoice.Auto, prefs.Theme);
        Assert.Null(prefs.Window);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    [InlineData("{\"Theme\": 42, \"Window\": 7, \"Tab\": \"x\", \"RecentTemplates\": 5}")]
    public void A_damaged_file_gives_the_defaults_without_failing(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathOf(), content);
        var prefs = TrayPreferences.Load(PathOf());
        Assert.Equal(new TrayPreferences(), prefs with { RecentTemplates = prefs.RecentTemplates });
    }

    [Fact]
    public void One_bad_value_does_not_lose_the_others()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathOf(), """{"QuickNoteHotkey":"Ctrl+Alt+K","Theme":"Purple","TextSize":"Large","Window":{"Left":1,"Top":2,"Width":-5,"Height":10}}""");
        var prefs = TrayPreferences.Load(PathOf());
        Assert.Equal("Ctrl+Alt+K", prefs.QuickNoteHotkey);
        Assert.Equal(ThemeChoice.Auto, prefs.Theme);      // unknown name: default
        Assert.Equal(TextSizeChoice.Large, prefs.TextSize);
        Assert.Null(prefs.Window);                        // negative width: not a window
    }

    [Fact]
    public void Saving_leaves_no_temporary_file_and_replaces_the_old_content()
    {
        new TrayPreferences { Tab = 1 }.Save(PathOf());
        new TrayPreferences { Tab = 2 }.Save(PathOf());
        Assert.Equal(2, TrayPreferences.Load(PathOf()).Tab);
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Recent_templates_keep_the_latest_first_without_repeats_and_at_most_five()
    {
        var prefs = new TrayPreferences();
        foreach (var name in new[] { "a", "b", "c", "a", "d", "e", "f", "g", "h", "i", "j" })
            prefs = prefs.WithRecentTemplate(name);
        Assert.Equal(TrayPreferences.MaxRecentTemplates, prefs.RecentTemplates.Count);
        Assert.Equal(["j", "i", "h", "g", "f"], prefs.RecentTemplates);
        Assert.Equal("c", prefs.WithRecentTemplate("c").RecentTemplates[0]);
    }

    // ---- window placement ----------------------------------------------------------------------------------

    private static readonly ScreenArea Primary = new(0, 0, 1920, 1040);
    private static readonly ScreenArea Second = new(1920, 0, 1280, 984);

    [Fact]
    public void A_position_on_a_connected_screen_is_kept()
    {
        var saved = new WindowBounds(200, 100, 900, 640);
        Assert.Equal(saved, WindowPlacement.Validate(saved, [Primary], 560, 440));
    }

    [Fact]
    public void A_window_that_was_on_a_screen_that_is_gone_is_centred()
    {
        var saved = new WindowBounds(2300, 100, 900, 640);
        Assert.Null(WindowPlacement.Validate(saved, [Primary], 560, 440));
        Assert.NotNull(WindowPlacement.Validate(saved, [Primary, Second], 560, 440));
    }

    [Fact]
    public void A_window_whose_title_bar_is_off_screen_is_not_reachable_and_is_centred()
    {
        Assert.Null(WindowPlacement.Validate(new WindowBounds(100, 5000, 900, 640), [Primary], 560, 440));
        Assert.Null(WindowPlacement.Validate(new WindowBounds(-2000, 100, 900, 640), [Primary], 560, 440));
    }

    [Fact]
    public void A_window_partly_off_screen_is_pulled_back_in_and_cannot_be_larger_than_the_screen()
    {
        var pulled = WindowPlacement.Validate(new WindowBounds(1500, 900, 900, 640), [Primary], 560, 440)!;
        Assert.InRange(pulled.Left + pulled.Width, 0, Primary.Right);
        Assert.InRange(pulled.Top + pulled.Height, 0, Primary.Bottom);

        var huge = WindowPlacement.Validate(new WindowBounds(0, 0, 5000, 4000), [Primary], 560, 440)!;
        Assert.Equal((Primary.Width, Primary.Height), (huge.Width, huge.Height));
    }

    [Fact]
    public void The_minimum_size_is_always_respected_and_no_screens_means_centre()
    {
        var small = WindowPlacement.Validate(new WindowBounds(100, 100, 100, 100), [Primary], 560, 440)!;
        Assert.Equal((560d, 440d), (small.Width, small.Height));
        Assert.Null(WindowPlacement.Validate(new WindowBounds(100, 100, 900, 640), [], 560, 440));
        Assert.Null(WindowPlacement.Validate(null, [Primary], 560, 440));
    }

    [Fact]
    public void Maximized_is_preserved()
    {
        var saved = new WindowBounds(100, 100, 900, 640, Maximized: true);
        Assert.True(WindowPlacement.Validate(saved, [Primary], 560, 440)!.Maximized);
    }
}
