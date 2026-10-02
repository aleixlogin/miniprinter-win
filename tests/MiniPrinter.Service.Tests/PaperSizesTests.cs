using Microsoft.Extensions.Logging.Abstractions;
using MiniPrinter.Control;

namespace MiniPrinter.Service.Tests;

/// <summary>The paper sizes setting: factory defaults, own sizes, validation and normalization.</summary>
public class PaperSizesTests
{
    private static ServiceSettings Validate(IReadOnlyList<PaperSizeSetting>? list, string? defaultId = null) =>
        SettingsStore.Validate(new ServiceSettings { PaperSizes = list, DefaultPaperId = defaultId });

    private static PaperSizeSetting Own(int w, int l, string name = "Mío", bool enabled = true) =>
        new(PaperCatalog.CustomId(w, l), name, w, l, enabled);

    private static List<PaperSizeSetting> Presets(Func<PaperSizeSetting, bool>? enabled = null) =>
        [.. PaperCatalog.Presets.Select(p => p with { Enabled = enabled?.Invoke(p) ?? true })];

    // ---- factory defaults and compatibility -------------------------------------------------------------

    [Fact]
    public void Without_a_saved_list_the_seven_factory_sizes_are_offered_like_before()
    {
        var settings = new ServiceSettings();
        var effective = PaperCatalog.Effective(settings);
        Assert.Equal(["48x210", "48x100", "48x50", "48x297", "48x1000", "80x297", "80x100"], effective.Select(p => p.Id));
        Assert.Equal("om_x5h-48x210mm_48x210mm", effective[0].IppName);
        Assert.Equal("om_x5h-48x1000mm_48x1000mm", effective[4].IppName);
        Assert.Equal("48x210", PaperCatalog.DefaultId(settings));
    }

    [Fact]
    public void An_old_settings_file_gets_the_same_sizes_as_before()
    {
        var dir = TestEnv.TempDirectory();
        File.WriteAllText(Path.Combine(dir, "settings.json"), """{"networkMode":"Local","ippPort":8631,"printerName":"X5h Thermal Printer"}""");
        var settings = new SettingsStore(new ServicePaths(dir), NullLogger<SettingsStore>.Instance).Current;
        Assert.Null(settings.PaperSizes);
        Assert.Equal(7, PaperCatalog.Effective(settings).Count);
        Assert.Equal("48x210", PaperCatalog.Effective(settings)[0].Id);
    }

    [Fact]
    public void Factory_presets_have_the_expected_measures()
    {
        Assert.Equal([(48, 210), (48, 100), (48, 50), (48, 297), (48, 1000), (80, 297), (80, 100)],
            PaperCatalog.Presets.Select(p => (p.WidthMm, p.LengthMm)));
        Assert.All(PaperCatalog.Presets, p => Assert.True(p.Preset && p.Enabled));
    }

    // ---- own sizes -----------------------------------------------------------------------------------

    [Fact]
    public void An_own_size_gets_an_id_and_ipp_key_from_its_measures()
    {
        var list = Presets();
        list.Add(Own(57, 150, "Ticket ancho"));
        var saved = Validate(list).PaperSizes!.Single(p => !p.Preset);
        Assert.Equal("c57x150", saved.Id);
        Assert.Equal("om_x5h-57x150mm_57x150mm", saved.IppName);
        Assert.Equal("Ticket ancho", saved.Name);
        Assert.Equal(4.5, saved.SideMarginMm);
    }

    [Theory]
    [InlineData(60, 100, 57, 100)]    // too wide: corrected to the maximum
    [InlineData(20, 100, 30, 100)]    // too narrow
    [InlineData(40, 5, 40, 10)]       // too short
    [InlineData(40, 5000, 40, 1000)]  // too long
    public void File_values_out_of_range_are_corrected(int w, int l, int expectedW, int expectedL)
    {
        var list = Presets();
        list.Add(new PaperSizeSetting("x", "Fuera de rango", w, l));
        var saved = Validate(list).PaperSizes!.Single(p => !p.Preset);
        Assert.Equal((expectedW, expectedL), (saved.WidthMm, saved.LengthMm));
        Assert.Equal(PaperCatalog.CustomId(expectedW, expectedL), saved.Id);
    }

