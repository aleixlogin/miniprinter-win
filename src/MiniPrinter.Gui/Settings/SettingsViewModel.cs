using MiniPrinter.Control;

namespace MiniPrinter.Gui;

/// <summary>What the Settings window needs from the service.</summary>
public interface ISettingsGateway
{
    Task<ServiceSettings> GetAsync();

    Task<ServiceSettings> SaveAsync(ServiceSettings settings);

    Task<AutomationInfo> GetAutomationAsync();

    Task<AutomationInfo> RegenerateTokenAsync();
}

/// <summary>The questions the Settings window may ask before saving (the window shows them as dialogs).</summary>
public interface ISettingsInteraction
{
    /// <summary>Opening the RAW port to the local network lets any machine on it print without authenticating.</summary>
    Task<bool> ConfirmRawNetworkAsync(int port);

    /// <summary>The sizes or the name changed: Windows only sees them when the printer is recreated.</summary>
    Task<RecreateDecision> AskRecreateAsync();

    /// <summary>A new token stops the scripts that use the old one.</summary>
    Task<bool> ConfirmNewTokenAsync();

    /// <summary>The service changed the same settings while they were being edited: overwrite them (yes) or reload (no).</summary>
    Task<bool> ConfirmOverwriteAsync();
}

public enum RecreateDecision
{
    Cancel,
    SaveOnly,
    Recreate,
}

public enum ApplyStatus
{
    Saved,

    /// <summary>The section has errors: nothing was sent.</summary>
    Invalid,

    /// <summary>The user answered no to a question.</summary>
    Cancelled,

    /// <summary>The service changed these settings while they were being edited.</summary>
    Conflict,

    Failed,
}

/// <param name="RecreateQueue">The user chose to recreate the Windows printer: the window does it after saving.</param>
/// <param name="PreviousName">The name the printer had, when it was renamed.</param>
/// <param name="QueueStale">The sizes or the name were saved but the Windows printer was not recreated, so it still shows the old ones.</param>
public sealed record ApplyOutcome(ApplyStatus Status, string Message, bool RecreateQueue = false, string? PreviousName = null, bool QueueStale = false)
{
    public bool Succeeded => Status == ApplyStatus.Saved;
}

