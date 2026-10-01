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
    private bool _wizardShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\MiniPrinter.Tray", out var created);
        if (!created)
        {
            Shutdown();
            return;
        }

        _service = new ServiceConnection();
        _tray = new TrayIcon(OpenPanel, ToggleConnection, () => RunAction(c => c.TestPrintAsync(), "Página de prueba enviada."),
            () => RunAction(c => c.FeedAsync(), null), ExitApp);
        _service.Changed += () => Dispatcher.BeginInvoke(OnServiceChanged);
        _service.Start();

        if (e.Args.Contains("--open"))
            OpenPanel();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
        return status.Link switch
        {
            "Connected" => $"{status.Printer.Name}: lista",
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
