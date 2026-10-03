using System.Reflection;
using MiniPrinter.Control;

namespace MiniPrinter.Gui.Tests;

/// <summary>tray-settings-navigation: sections, unsaved changes, validation, search and saving one section at a time.</summary>
public class SettingsViewModelTests
{
    private sealed class FakeGateway : ISettingsGateway
    {
        public ServiceSettings Service { get; set; } = new() { Printer = new PrinterSelection { Name = "X5h", Address = "AA" } };
        public List<ServiceSettings> Saved { get; } = [];
        public int Regenerated { get; private set; }

        public Task<ServiceSettings> GetAsync() => Task.FromResult(Service);

        public Task<ServiceSettings> SaveAsync(ServiceSettings settings)
        {
            Saved.Add(settings);
            Service = settings;
            return Task.FromResult(settings);
        }

        public Task<AutomationInfo> GetAutomationAsync() => Task.FromResult(new AutomationInfo(Service.AutomationApiEnabled, "tok-1", ["http://127.0.0.1:8631/api/v1"]));

        public Task<AutomationInfo> RegenerateTokenAsync()
        {
            Regenerated++;
            return Task.FromResult(new AutomationInfo(Service.AutomationApiEnabled, "tok-2", []));
        }
    }

    private sealed class FakeInteraction : ISettingsInteraction
    {
        public bool RawNetworkAnswer { get; set; } = true;
        public RecreateDecision RecreateAnswer { get; set; } = RecreateDecision.SaveOnly;
        public bool TokenAnswer { get; set; } = true;
        public bool OverwriteAnswer { get; set; } = true;
        public List<string> Asked { get; } = [];

        public Task<bool> ConfirmRawNetworkAsync(int port)
        {
            Asked.Add($"raw:{port}");
            return Task.FromResult(RawNetworkAnswer);
        }

        public Task<RecreateDecision> AskRecreateAsync()
        {
            Asked.Add("recreate");
            return Task.FromResult(RecreateAnswer);
        }

        public Task<bool> ConfirmOverwriteAsync()
        {
            Asked.Add("overwrite");
            return Task.FromResult(OverwriteAnswer);
        }

        public Task<bool> ConfirmNewTokenAsync()
        {
            Asked.Add("token");
            return Task.FromResult(TokenAnswer);
        }
    }

    private static (SettingsViewModel Model, FakeGateway Gateway, FakeInteraction Interaction) Create(string? selected = null)
    {
        var gateway = new FakeGateway();
        var interaction = new FakeInteraction();
        List<SettingsSectionViewModel> sections =
        [
            new PrintSectionViewModel(), new PaperSectionViewModel(), new NetworkSectionViewModel(), new RawPortSectionViewModel(),
            new AutomationSectionViewModel(), new BatterySectionViewModel(),
        ];
        var model = new SettingsViewModel(gateway, interaction, sections, selected);
        return (model, gateway, interaction);
    }

    private static T Section<T>(SettingsViewModel model) where T : SettingsSectionViewModel => model.Sections.OfType<T>().Single();

    // ---- loading and unsaved changes -------------------------------------------------------------------------------

    [Fact]
    public async Task Loading_shows_the_settings_and_nothing_is_unsaved()
    {
        var (model, gateway, _) = Create();
        gateway.Service = gateway.Service with { Darkness = 4, IppPort = 9000, LowBatteryPercent = 30 };
        await model.LoadAsync();
        Assert.Equal(4, Section<PrintSectionViewModel>(model).Darkness);
        Assert.Equal("9000", Section<NetworkSectionViewModel>(model).Port);
        Assert.Equal("30", Section<BatterySectionViewModel>(model).LowPercent);
        Assert.False(model.HasPendingChanges);
    }

    [Fact]
    public async Task A_change_marks_its_section_and_putting_it_back_clears_the_mark()
    {
        var (model, _, _) = Create();
        await model.LoadAsync();
        var print = Section<PrintSectionViewModel>(model);
        var original = print.Darkness;
        print.Darkness = original + 1;
        Assert.True(print.IsDirty);
        Assert.True(model.HasPendingChanges);
        Assert.False(Section<NetworkSectionViewModel>(model).IsDirty);
        print.Darkness = original;
        Assert.False(print.IsDirty);
        Assert.False(model.HasPendingChanges);
    }