/// <summary>
/// The Settings window: its sections, the one shown, the search and the saving of one section at a time.
/// Saving reads the current settings from the service, replaces only the fields of that section and sends them back, so
/// another section with unsaved changes, the command line or the API never lose theirs.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsGateway _gateway;
    private readonly ISettingsInteraction _interaction;
    private SettingsSectionViewModel _selected;
    private string _search = "";
    private string _status = "";

    public SettingsViewModel(ISettingsGateway gateway, ISettingsInteraction interaction,
        IReadOnlyList<SettingsSectionViewModel> sections, string? selectedKey = null)
    {
        _gateway = gateway;
        _interaction = interaction;
        Sections = sections;
        _selected = sections.FirstOrDefault(s => s.Key == selectedKey) ?? sections[0];
        Automation = sections.OfType<AutomationSectionViewModel>().FirstOrDefault();
        Raw = sections.OfType<RawPortSectionViewModel>().FirstOrDefault() ?? new RawPortSectionViewModel();
        Paper = sections.OfType<PaperSectionViewModel>().FirstOrDefault() ?? new PaperSectionViewModel();
        if (Automation is not null)
        {
            Automation.RegenerateToken = new AsyncCommand(RegenerateTokenAsync);
            Automation.RegenerateToken.Failed += ex => Status = ex.Message;
        }
        foreach (var section in sections)
            section.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is "IsDirty" or "")
                {
                    OnPropertyChanged(nameof(HasPendingChanges));
                    // The button is bound to the command: WPF only asks again when told, so a change (or a mistake fixed) must tell it.
                    Apply.RaiseCanExecuteChanged();
                }
            };
        Apply = new AsyncCommand(() => ApplyWithQuestionsAsync(_selected), () => _selected is { IsInstant: false, IsDirty: true, HasErrors: false });
        Apply.Failed += ex => Status = ex.Message;
        Discard = new RelayCommand(() =>
        {
            _selected.Discard();
            Status = "";
            Apply.RaiseCanExecuteChanged();
        });
    }

    public IReadOnlyList<SettingsSectionViewModel> Sections { get; }

    public AutomationSectionViewModel? Automation { get; }

    /// <summary>The direct printing and paper sections (the window shows the port status and the state of the Windows printer there).</summary>
    public RawPortSectionViewModel Raw { get; }

    public PaperSectionViewModel Paper { get; }

    /// <summary>Saves the section being shown.</summary>
    public AsyncCommand Apply { get; }

    /// <summary>Drops the unsaved changes of the section being shown.</summary>
    public RelayCommand Discard { get; }

    /// <summary>Raised when the user chose to recreate the Windows printer: the window does it (with the name it had before, if renamed).</summary>
    public event Func<string?, Task>? RecreateRequested;

    public SettingsSectionViewModel Selected
    {
        get => _selected;
        set
        {
            if (value is null || !Set(ref _selected, value))
                return;
            Apply.RaiseCanExecuteChanged();
            SelectedChanged?.Invoke(value.Key);
        }
    }

    /// <summary>Raised with the key of the section when the user picks another one (to remember it).</summary>
    public event Action<string>? SelectedChanged;

    public string Search
    {
        get => _search;
        set
        {
            if (!Set(ref _search, value))
                return;
            foreach (var section in Sections)
                section.Query = value.Trim();
            OnPropertyChanged(nameof(VisibleSections));
            if (VisibleSections.Count > 0 && !VisibleSections.Contains(_selected))
                Selected = VisibleSections[0];
        }
    }

    /// <summary>The sections that match the search (all of them when it is empty).</summary>
    public IReadOnlyList<SettingsSectionViewModel> VisibleSections =>
        _search.Trim().Length == 0 ? Sections : [.. Sections.Where(s => s.MatchesSearch(_search.Trim()))];

    public bool HasPendingChanges => Sections.Any(s => s.IsDirty);

    public IReadOnlyList<SettingsSectionViewModel> DirtySections => [.. Sections.Where(s => s.IsDirty)];

    /// <summary>What the last action said (saved, a problem…).</summary>
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>
    /// Reads the settings from the service. Sections with unsaved changes keep what the user typed unless
    /// <paramref name="discardChanges"/> is set.
    /// </summary>
    public async Task LoadAsync(bool discardChanges = false)
    {
        ServiceSettings settings;
        try
        {
            settings = await _gateway.GetAsync();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            return;
        }
        foreach (var section in Sections.Where(s => discardChanges || !s.IsDirty || s.Saved is null))
            section.Load(settings);
        await LoadAutomationAsync();
        Status = "";
        Apply.RaiseCanExecuteChanged();
    }

    private async Task LoadAutomationAsync()
    {
        if (Automation is null)
            return;
        try
        {
            Automation.Show(await _gateway.GetAutomationAsync());
        }
        catch (Exception ex)
        {
            Automation.ShowProblem(ex.Message);
        }
    }

    private async Task RegenerateTokenAsync()
    {
        if (Automation is null || !await _interaction.ConfirmNewTokenAsync())
            return;
        Automation.Show(await _gateway.RegenerateTokenAsync());
    }

    /// <summary>Goes back to the values last loaded in every section.</summary>
    public void DiscardAll()
    {
        foreach (var section in Sections)
            section.Discard();
        Apply.RaiseCanExecuteChanged();
    }

    /// <summary>Saves every section with unsaved changes; stops at the first one that does not save. True when all were saved.</summary>
    public async Task<bool> ApplyAllAsync()
    {
        foreach (var section in DirtySections)
        {
            if (!(await ApplyWithQuestionsAsync(section)).Succeeded)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Saves a section and deals with what the outcome asks for: a conflict (overwrite or reload), recreating the Windows
    /// printer and the notice that it is out of date when it was not recreated.
    /// </summary>
    public async Task<ApplyOutcome> ApplyWithQuestionsAsync(SettingsSectionViewModel section)
    {
        var outcome = await ApplyAsync(section);
        if (outcome.Status == ApplyStatus.Conflict)
        {
            if (!await _interaction.ConfirmOverwriteAsync())
            {
                if (await SafeGetAsync() is { } latest)
                    section.Load(latest);
                Status = "";
                Apply.RaiseCanExecuteChanged();
                return new ApplyOutcome(ApplyStatus.Cancelled, "");
            }
            outcome = await ApplyAsync(section, overwrite: true);
        }
        if (!outcome.Succeeded)
            return outcome;
        if (outcome.RecreateQueue && RecreateRequested is { } recreate)
            await recreate(outcome.PreviousName);
        else if (outcome.QueueStale && Sections.OfType<PaperSectionViewModel>().FirstOrDefault() is { } paper)
        {
            paper.QueueStatus = Strings.Get("Main.QueueStale");
            paper.QueueStatusIsProblem = true;
        }
        return outcome;
    }

    private async Task<ServiceSettings?> SafeGetAsync()
    {
        try
        {
            return await _gateway.GetAsync();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Saves one section (see the class summary). With <paramref name="overwrite"/> a conflict is ignored.</summary>
    public async Task<ApplyOutcome> ApplyAsync(SettingsSectionViewModel section, bool overwrite = false)
    {
        var outcome = await ApplyCoreAsync(section, overwrite);
        Status = outcome.Message;
        Apply.RaiseCanExecuteChanged();
        return outcome;
    }

    private async Task<ApplyOutcome> ApplyCoreAsync(SettingsSectionViewModel section, bool overwrite)
    {
        if (section.HasErrors)
            return new ApplyOutcome(ApplyStatus.Invalid, Strings.Get("Settings.CheckErrors"));

        ServiceSettings current;
        try
        {
            current = await _gateway.GetAsync();
        }
        catch (Exception ex)
        {
            return new ApplyOutcome(ApplyStatus.Failed, ex.Message);
        }

        if (!overwrite && section.Saved is { } loaded && section.DiffersBetween(loaded, current))
            return new ApplyOutcome(ApplyStatus.Conflict, Strings.Get("Settings.Conflict"));

        var updated = section.Write(current);

        // Opening the RAW port to the network lets any machine on the private network print without authenticating.
        var exposed = updated.RawPortEnabled && updated.NetworkMode == NetworkMode.Lan;
        var wasExposed = current.RawPortEnabled && current.NetworkMode == NetworkMode.Lan;
        if (exposed && !wasExposed && !await _interaction.ConfirmRawNetworkAsync(updated.RawPort))
            return new ApplyOutcome(ApplyStatus.Cancelled, Strings.Get("Main.UnsavedChanges"));

        // A change of sizes or of the printer name needs the Windows printer to be recreated to be seen.
        var nameChanged = !string.Equals(current.PrinterName.Trim(), updated.PrinterName.Trim(), StringComparison.Ordinal);
        var recreate = false;
        var queueChanged = !PaperCatalog.SameEffective(current, updated) || nameChanged;
        if (queueChanged)
        {
            var decision = await _interaction.AskRecreateAsync();
            if (decision == RecreateDecision.Cancel)
                return new ApplyOutcome(ApplyStatus.Cancelled, Strings.Get("Main.UnsavedChanges"));
            recreate = decision == RecreateDecision.Recreate;
        }

        ServiceSettings saved;
        try
        {
            saved = await _gateway.SaveAsync(updated);
        }
        catch (Exception ex)
        {
            return new ApplyOutcome(ApplyStatus.Failed, ex.Message);
        }

        // The saved settings are what the service has now: sections without changes follow them, this one takes them as its state.
        foreach (var other in Sections.Where(s => s == section || !s.IsDirty))
            other.Load(saved);
        await LoadAutomationAsync();
        var message = saved.NetworkMode != NetworkMode.Local ? Strings.Get("Main.SavedNetwork") : Strings.Get("Main.Saved");
        return new ApplyOutcome(ApplyStatus.Saved, message, recreate, nameChanged ? current.PrinterName : null, QueueStale: queueChanged && !recreate);
    }
}
