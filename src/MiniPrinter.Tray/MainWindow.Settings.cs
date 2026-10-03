using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using MiniPrinter.Control;
using MiniPrinter.Gui;

namespace MiniPrinter.Tray;

/// <summary>The Settings tab: the view model and what it needs from the window (dialogs, the clipboard, the Windows printer).</summary>
public partial class MainWindow
{
    private SettingsViewModel _settings = null!;

    /// <summary>The view model of the Settings tab (UiShots fills it to photograph the panel).</summary>
    public SettingsViewModel Settings => _settings;
    private bool _closingConfirmed;

    /// <summary>True while the unsaved settings are being applied before the window closes.</summary>
    public bool ClosePending { get; private set; }

    private void InitSettings()
    {
        var app = (App)Application.Current;
        var paper = new PaperSectionViewModel();
        var automation = new AutomationSectionViewModel();
        List<SettingsSectionViewModel> sections =
        [
            new PrintSectionViewModel(),
            paper,
            new NetworkSectionViewModel(),
            new RawPortSectionViewModel(),
            automation,
            new BatterySectionViewModel(),
            new AppearanceSectionViewModel(app.Preferences.Theme, app.Preferences.TextSize, app.SetTheme, app.SetTextSize),
            new GeneralSectionViewModel(app.Updater?.State.AutoCheck ?? true, app.Preferences.QuickNoteHotkey,
                on => app.Updater?.SetAutoCheck(on), ApplyHotkey, CheckForUpdatesAsync),
        ];
        _settings = new SettingsViewModel(new ServiceSettingsGateway(_service), new SettingsInteraction(this), sections, app.Preferences.SettingsSection);
        _settings.SelectedChanged += key => app.UpdatePreferences(p => p with { SettingsSection = key });
        _settings.RecreateRequested += RecreateQueueAsync;

        paper.RecreateQueue = new AsyncCommand(() => RecreateQueueAsync(null));
        paper.RecreateQueue.Failed += ex => MessageBox.Show(this, ex.Message, "MiniPrinter", MessageBoxButton.OK, MessageBoxImage.Warning);
        automation.CopyToken = new RelayCommand(() =>
        {
            if (!string.IsNullOrEmpty(automation.Token))
                Clipboard.SetText(automation.Token);
        });

        _settings.Raw.Tools.Gateway = new ServiceRawPortGateway(_service);
        _settings.Raw.Tools.CopyRequested += text =>
        {
            Clipboard.SetText(text);
            _settings.Raw.Tools.NotifyCopied(text);
        };
        _settings.Raw.Tools.QrRequested += text => new QrWindow(this, text).ShowDialog();
        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.Selected) && _settings.Selected == _settings.Raw)
                _ = _settings.Raw.Tools.RefreshClientsAsync();
        };

        SettingsRoot.DataContext = _settings;
        Loaded += async (_, _) =>
        {
            await _settings.LoadAsync();
            await RefreshQueueStatusAsync();
        };
        Closing += OnClosingWithPendingChanges;
    }

    /// <summary>Applies the hotkey of the quick note and says whether it is active.</summary>
    private string ApplyHotkey(string hotkey)
    {
        if (!GlobalHotkey.TryParse(hotkey, out _, out _))
            return Strings.Get("Main.HotkeyInvalid");
        return ((App)Application.Current).ApplyHotkey(hotkey, notifyOnFailure: false)
            ? Strings.Get("Main.HotkeyActive", hotkey)
            : Strings.Get("Main.HotkeyTaken", hotkey);
    }

    private async Task CheckForUpdatesAsync()
    {
        if (((App)Application.Current).Updater is { } updater)
            await updater.CheckAsync(manual: true);
    }

    /// <summary>Asks what to do with the settings that were not saved when the panel is closed.</summary>
    private async void OnClosingWithPendingChanges(object? sender, CancelEventArgs e)
    {
        if (_closingConfirmed || !_settings.HasPendingChanges)
            return;
        var names = string.Join(", ", _settings.DirtySections.Select(s => s.Title));
        var answer = MessageBox.Show(this, Strings.Get("Settings.ClosePrompt", names), "MiniPrinter", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            return;
        }
        if (answer == MessageBoxResult.No)
        {
            _settings.DiscardAll();
            return;
        }
        e.Cancel = true;
        ClosePending = true;
        if (await _settings.ApplyAllAsync())
        {
            _closingConfirmed = true;
            Close();
        }
        ClosePending = false;
    }

    // ---- The Windows printer ---------------------------------------------------------------------------------------

    /// <summary>Whether the printer exists in Windows; suggests recreating it when it does not.</summary>
    private async Task RefreshQueueStatusAsync()
    {
        var paper = _settings.Paper;
        try
        {
            var queue = await _service.Client.GetWindowsQueueAsync();
            paper.QueueStatusIsProblem = false;
            paper.QueueStatus = queue.Exists || queue.State == "Running" ? "" : Strings.Get("Main.QueueMissing", queue.Name);
        }
        catch (Exception)
        {
            paper.QueueStatus = "";
        }
    }

    /// <summary>
    /// Recreates the Windows print queue through the service and follows its progress. The tray (running as the user)
    /// remembers whether the queue was this user's default printer and restores that afterwards.
    /// </summary>
    private async Task RecreateQueueAsync(string? previousName)
    {
        var paper = _settings.Paper;
        var queueName = paper.Saved?.PrinterName ?? "X5h Thermal Printer";
        var wasDefault = PaperSettingsPanel.IsDefaultPrinter(previousName ?? queueName);

        void Say(string text, bool problem = false)
        {
            paper.QueueStatusIsProblem = problem;
            paper.QueueStatus = text;
        }

        Say(Strings.Get("Main.QueueRecreating"));
        try
        {
            await _service.Client.RecreateWindowsQueueAsync(previousName);
            var deadline = DateTime.UtcNow.AddMinutes(3);
            WindowsQueueDto state;
            while (true)
            {
                await Task.Delay(700);
                state = await _service.Client.GetWindowsQueueAsync();
                if (state.State is "Succeeded" or "Failed")
                    break;
                if (DateTime.UtcNow > deadline)
                {
                    Say(Strings.Get("Main.QueueTooSlow"), problem: true);
                    return;
                }
                Say(state.Message ?? Strings.Get("Main.QueueRecreating"));
            }
            string? doneMessage = state.State == "Succeeded" ? state.Message : null;
            if (state.State == "Failed" && state.NeedsElevation)
            {
                // The service has no rights to manage printers: repeat the work in an elevated helper (Windows asks for permission).
                Say(Strings.Get("Main.QueueNeedsElevation"));
                var outcome = await RunElevatedAsync(previousName);
                state = new WindowsQueueDto(outcome.Succeeded, state.Name, outcome.Succeeded ? "Succeeded" : "Failed", outcome.Message, outcome.Step);
                doneMessage = outcome.Succeeded ? outcome.Message : null;
            }
            if (state.State == "Succeeded")
            {
                var restored = wasDefault && PaperSettingsPanel.MakeDefaultPrinter(state.Name);
                Say((doneMessage ?? Strings.Get("Main.QueueUpdated")) + (restored ? " " + Strings.Get("Main.QueueStillDefault") : ""));
            }
            else
            {
                Say(state.Message ?? Strings.Get("Main.QueueRecreateFailed"), problem: true);
            }
        }
        catch (Exception ex) when (ex is ControlApiException or HttpRequestException or InvalidOperationException)
        {
            Say(ex.Message, problem: true);
        }
    }

    /// <summary>
    /// Runs <c>miniprinter queue-recreate</c> elevated: Windows shows the UAC prompt. The helper writes its outcome to a file;
    /// declining the prompt is reported as a failure that leaves the printer as it was.
    /// </summary>
    private static async Task<QueueOutcome> RunElevatedAsync(string? previousName)
    {
        var cli = System.IO.Path.Combine(AppContext.BaseDirectory, "miniprinter.exe");
        if (!System.IO.File.Exists(cli))
            return new QueueOutcome(false, Strings.Get("Main.CliMissing"), "elevation");
        var result = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"miniprinter-queue-{Guid.NewGuid():N}.json");
        var args = $"queue-recreate --result \"{result}\"" + (string.IsNullOrWhiteSpace(previousName) ? "" : $" --old \"{previousName}\"");
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(cli, args)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            });
            if (process is null)
                return new QueueOutcome(false, Strings.Get("Main.ElevationStartFailed"), "elevation");
            await process.WaitForExitAsync();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new QueueOutcome(false, Strings.Get("Main.ElevationCanceled"), "elevation");
        }
        catch (Win32Exception ex)
        {
            return new QueueOutcome(false, Strings.Get("Main.ElevationFailed", ex.Message), "elevation");
        }

        try
        {
            if (System.IO.File.Exists(result)
                && System.Text.Json.JsonSerializer.Deserialize<QueueOutcome>(System.IO.File.ReadAllText(result), ControlDefaults.Json) is { } outcome)
                return outcome;
            return new QueueOutcome(false, Strings.Get("Main.ElevationNoResult"), "elevation");
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Text.Json.JsonException)
        {
            return new QueueOutcome(false, Strings.Get("Main.ElevationReadFailed", ex.Message), "elevation");
        }
        finally
        {
            try { System.IO.File.Delete(result); } catch (System.IO.IOException) { }
        }
    }
}

