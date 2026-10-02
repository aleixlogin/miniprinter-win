using MiniPrinter.Control;

namespace MiniPrinter.Service;

/// <summary>
/// Service side of recreating the Windows print queue (design.md D4, D8): it gives the printer a new identity (UUID), waits
/// for the listener to announce it, and runs the shared <see cref="QueueRecreator"/> in the background. When the service has
/// no rights to manage printers the task ends with <c>NeedsElevation</c> so the tray can repeat it elevated (UAC).
/// </summary>
public sealed class WindowsQueue
{
    public static readonly TimeSpan ListenerTimeout = TimeSpan.FromSeconds(90);

    private readonly SettingsStore _settings;
    private readonly IppHost _ipp;
    private readonly IPowerShellRunner _runner;
    private readonly ILogger<WindowsQueue> _logger;
    private readonly object _gate = new();
    private WindowsQueueTaskDto _task = new("Idle", null, null);
    private Task? _running;

    public WindowsQueue(SettingsStore settings, IppHost ipp, IPowerShellRunner runner, ILogger<WindowsQueue> logger)
    {
        _settings = settings;
        _ipp = ipp;
        _runner = runner;
        _logger = logger;
    }

    /// <summary>Raised whenever the task state changes.</summary>
    public event Action? Changed;

    /// <summary>Wait for the listener after taking a new identity (the tests make it instant).</summary>
    public Func<CancellationToken, Task> WaitForListener { get; set; } = null!;

    /// <summary>How many times the recreation may be repeated while Windows still offers a stale list of sizes.</summary>
    public int MaxPasses { get; set; } = 3;

    public WindowsQueueTaskDto Current
    {
        get { lock (_gate) return _task; }
    }

    public bool IsRunning => Current.State == "Running";

    /// <summary>Why a printer name cannot be used for a Windows queue, or null.</summary>
    public static string? CheckName(string? name) => QueueScripts.CheckName(name);

    /// <summary>The sizes Windows must offer, as "WxH" in millimetres (the effective list of the settings).</summary>
    public static IReadOnlyList<string> ExpectedSizes(ServiceSettings settings) =>
        [.. PaperCatalog.Effective(settings).Select(p => $"{p.WidthMm}x{p.LengthMm}")];

    /// <summary>
    /// Takes a new printer identity and waits until the listener announces it. Windows refuses a second queue for a printer with
    /// the same UUID, so each creation of a queue needs a new one. Also used by the elevated helper through the control API.
    /// </summary>
    public async Task PrepareAsync(CancellationToken ct)
    {
        _settings.Update(s => s with { PrinterUuid = Guid.NewGuid().ToString() });
        await (WaitForListener ?? DefaultWaitAsync)(ct);
    }

    private async Task DefaultWaitAsync(CancellationToken ct) => await _ipp.WaitForCurrentAsync(ListenerTimeout, ct);

    /// <summary>Does the queue named like the printer exist in Windows?</summary>
    public async Task<WindowsQueueDto> GetAsync(CancellationToken ct)
    {
        var name = _settings.Current.PrinterName;
        var task = Current;
        var exists = false;
        if (task.State != "Running" && CheckName(name) is null)
        {
            try
            {
                var env = new Dictionary<string, string> { ["MP_QUEUE"] = name, ["MP_OLD"] = name, ["MP_TEMP"] = name + " (nueva)", ["MP_URL"] = "", ["MP_SIZES"] = "" };
                exists = (await _runner.RunAsync(QueueScripts.Exists, env, ct)).ExitCode == 0;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _logger.LogDebug(ex, "Could not query the Windows queue");
            }
        }
        return new WindowsQueueDto(exists, name, task.State, task.Message, task.Step, task.NeedsElevation);
    }

    /// <summary>Starts the recreation in the background. Throws <see cref="InvalidOperationException"/> if one is already running.</summary>
    public void Start(string? previousName)
    {
        var settings = _settings.Current;
        if (CheckName(settings.PrinterName) is { } problem)
            throw new PrintRequestException(problem);
        var old = string.IsNullOrWhiteSpace(previousName) ? settings.PrinterName : previousName.Trim();
        if (CheckName(old) is { } oldProblem)
            throw new PrintRequestException(oldProblem);

        lock (_gate)
        {
            if (_task.State == "Running")
                throw new InvalidOperationException("Ya se está recreando la impresora de Windows.");
            _task = new("Running", "Preparando…", "start");
            _running = Task.Run(() => RunAsync(settings.PrinterName, old, CancellationToken.None));
        }
        Changed?.Invoke();
    }

    /// <summary>For the tests: waits for the running task to finish.</summary>
    public Task WhenIdle() => _running ?? Task.CompletedTask;

    private async Task RunAsync(string name, string old, CancellationToken ct)
    {
        var settings = _settings.Current;
        var request = new QueueRequest(name, old, $"http://127.0.0.1:{settings.IppPort}/ipp/print", ExpectedSizes(settings), MaxPasses);
        var recreator = new QueueRecreator(_runner);
        recreator.Progress += (state, message, step) => Set(state, message, step);
        try
        {
            var outcome = await recreator.RunAsync(request, new LocalHost(this), ct);
            if (!outcome.Succeeded)
                _logger.LogWarning("Windows queue recreation failed at {Step}: {Message}", outcome.Step, outcome.Message);
            Set(outcome.Succeeded ? "Succeeded" : "Failed", outcome.Message, outcome.Step, outcome.NeedsElevation);
        }
        catch (TimeoutException ex)
        {
            Set("Failed", ex.Message, "listener");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or OperationCanceledException)
        {
            _logger.LogError(ex, "Recreating the Windows print queue failed");
            Set("Failed", $"No se pudo ejecutar PowerShell: {ex.Message}", "error");
        }
    }

    private void Set(string state, string message, string step, bool needsElevation = false)
    {
        lock (_gate)
            _task = new(state, message, step, needsElevation);
        Changed?.Invoke();
    }

    private sealed class LocalHost(WindowsQueue owner) : IQueueHost
    {
        public Task PrepareAsync(CancellationToken ct) => owner.PrepareAsync(ct);
    }
}