    [Fact]
    public async Task Discarding_goes_back_to_what_was_loaded()
    {
        var (model, _, _) = Create();
        await model.LoadAsync();
        var network = Section<NetworkSectionViewModel>(model);
        network.Port = "1234";
        network.IsLan = true;
        network.Discard();
        Assert.Equal("8631", network.Port);
        Assert.True(network.IsLocal);
        Assert.False(network.IsDirty);
    }

    [Fact]
    public async Task Reloading_keeps_what_the_user_is_typing_unless_asked_to_discard()
    {
        var (model, gateway, _) = Create();
        await model.LoadAsync();
        var print = Section<PrintSectionViewModel>(model);
        print.Darkness = 5;
        gateway.Service = gateway.Service with { Darkness = 2, IppPort = 7000 };
        await model.LoadAsync();
        Assert.Equal(5, print.Darkness);
        Assert.Equal("7000", Section<NetworkSectionViewModel>(model).Port);
        await model.LoadAsync(discardChanges: true);
        Assert.Equal(2, print.Darkness);
    }

    // ---- validation --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("65535", true)]
    [InlineData("65536", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public async Task The_ipp_port_is_checked_next_to_its_field(string text, bool valid)
    {
        var (model, _, _) = Create();
        await model.LoadAsync();
        var network = Section<NetworkSectionViewModel>(model);
        network.Port = text;
        Assert.Equal(valid, network.ErrorOf("Port").Length == 0);
        Assert.Equal(!valid, network.HasErrors);
    }

    [Theory]
    [InlineData("1023", false)]
    [InlineData("1024", true)]
    [InlineData("9100", true)]
    public async Task The_raw_port_has_its_own_range(string text, bool valid)
    {
        var (model, _, _) = Create();
        await model.LoadAsync();
        var raw = Section<RawPortSectionViewModel>(model);
        raw.Port = text;
        Assert.Equal(valid, !raw.HasErrors);
    }

    [Fact]
    public async Task Applying_is_not_possible_while_there_are_errors()
    {
        var (model, gateway, _) = Create();
        await model.LoadAsync();
        model.Selected = Section<NetworkSectionViewModel>(model);
        Section<NetworkSectionViewModel>(model).Port = "x";
        Assert.False(model.Apply.CanExecute(null));
        var outcome = await model.ApplyAsync(Section<NetworkSectionViewModel>(model));
        Assert.Equal(ApplyStatus.Invalid, outcome.Status);
        Assert.Empty(gateway.Saved);
    }

    [Fact]
    public async Task Apply_is_enabled_only_with_valid_unsaved_changes_in_the_section_shown()
    {
        var (model, _, _) = Create();
        await model.LoadAsync();
        model.Selected = Section<BatterySectionViewModel>(model);
        Assert.False(model.Apply.CanExecute(null));
        Section<BatterySectionViewModel>(model).LowPercent = "25";
        Assert.True(model.Apply.CanExecute(null));
        Section<PrintSectionViewModel>(model).Darkness = 5; // another section: does not enable this one's button more
        Section<BatterySectionViewModel>(model).LowPercent = "4";
        Assert.False(model.Apply.CanExecute(null));
    }

    [Fact]
    public async Task The_apply_button_is_told_when_a_change_makes_it_possible_and_when_a_mistake_makes_it_impossible()
    {
        var (model, _, _) = Create();
        await model.LoadAsync();
        model.Selected = Section<NetworkSectionViewModel>(model);
        var told = 0;
        model.Apply.CanExecuteChanged += (_, _) => told++;

        Section<NetworkSectionViewModel>(model).Port = "9000";
        Assert.True(told > 0, "editing a field tells the button");
        Assert.True(model.Apply.CanExecute(null));

        told = 0;
        Section<NetworkSectionViewModel>(model).Port = "abc";
        Assert.True(told > 0);
        Assert.False(model.Apply.CanExecute(null));

        Section<NetworkSectionViewModel>(model).Discard();
        Assert.False(model.Apply.CanExecute(null));
    }

    [Fact]
    public async Task The_paper_list_and_the_name_are_checked()
    {
        var (model, _, _) = Create();
        await model.LoadAsync();
        var paper = Section<PaperSectionViewModel>(model);
        paper.PrinterName = "  ";
        Assert.NotEqual("", paper.ErrorOf("Name"));
        paper.PrinterName = "Mi impresora";
        Assert.Equal("", paper.ErrorOf("Name"));
        paper.SetSizes([.. PaperCatalog.Presets, new PaperSizeSetting("c99x100", "Ancho", 99, 100)], paper.DefaultId);
        Assert.NotEqual("", paper.ErrorOf("Sizes"));
    }

    // ---- saving one section at a time ------------------------------------------------------------------------------

    [Fact]
    public async Task Saving_a_section_changes_only_its_own_settings()
    {
        var (model, gateway, _) = Create();
        await model.LoadAsync();
        Section<PrintSectionViewModel>(model).Darkness = 5;
        Section<NetworkSectionViewModel>(model).Port = "9999"; // unsaved, in another section
        var outcome = await model.ApplyAsync(Section<PrintSectionViewModel>(model));

        Assert.True(outcome.Succeeded);
        var sent = Assert.Single(gateway.Saved);
        Assert.Equal(5, sent.Darkness);
        Assert.Equal(8631, sent.IppPort);
        Assert.Equal("AA", sent.Printer!.Address); // what the section does not own is kept
        Assert.False(Section<PrintSectionViewModel>(model).IsDirty);
        Assert.True(Section<NetworkSectionViewModel>(model).IsDirty);
        Assert.Equal("9999", Section<NetworkSectionViewModel>(model).Port);
    }

    [Fact]
    public async Task A_change_made_elsewhere_in_another_section_is_not_lost()
    {
        var (model, gateway, _) = Create();
        await model.LoadAsync();
        Section<PrintSectionViewModel>(model).Darkness = 5;
        gateway.Service = gateway.Service with { IppPort = 7777 }; // the CLI changed the port meanwhile
        var outcome = await model.ApplyAsync(Section<PrintSectionViewModel>(model));
        Assert.True(outcome.Succeeded);
        Assert.Equal(7777, gateway.Saved.Single().IppPort);
        Assert.Equal("7777", Section<NetworkSectionViewModel>(model).Port);
    }

    [Fact]
    public async Task A_change_made_elsewhere_in_the_same_section_is_reported_not_overwritten()
    {
        var (model, gateway, _) = Create();
        await model.LoadAsync();
        var print = Section<PrintSectionViewModel>(model);
        print.Darkness = 5;
        gateway.Service = gateway.Service with { Darkness = 1, ExtraFeedSteps = 3 };
        var outcome = await model.ApplyAsync(print);
        Assert.Equal(ApplyStatus.Conflict, outcome.Status);
        Assert.Empty(gateway.Saved);
        Assert.True(print.IsDirty);

        var forced = await model.ApplyAsync(print, overwrite: true);
        Assert.True(forced.Succeeded);
        Assert.Equal(5, gateway.Saved.Single().Darkness);
    }

    [Fact]
    public async Task With_a_conflict_the_user_chooses_between_overwriting_and_reloading()
    {
        var (model, gateway, interaction) = Create();
        await model.LoadAsync();
        var print = Section<PrintSectionViewModel>(model);
        print.Darkness = 5;
        gateway.Service = gateway.Service with { Darkness = 1 };
        interaction.OverwriteAnswer = false;
        await model.ApplyWithQuestionsAsync(print);
        Assert.Empty(gateway.Saved);
        Assert.Equal(1, print.Darkness); // reloaded what the service has
        Assert.False(print.IsDirty);

        print.Darkness = 5;
        gateway.Service = gateway.Service with { Darkness = 2 };
        interaction.OverwriteAnswer = true;
        Assert.True((await model.ApplyWithQuestionsAsync(print)).Succeeded);
        Assert.Equal(5, gateway.Service.Darkness);
    }

    [Fact]
    public async Task Recreating_the_windows_printer_is_requested_after_saving_and_a_skipped_recreation_is_flagged()
    {
        var (model, gateway, interaction) = Create();
        await model.LoadAsync();
        string? requested = "nada";
        model.RecreateRequested += previous =>
        {
            requested = previous;
            return Task.CompletedTask;
        };
        var paper = Section<PaperSectionViewModel>(model);
        paper.PrinterName = "Tickets";
        interaction.RecreateAnswer = RecreateDecision.Recreate;
        await model.ApplyWithQuestionsAsync(paper);
        Assert.Equal("X5h Thermal Printer", requested);
        Assert.Equal("", paper.QueueStatus);

        paper.PrinterName = "Otra";
        interaction.RecreateAnswer = RecreateDecision.SaveOnly;
        requested = "nada";
        await model.ApplyWithQuestionsAsync(paper);
        Assert.Equal("nada", requested);
        Assert.True(paper.QueueStatusIsProblem);
        Assert.NotEqual("", paper.QueueStatus);
    }

    [Fact]
    public async Task Applying_everything_saves_each_section_with_changes()
    {
        var (model, gateway, _) = Create();
        await model.LoadAsync();
        Section<PrintSectionViewModel>(model).Darkness = 4;
        Section<BatterySectionViewModel>(model).Unit = BatteryUnit.Percent;
        Assert.True(await model.ApplyAllAsync());
        Assert.Equal(2, gateway.Saved.Count);
        Assert.Equal(4, gateway.Service.Darkness);
        Assert.Equal(BatteryUnit.Percent, gateway.Service.BatteryUnit);
        Assert.False(model.HasPendingChanges);
    }

    [Fact]
    public async Task Failing_to_save_keeps_the_changes_and_says_why()
    {
        var (model, gateway, _) = Create();
        await model.LoadAsync();
        var failing = new ThrowingGateway(gateway);
        var broken = new SettingsViewModel(failing, new FakeInteraction(), [new PrintSectionViewModel()]);
        await broken.LoadAsync();
        var print = (PrintSectionViewModel)broken.Sections[0];
        print.Darkness = 5;
        var outcome = await broken.ApplyAsync(print);
        Assert.Equal(ApplyStatus.Failed, outcome.Status);
        Assert.Equal("sin servicio", outcome.Message);
        Assert.True(print.IsDirty);
    }

    private sealed class ThrowingGateway(FakeGateway inner) : ISettingsGateway
    {
        public Task<ServiceSettings> GetAsync() => inner.GetAsync();
        public Task<ServiceSettings> SaveAsync(ServiceSettings settings) => Task.FromException<ServiceSettings>(new InvalidOperationException("sin servicio"));
        public Task<AutomationInfo> GetAutomationAsync() => inner.GetAutomationAsync();
        public Task<AutomationInfo> RegenerateTokenAsync() => inner.RegenerateTokenAsync();
    }

    // ---- confirmations ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Opening_the_raw_port_to_the_network_asks_first_and_no_means_nothing_is_saved()
    {
        var (model, gateway, interaction) = Create();
        gateway.Service = gateway.Service with { NetworkMode = NetworkMode.Lan };
        await model.LoadAsync();
        var raw = Section<RawPortSectionViewModel>(model);
        raw.Enabled = true;
        interaction.RawNetworkAnswer = false;
        var outcome = await model.ApplyAsync(raw);
        Assert.Equal(ApplyStatus.Cancelled, outcome.Status);
        Assert.Equal(["raw:9100"], interaction.Asked);
        Assert.Empty(gateway.Saved);
        Assert.True(raw.IsDirty);

        interaction.RawNetworkAnswer = true;
        Assert.True((await model.ApplyAsync(raw)).Succeeded);
        Assert.True(gateway.Service.RawPortEnabled);
    }

    [Fact]
    public async Task The_raw_port_on_this_pc_only_needs_no_confirmation()
    {
        var (model, gateway, interaction) = Create();
        await model.LoadAsync();
        Section<RawPortSectionViewModel>(model).Enabled = true;
        Assert.True((await model.ApplyAsync(Section<RawPortSectionViewModel>(model))).Succeeded);
        Assert.Empty(interaction.Asked);
        Assert.True(gateway.Service.RawPortEnabled);
    }

    [Fact]
    public async Task Widening_the_network_with_the_raw_port_already_on_asks_too()
    {
        var (model, gateway, interaction) = Create();
        gateway.Service = gateway.Service with { RawPortEnabled = true };
        await model.LoadAsync();
        Section<NetworkSectionViewModel>(model).IsLan = true;
        interaction.RawNetworkAnswer = false;
        var outcome = await model.ApplyAsync(Section<NetworkSectionViewModel>(model));
        Assert.Equal(ApplyStatus.Cancelled, outcome.Status);
        Assert.Equal(NetworkMode.Local, gateway.Service.NetworkMode);
    }

    [Theory]
    [InlineData(RecreateDecision.Cancel, ApplyStatus.Cancelled, false, false)]
    [InlineData(RecreateDecision.SaveOnly, ApplyStatus.Saved, false, true)]
    [InlineData(RecreateDecision.Recreate, ApplyStatus.Saved, true, false)]
    public async Task A_new_name_asks_whether_to_recreate_the_windows_printer(RecreateDecision answer, ApplyStatus status, bool recreate, bool stale)
    {
        var (model, gateway, interaction) = Create();
        await model.LoadAsync();
        var paper = Section<PaperSectionViewModel>(model);
        paper.PrinterName = "Tickets";
        interaction.RecreateAnswer = answer;
        var outcome = await model.ApplyAsync(paper);
        Assert.Equal(status, outcome.Status);
        Assert.Equal(recreate, outcome.RecreateQueue);
        Assert.Equal(stale, outcome.QueueStale);
        Assert.Equal(status == ApplyStatus.Saved ? "Tickets" : "X5h Thermal Printer", gateway.Service.PrinterName);
        if (recreate)
            Assert.Equal("X5h Thermal Printer", outcome.PreviousName);
    }

    [Fact]
    public async Task Changing_the_sizes_asks_and_leaving_them_alone_does_not()
    {
        var (model, _, interaction) = Create();
        await model.LoadAsync();
        var paper = Section<PaperSectionViewModel>(model);
        var sizes = paper.Sizes.Select(s => s with { Enabled = s.Id == "48x210" || s.Enabled && s.Id != "48x100" }).ToList();
        paper.SetSizes(sizes, paper.DefaultId);
        Assert.True((await model.ApplyAsync(paper)).Succeeded);
        Assert.Equal(["recreate"], interaction.Asked);

        interaction.Asked.Clear();
        Section<PrintSectionViewModel>(model).Darkness = 2;
        Assert.True((await model.ApplyAsync(Section<PrintSectionViewModel>(model))).Succeeded);
        Assert.Empty(interaction.Asked);
    }

    [Fact]
    public async Task A_new_token_asks_first()
    {
        var (model, gateway, interaction) = Create();
        await model.LoadAsync();
        Assert.Equal("tok-1", model.Automation!.Token);
        interaction.TokenAnswer = false;
        await model.Automation.RegenerateToken!.ExecuteAsync();
        Assert.Equal(0, gateway.Regenerated);
        Assert.Equal("tok-1", model.Automation.Token);

        interaction.TokenAnswer = true;
        await model.Automation.RegenerateToken.ExecuteAsync();
        Assert.Equal("tok-2", model.Automation.Token);
    }

    // ---- sections and search ---------------------------------------------------------------------------------------

    [Fact]
    public void The_section_that_was_open_is_remembered()
    {
        Assert.Equal("Network", Create("Network").Model.Selected.Key);
        Assert.Equal("Print", Create("Nada").Model.Selected.Key);
        var (model, _, _) = Create();
        string? remembered = null;
        model.SelectedChanged += key => remembered = key;
        model.Selected = Section<BatterySectionViewModel>(model);
        Assert.Equal("Battery", remembered);
    }

    [Theory]
    [InlineData("bateria", "Battery")]
    [InlineData("BATERÍA", "Battery")]
    [InlineData("9100", "Raw")]
    [InlineData("token", "Automation")]
    [InlineData("tamaños", "Paper")]
    [InlineData("puerto ipp", "Network")]
    [InlineData("historial", "Print")]
    public async Task The_search_finds_the_sections_by_their_settings_and_their_synonyms(string query, string expected)
    {
        var (model, _, _) = Create();
        await model.LoadAsync();
        model.Search = query;
        Assert.Contains(expected, model.VisibleSections.Select(s => s.Key));
        Assert.True(model.VisibleSections.Count < model.Sections.Count);
    }

    [Fact]
    public async Task The_search_highlights_the_matching_settings_and_follows_the_selection()
    {
        var (model, _, _) = Create();
        await model.LoadAsync();
        model.Selected = Section<PaperSectionViewModel>(model);
        model.Search = "latencia que no existe";
        Assert.Empty(model.VisibleSections);

        model.Search = "voltaje";
        Assert.Equal("Battery", model.Selected.Key);
        Assert.True(Section<BatterySectionViewModel>(model).Highlights("Unit"));
        Assert.False(Section<BatterySectionViewModel>(model).Highlights("Low"));

        model.Search = "";
        Assert.Equal(model.Sections.Count, model.VisibleSections.Count);
        Assert.False(Section<BatterySectionViewModel>(model).Highlights("Unit"));
    }

    [Fact]
    public void Every_editable_setting_belongs_to_exactly_one_section()
    {
        var (model, _, _) = Create();
        var covered = model.Sections.SelectMany(s => s.Covers).ToList();
        Assert.Equal(covered.Count, covered.Distinct().Count());
        // These are not edited here: the printer is chosen in Search; the uuid is managed by the service; the padding is internal.
        string[] elsewhere = [nameof(ServiceSettings.Printer), nameof(ServiceSettings.PrinterUuid), nameof(ServiceSettings.FeedPadding)];
        var all = typeof(ServiceSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)
            .Where(n => !elsewhere.Contains(n)).ToList();
        Assert.Empty(all.Except(covered));
        Assert.Empty(covered.Except(all));
    }

    [Fact]
    public void Every_section_and_setting_has_its_text_and_search_words()
    {
        var (model, _, _) = Create();
        foreach (var section in model.Sections)
        {
            Assert.True(Strings.Exists(section.TitleKey), section.TitleKey);
            foreach (var field in section.Fields)
            {
                Assert.True(Strings.Exists(field.LabelKey), field.LabelKey);
                Assert.True(Strings.Exists(field.TermsKey), field.TermsKey);
            }
        }
    }

    [Fact]
    public void Appearance_and_general_apply_at_once()
    {
        ThemeChoice theme = ThemeChoice.Auto;
        TextSizeChoice size = TextSizeChoice.Normal;
        var appearance = new AppearanceSectionViewModel(theme, size, t => theme = t, s => size = s);
        appearance.Theme = ThemeChoice.Dark;
        appearance.TextSize = TextSizeChoice.Large;
        Assert.Equal(ThemeChoice.Dark, theme);
        Assert.Equal(TextSizeChoice.Large, size);
        Assert.True(appearance.IsInstant);
        Assert.False(appearance.IsDirty);

        var auto = true;
        var general = new GeneralSectionViewModel(true, "Ctrl+Alt+P", v => auto = v, key => "activo " + key, () => Task.CompletedTask);
        general.AutoUpdate = false;
        Assert.False(auto);
        general.Hotkey = "Ctrl+Alt+Q";
        general.ApplyHotkey.Execute(null);
        Assert.Equal("activo Ctrl+Alt+Q", general.HotkeyStatus);
    }

    [Fact]
    public void Describing_the_raw_port_status()
    {
        var raw = new RawPortSectionViewModel();
        raw.ShowStatus(new RawPortDto(false, 9100, false, null, []));
        Assert.Equal(RawPortState.Off, raw.State);
        raw.ShowStatus(new RawPortDto(true, 9100, true, null, ["192.168.1.5:9100"]));
        Assert.Equal(RawPortState.Listening, raw.State);
        Assert.Contains("192.168.1.5:9100", raw.Status);
        raw.ShowStatus(new RawPortDto(true, 9100, false, "El puerto está en uso", []));
        Assert.Equal(RawPortState.Failed, raw.State);
        Assert.Equal("El puerto está en uso", raw.Status);
        raw.ShowStatus(null);
        Assert.Equal(RawPortState.Unknown, raw.State);
    }
}
