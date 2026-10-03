using System.IO;
using System.Windows;
using MiniPrinter.Control;
using MiniPrinter.Gui;

namespace MiniPrinter.Tray;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private ServiceConnection? _service;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private bool _exitRequested;
    private StatusDto? _lastStatus;
    private const string OpenPanelEventName = @"Local\MiniPrinter.Tray.OpenPanel";
    private const string ExitEventName = @"Local\MiniPrinter.Tray.Exit";
    private EventWaitHandle? _exitSignal;
    private EventWaitHandle? _openPanelSignal;
    private bool _wizardShown;
    private GlobalHotkey? _hotkey;
    private Updater? _updater;

    public Updater? Updater => _updater;

    /// <summary>The palette service (light, dark, high contrast); null before the tray starts.</summary>
    public ThemeService? Theme { get; private set; }
    private QuickNoteWindow? _quickNote;
    private TrayPreferences _preferences = TrayPreferences.Load();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // tools/UiShots builds the panel on its own, without the tray, the single-instance mutex or the service.
        if (Environment.GetEnvironmentVariable("MINIPRINTER_UISHOTS") == "1")
            return;
        // "Send to > MiniPrinter": print the files and exit without starting the tray.
        if (e.Args.Length > 0 && e.Args[0] == "--print")
        {
            await PrintFromCommandLineAsync(e.Args.Skip(1).ToArray());
            Shutdown();
            return;
        }

        // Installer/uninstaller: ask the running tray to close itself (removes its icon cleanly).
        if (e.Args.Contains("--exit"))
        {
            if (EventWaitHandle.TryOpenExisting(ExitEventName, out var exit))
                exit.Set();
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\MiniPrinter.Tray", out var created);
        if (!created)
        {
            // Start menu shortcut while the tray is already running: ask it to show its panel.
            if (e.Args.Contains("--open") && EventWaitHandle.TryOpenExisting(OpenPanelEventName, out var signal))
                signal.Set();
            Shutdown();
            return;
        }
        _openPanelSignal = new EventWaitHandle(false, EventResetMode.AutoReset, OpenPanelEventName);
        _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        new Thread(() =>
        {
            if (_exitSignal.WaitOne())
                Dispatcher.BeginInvoke(ExitApp);
        }) { IsBackground = true, Name = "ExitSignal" }.Start();
        new Thread(() =>
        {
            while (_openPanelSignal.WaitOne())
                Dispatcher.BeginInvoke(OpenPanel);
        }) { IsBackground = true, Name = "OpenPanelSignal" }.Start();

        ApplyTextSize(_preferences.TextSize);
        Theme = new ThemeService(this, _preferences.Theme);
        _service = new ServiceConnection();
        _tray = new TrayIcon(OpenPanel, ToggleConnection, () => RunAction(c => c.TestPrintAsync(), Strings.Get("App.TestPrintSent")),
            () => RunAction(c => c.FeedAsync(), null), ExitApp, PrintClipboard, OpenQuickNote, ToggleKeepAlive,
            () => _ = _updater?.CheckAsync(manual: true),
            new TrayIcon.TemplateMenuSource(
                () => TrayTemplatesMenu.Favorites(TemplateFavorites.Read(TemplatesPanel.FavoritesPath), TemplatesForMenu()),
                () => TrayTemplatesMenu.Recent(_preferences.RecentTemplates, TemplatesForMenu()),
                PrintFavorite, OpenTemplate));
        _tray.ApplyTheme(Theme.Current);
        Theme.Changed += theme => _tray?.ApplyTheme(theme);
        _service.Changed += () => Dispatcher.BeginInvoke(OnServiceChanged);
        _service.Start();

        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += OpenQuickNote;
        ApplyHotkey(_preferences.QuickNoteHotkey, notifyOnFailure: true);
        QuickPrint.EnsureSendToShortcut(Environment.ProcessPath ?? "");
        _updater = new Updater(_service, (title, message, warning, onClick) => _tray!.Notify(title, message, warning, onClick));

        if (e.Args.Contains("--open"))
            OpenPanel();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Theme?.Dispose();
        _hotkey?.Dispose();
        _updater?.Dispose();
        _tray?.Dispose();
        _service?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnServiceChanged()
    {
        var status = _service!.Status;
        var available = _service.ServiceAvailable;
        var state = TrayIcon.StateOf(status, available);
        _tray!.Update(state, Describe(status, available, _service.ServiceError), status?.Link == "Connected");
        _tray.SetKeepAlive(status?.KeepAlive == true);
        _window?.ShowStatus(status, available, _service.ServiceError);

        if (available && status is not null)
        {
            NotifyChanges(_lastStatus, status);
            _lastStatus = status;
            // First run (task 8.6): no printer selected yet → open the guided search once.
            if (status.Printer is null && !_wizardShown)
            {
                _wizardShown = true;
                OpenPanel();
                _window!.StartWizard();
            }
        }
    }

    private void NotifyChanges(StatusDto? previous, StatusDto current)
    {
        var newAlarms = current.Alarms.Except(previous?.Alarms ?? []).ToList();
        if (newAlarms.Count > 0)
            _tray!.Notify(Strings.Get("App.NeedsAttention"), string.Join(", ", newAlarms.Select(ServiceTexts.AlarmNotice)), warning: true);
        if (current.LowBattery && previous?.LowBattery != true)
            _tray!.Notify(Strings.Get("App.LowBatteryTitle"), Strings.Get("App.LowBatteryText", current.BatteryPercent), warning: true);
        if (current.LastErrorKind == "Busy" && previous?.LastErrorKind != "Busy")
            _tray!.Notify(Strings.Get("App.BusyTitle"), Strings.Get("App.BusyText"), warning: true);
    }

    /// <summary>The one-line state shown as the tooltip of the tray icon.</summary>
    public static string Describe(StatusDto? status, bool available, string? error)
    {
        if (!available || status is null)
            return error ?? Strings.Get("App.ServiceUnavailable");
        if (status.Printer is null)
            return Strings.Get("App.NoPrinter");
        var name = status.Printer.Name;
        if (status.Alarms.Count > 0)
            return $"{name}: {string.Join(", ", status.Alarms.Select(ServiceTexts.AlarmNotice))}";
        if (status.Printing)
            return Strings.Get("App.TipPrinting", name);
        if (status.Reconnecting)
            return Strings.Get("App.TipReconnecting", name);
        return status.Link switch
        {
            "Connected" => status.KeepAlive ? Strings.Get("App.TipReadyKeepAlive", name) : Strings.Get("App.TipReady", name),
            "Connecting" => Strings.Get("App.TipConnecting", name),
            "Error" => $"{name}: {status.LastError}",
            _ => Strings.Get("App.TipDisconnected", name),
        };
    }

    public void OpenPanel()
    {
        if (_window is null)
        {
            _window = new MainWindow(_service!);
            _window.Closed += (_, _) =>
            {
                _window = null;
                if (_exitRequested)
                    Shutdown();
            };
            _window.ShowStatus(_service!.Status, _service.ServiceAvailable, _service.ServiceError);
        }
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public TrayPreferences Preferences => _preferences;

    /// <summary>Changes a preference of the user and saves them (a failure to write is not worth stopping for).</summary>
    public void UpdatePreferences(Func<TrayPreferences, TrayPreferences> change)
    {
        _preferences = change(_preferences);
        try
        {
            _preferences.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the preferences stay in memory for this session
        }
    }

    /// <summary>Applies and remembers the theme chosen in Settings, Appearance.</summary>
    public void SetTheme(ThemeChoice choice)
    {
        Theme?.SetChoice(choice);
        UpdatePreferences(p => p with { Theme = choice });
    }

    /// <summary>Applies and remembers the text size chosen in Settings, Appearance.</summary>
    public void SetTextSize(TextSizeChoice size)
    {
        ApplyTextSize(size);
        UpdatePreferences(p => p with { TextSize = size });
    }

    private void ApplyTextSize(TextSizeChoice size)
    {
        Resources["Ui.FontSize"] = TextSizes.FontSize(size);
        _window?.ApplyMinimumScale(TextSizes.MinimumScale(size));
    }

    /// <summary>Registers the quick-note hotkey; returns false (and optionally notifies) if it is invalid or taken.</summary>
    public bool ApplyHotkey(string hotkey, bool notifyOnFailure)
    {
        if (_hotkey is null)
            return false;
        var ok = _hotkey.Register(hotkey);
        _tray?.SetQuickNoteHotkey(ok ? hotkey : null);
        if (ok)
        {
            _preferences = _preferences with { QuickNoteHotkey = hotkey };
            _preferences.Save();
        }
        else if (notifyOnFailure)
        {
            _tray?.Notify(Strings.Get("App.HotkeyUnavailableTitle"),
                Strings.Get("App.HotkeyUnavailableText", hotkey), warning: true);
        }
        return ok;
    }

    private void OpenQuickNote()
    {
        if (_quickNote is not null)
        {
            _quickNote.Activate();
            return;
        }
        _quickNote = new QuickNoteWindow(_service!, _preferences.QuickNoteSizePt);
        _quickNote.Closed += (_, _) =>
        {
            _preferences = _preferences with { QuickNoteSizePt = _quickNote.SizePt };
            _preferences.Save();
            _quickNote = null;
        };
        _quickNote.Show();
        _quickNote.Activate();
    }

    private async void PrintClipboard()
    {
        try
        {
            var message = await QuickPrint.PrintClipboardAsync(_service!.Client);
            _tray!.Notify("MiniPrinter", message.Message, warning: message.Warning);
        }
        catch (Exception ex)
        {
            _tray!.Notify("MiniPrinter", ex.Message, warning: true);
        }
    }

    private static async Task PrintFromCommandLineAsync(string[] files)
    {
        try
        {
            using var client = ControlClient.FromTokenFile();
            var message = await QuickPrint.PrintFilesAsync(client, files);
            if (message.Warning)
                MessageBox.Show(message.Message, "MiniPrinter", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Strings.Get("App.PrintFailed", ex.Message), "MiniPrinter", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Flips the keep-alive setting from the tray menu (e.g. to free the printer for the phone).</summary>
    private async void ToggleKeepAlive()
    {
        try
        {
            var client = _service!.Client;
            var settings = await client.GetSettingsAsync();
            var updated = await client.SaveSettingsAsync(settings with { KeepAlive = !settings.KeepAlive });
            _tray!.SetKeepAlive(updated.KeepAlive);
            _tray.Notify("MiniPrinter", updated.KeepAlive
                ? Strings.Get("App.KeepAliveOn")
                : Strings.Get("App.KeepAliveOff"), warning: false);
        }
        catch (Exception ex)
        {
            _tray!.Notify("MiniPrinter", ex.Message, warning: true);
        }
    }

    private void ToggleConnection()
    {
        var connected = _service?.Status?.Link == "Connected";
        RunAction(c => connected ? c.DisconnectAsync() : c.ConnectAsync(), null);
    }

    private async void RunAction<T>(Func<ControlClient, Task<T>> action, string? success)
    {
        try
        {
            await action(_service!.Client);
            if (success is not null)
                _tray!.Notify("MiniPrinter", success, warning: false);
        }
        catch (Exception ex)
        {
            _tray!.Notify("MiniPrinter", ex.Message, warning: true);
        }
    }

    // ---- templates from the icon ---------------------------------------------------------------------------------

    private IReadOnlyList<TemplateDto> _templateCache = [];

    /// <summary>The templates of the service for the menu: asked for each time it opens, and the last known ones if the service does not answer at once.</summary>
    private IReadOnlyList<TemplateDto> TemplatesForMenu()
    {
        try
        {
            var task = Task.Run(() => _service!.Client.GetTemplatesAsync());
            if (task.Wait(TimeSpan.FromMilliseconds(600)))
                _templateCache = task.Result;
        }
        catch (Exception)
        {
            // the cache is what there is
        }
        return _templateCache;
    }

    /// <summary>Prints a favorite at once with its saved values, tells so, and says clearly if it is no longer valid.</summary>
    private async void PrintFavorite(string template, string favorite)
    {
        var title = _templateCache.FirstOrDefault(t => t.Name == template)?.Title ?? template;
        var label = $"{title} — {favorite}";
        try
        {
            if (TemplateFavorites.Read(TemplatesPanel.FavoritesPath).GetValueOrDefault(template)?.GetValueOrDefault(favorite) is not { } values)
                throw new InvalidOperationException(Strings.Get("Tray.FavoriteGone"));
            await _service!.Client.PrintTemplateAsync(template, values);
            UpdatePreferences(p => p.WithRecentTemplate(template));
            _tray!.Notify("MiniPrinter", Strings.Get("Tray.FavoritePrinting", label), warning: false);
        }
        catch (Exception ex)
        {
            _tray!.Notify("MiniPrinter", Strings.Get("Tray.FavoriteFailed", label, ex.Message), warning: true);
        }
    }

    /// <summary>Opens the panel on a template (with its last values), without printing.</summary>
    private void OpenTemplate(string template)
    {
        OpenPanel();
        _window?.ShowTemplate(template);
    }

    /// <summary>Remembers a template as one of the latest used.</summary>
    public void RecordRecentTemplate(string template) => UpdatePreferences(p => p.WithRecentTemplate(template));

    private void ExitApp()
    {
        // The panel may ask about unsaved settings first; if the user keeps editing, the tray keeps running.
        _exitRequested = true;
        if (_window is { } panel)
        {
            panel.Close();
            if (_window is not null)
            {
                _exitRequested = panel.ClosePending;
                return;
            }
        }
        Shutdown();
    }
}