    [Fact]
    public void The_api_rejects_what_the_file_would_correct()
    {
        var list = Presets();
        list.Add(Own(60, 100));
        Assert.Contains("57", PaperCatalog.Problem(list));
        Assert.Contains("entre 10 y 1000", PaperCatalog.Problem([.. Presets(), Own(40, 5)]));
        Assert.Contains("nombre", PaperCatalog.Problem([.. Presets(), Own(40, 100, "")]));
        Assert.Contains("nombre", PaperCatalog.Problem([.. Presets(), Own(40, 100, new string('a', 41))]));
        Assert.Null(PaperCatalog.Problem([.. Presets(), Own(57, 1000, new string('a', 40))]));
    }

    [Fact]
    public void Repeated_measures_are_rejected_by_the_api_and_dropped_from_files()
    {
        var withPresetDuplicate = new List<PaperSizeSetting>([.. Presets(), Own(48, 100, "Igual que un preajuste")]);
        Assert.Contains("48 × 100", PaperCatalog.Problem(withPresetDuplicate));
        var cleaned = Validate(withPresetDuplicate).PaperSizes!;
        Assert.Single(cleaned, p => p.WidthMm == 48 && p.LengthMm == 100);
        Assert.Equal(7, cleaned.Count);

        var twice = new List<PaperSizeSetting>([.. Presets(), Own(40, 60, "A"), Own(40, 60, "B")]);
        Assert.NotNull(PaperCatalog.Problem(twice));
        Assert.Single(Validate(twice).PaperSizes!, p => !p.Preset);
    }

    [Fact]
    public void At_most_thirty_sizes()
    {
        var many = new List<PaperSizeSetting>(Presets());
        for (var i = 0; i < 40; i++)
            many.Add(Own(30 + i % 28, 10 + i * 7, $"S{i}"));
        Assert.Contains("30", PaperCatalog.Problem(many));
        Assert.Equal(30, Validate(many).PaperSizes!.Count);
    }

    [Fact]
    public void Names_are_cleaned_and_empty_ones_get_the_measures()
    {
        var saved = Validate([.. Presets(), Own(50, 100, "  \u0007Etiqueta\n  "), Own(40, 60, "   ")]).PaperSizes!.Where(p => !p.Preset).ToList();
        Assert.Equal("Etiqueta", saved.Single(p => p.WidthMm == 50).Name);
        Assert.Equal("40 × 60 mm", saved.Single(p => p.WidthMm == 40).Name);
    }

    // ---- presets ---------------------------------------------------------------------------------------

    [Fact]
    public void A_preset_keeps_its_measures_and_only_its_enabled_flag_is_respected()
    {
        var tampered = Presets().Select(p => p.Id == "48x100" ? p with { WidthMm = 30, LengthMm = 30, Name = "Otro", Enabled = false } : p).ToList();
        var saved = Validate(tampered).PaperSizes!.Single(p => p.Id == "48x100");
        Assert.Equal((48, 100, false, "48 × 100 mm"), (saved.WidthMm, saved.LengthMm, saved.Enabled, saved.Name));
    }

    [Fact]
    public void A_missing_preset_comes_back_disabled_and_presets_cannot_be_deleted()
    {
        var withoutRoll = Presets().Where(p => p.Id != "48x1000").ToList();
        var saved = Validate(withoutRoll).PaperSizes!;
        Assert.False(saved.Single(p => p.Id == "48x1000").Enabled);
        Assert.DoesNotContain(PaperCatalog.Effective(new ServiceSettings { PaperSizes = saved }), p => p.Id == "48x1000");
    }

    [Fact]
    public void Restoring_factory_values_is_a_null_list()
    {
        var custom = Validate([.. Presets(), Own(57, 150)]);
        var restored = SettingsStore.Validate(custom with { PaperSizes = null, DefaultPaperId = null });
        Assert.Null(restored.PaperSizes);
        Assert.Equal(7, PaperCatalog.Effective(restored).Count);
    }