/// <summary>The tools of the RAW port (test ticket and recent clients), over the service.</summary>
internal sealed class ServiceRawPortGateway(ServiceConnection service) : IRawPortGateway
{
    public Task<JobDto> SendTestAsync() => service.Client.SendRawTestAsync();

    public Task<IReadOnlyList<RawClientDto>> GetClientsAsync() => service.Client.GetRawClientsAsync();
}

/// <summary>The settings of the service, as the Settings tab asks for them.</summary>
internal sealed class ServiceSettingsGateway(ServiceConnection service) : ISettingsGateway
{
    public Task<ServiceSettings> GetAsync() => service.Client.GetSettingsAsync();

    public Task<ServiceSettings> SaveAsync(ServiceSettings settings) => service.Client.SaveSettingsAsync(settings);

    public Task<AutomationInfo> GetAutomationAsync() => service.Client.GetAutomationAsync();

    public Task<AutomationInfo> RegenerateTokenAsync() => service.Client.RegenerateAutomationTokenAsync();
}

/// <summary>The questions of the Settings tab, as dialogs over the panel.</summary>
internal sealed class SettingsInteraction(Window owner) : ISettingsInteraction
{
    public Task<bool> ConfirmRawNetworkAsync(int port) => Task.FromResult(MessageBox.Show(owner,
        Strings.Get("Main.RawLanConfirm", port), "MiniPrinter", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);

    public Task<RecreateDecision> AskRecreateAsync()
    {
        var dialog = new RecreateQueueDialog(owner, offerSaveOnly: true);
        dialog.ShowDialog();
        return Task.FromResult(dialog.Choice switch
        {
            RecreateChoice.Recreate => RecreateDecision.Recreate,
            RecreateChoice.SaveOnly => RecreateDecision.SaveOnly,
            _ => RecreateDecision.Cancel,
        });
    }

    public Task<bool> ConfirmNewTokenAsync() => Task.FromResult(MessageBox.Show(owner,
        Strings.Get("Main.RegenerateTokenConfirm"), "MiniPrinter", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);

    public Task<bool> ConfirmOverwriteAsync() => Task.FromResult(MessageBox.Show(owner,
        Strings.Get("Settings.ConflictPrompt"), "MiniPrinter", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);
}
