using System.Windows;
using MiniPrinter.Control;

namespace MiniPrinter.Tray;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private ServiceConnection? _service;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private StatusDto? _lastStatus;
    private const string OpenPanelEventName = @"Local\MiniPrinter.Tray.OpenPanel";
    private const string ExitEventName = @"Local\MiniPrinter.Tray.Exit";
    private EventWaitHandle? _exitSignal;
    private EventWaitHandle? _openPanelSignal;
    private bool _wizardShown;
    private GlobalHotkey? _hotkey;
    private Updater? _updater;

    public Updater? Updater => _updater;
    private QuickNoteWindow? _quickNote;
    private TrayPreferences _preferences = TrayPreferences.Load();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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

        _service = new ServiceConnection();
        _tray = new TrayIcon(OpenPanel, ToggleConnection, () => RunAction(c => c.TestPrintAsync(), "Página de prueba enviada."),
            () => RunAction(c => c.FeedAsync(), null), ExitApp, PrintClipboard, OpenQuickNote, ToggleKeepAlive,
            () => _ = _updater?.CheckAsync(manual: true));
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
            _tray!.Notify("La impresora necesita atención", string.Join(", ", newAlarms.Select(Translate)), warning: true);
        if (current.LowBattery && previous?.LowBattery != true)
            _tray!.Notify("Batería baja", $"La impresora está al {current.BatteryPercent} %. Conéctala para cargar.", warning: true);
        if (current.LastErrorKind == "Busy" && previous?.LastErrorKind != "Busy")
            _tray!.Notify("Impresora ocupada", "Otra aplicación (TiMini-Print, la app del móvil…) está usando la impresora.", warning: true);
    }

    public static string Translate(string alarm) => alarm switch
    {
        "OutOfPaper" => "sin papel o tapa abierta",
        "Overheated" => "sobrecalentada",
        "LowBattery" => "batería baja",
        _ => alarm,
    };

    public static string Describe(StatusDto? status, bool available, string? error)
    {
        if (!available || status is null)
            return error ?? "Servicio no disponible";
        if (status.Printer is null)
            return "Sin impresora seleccionada";
        if (status.Alarms.Count > 0)
            return $"{status.Printer.Name}: {string.Join(", ", status.Alarms.Select(Translate))}";
        if (status.Printing)
            return $"{status.Printer.Name}: imprimiendo";
        if (status.Reconnecting)
            return $"{status.Printer.Name}: reconectando…";
        return status.Link switch
        {
            "Connected" => status.KeepAlive ? $"{status.Printer.Name}: lista (keep-alive)" : $"{status.Printer.Name}: lista",
            "Connecting" => $"{status.Printer.Name}: conectando…",
            "Error" => $"{status.Printer.Name}: {status.LastError}",
            _ => $"{status.Printer.Name}: desconectada (se conecta al imprimir)",
        };
    }

    public void OpenPanel()
    {
        if (_window is null)
        {
            _window = new MainWindow(_service!);
            _window.Closed += (_, _) => _window = null;
            _window.ShowStatus(_service!.Status, _service.ServiceAvailable, _service.ServiceError);
        }
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public TrayPreferences Preferences => _preferences;

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
            _tray?.Notify("Atajo no disponible",
                $"No se pudo registrar {hotkey} (¿lo usa otro programa?). Elige otro en Ajustes.", warning: true);
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
            _tray!.Notify("MiniPrinter", message, warning: message.StartsWith("No hay", StringComparison.Ordinal));
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
            if (message.Contains("no admitido", StringComparison.Ordinal))
                MessageBox.Show(message, "MiniPrinter", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo imprimir: {ex.Message}", "MiniPrinter", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                ? "Keep-alive activado: la impresora se mantendrá conectada."
                : "Keep-alive desactivado: la impresora se liberará tras el tiempo de inactividad.", warning: false);
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

    private void ExitApp()
    {
        _window?.Close();
        Shutdown();
    }
}