    [Fact]
    public void Disabling_the_roll_stops_offering_it()
    {
        var settings = Validate(Presets(p => p.Id != "48x1000"));
        Assert.DoesNotContain(PaperCatalog.Effective(settings), p => p.Id == "48x1000");
        Assert.Equal(6, PaperCatalog.Effective(settings).Count);
    }

    // ---- at least one active size and the default -------------------------------------------------------

    [Fact]
    public void With_everything_disabled_48x210_is_enabled_again_and_is_the_default()
    {
        var settings = Validate(Presets(_ => false));
        Assert.Equal(["48x210"], PaperCatalog.Effective(settings).Select(p => p.Id));
        Assert.Equal("48x210", settings.DefaultPaperId);
        Assert.Null(PaperCatalog.Problem(Presets(_ => false)) is null ? "x" : null);   // the API rejects an all-disabled list
    }

    [Fact]
    public void The_chosen_default_goes_first()
    {
        var settings = Validate(Presets(), "48x100");
        Assert.Equal("48x100", settings.DefaultPaperId);
        Assert.Equal("48x100", PaperCatalog.Effective(settings)[0].Id);
        Assert.Equal(7, PaperCatalog.Effective(settings).Count);
    }

    [Fact]
    public void A_disabled_or_unknown_default_falls_back_to_the_first_enabled_size()
    {
        var disabledDefault = Validate(Presets(p => p.Id != "48x100"), "48x100");
        Assert.Equal("48x210", disabledDefault.DefaultPaperId);

        var firstDisabled = Validate(Presets(p => p.Id != "48x210"), "48x210");
        Assert.Equal("48x100", firstDisabled.DefaultPaperId);

        Assert.Equal("48x210", Validate(Presets(), "no-existe").DefaultPaperId);
    }

    [Fact]
    public void An_own_size_can_be_the_default()
    {
        var settings = Validate([.. Presets(), Own(57, 150, "Ticket")], "c57x150");
        Assert.Equal("c57x150", PaperCatalog.Effective(settings)[0].Id);
    }

    // ---- store ---------------------------------------------------------------------------------------------

    [Fact]
    public void Saving_the_same_sizes_does_not_look_like_a_change()
    {
        var dir = TestEnv.TempDirectory();
        var store = new SettingsStore(new ServicePaths(dir), NullLogger<SettingsStore>.Instance);
        store.Update(s => s with { PaperSizes = [.. Presets(), Own(57, 150)] });
        var changes = 0;
        store.Changed += (_, _) => changes++;
        store.Update(s => s with { PaperSizes = [.. Presets(), Own(57, 150)] });   // an equal list, other instance
        Assert.Equal(0, changes);
        store.Update(s => s with { PaperSizes = [.. Presets(), Own(57, 100)] });
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Sizes_survive_a_restart()
    {
        var dir = TestEnv.TempDirectory();
        new SettingsStore(new ServicePaths(dir), NullLogger<SettingsStore>.Instance)
            .Update(s => s with { PaperSizes = [.. Presets(p => p.Id != "80x297"), Own(57, 150, "Ticket")], DefaultPaperId = "c57x150" });
        var loaded = new SettingsStore(new ServicePaths(dir), NullLogger<SettingsStore>.Instance).Current;
        Assert.Equal("c57x150", PaperCatalog.Effective(loaded)[0].Id);
        Assert.DoesNotContain(PaperCatalog.Effective(loaded), p => p.Id == "80x297");
        Assert.Equal("Ticket", loaded.PaperSizes!.Single(p => !p.Preset).Name);
    }

    [Fact]
    public void Margins_apply_only_to_own_sizes_wider_than_the_head()
    {
        Assert.Equal(0, PaperCatalog.Presets.Single(p => p.Id == "80x297").SideMarginMm);   // virtual presets are untouched
        Assert.Equal(0, Own(48, 100).SideMarginMm);
        Assert.Equal(0, Own(40, 100).SideMarginMm);
        Assert.Equal(1.0, Own(50, 100).SideMarginMm);
        Assert.Equal(4.5, Own(57, 100).SideMarginMm);
    }
}
