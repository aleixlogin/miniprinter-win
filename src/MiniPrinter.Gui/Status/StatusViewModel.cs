using System.Collections.ObjectModel;
using MiniPrinter.Control;

namespace MiniPrinter.Gui;

/// <summary>What the Status tab asks of the service.</summary>
public interface IStatusGateway
{
    Task ConnectAsync();

    Task DisconnectAsync();

    Task TestPrintAsync();

    Task FeedAsync();

    Task StartSamplingAsync(TimeSpan duration);

    Task StopSamplingAsync();

    Task<IReadOnlyList<JobDto>> GetJobsAsync();

    Task CancelJobAsync(int jobId);

    Task<JobDto> ReprintJobAsync(int jobId);
}

/// <summary>
/// The Status tab: the card with the single state, the battery, the details, the actions and the list of jobs. It turns what the
/// service reports into texts and commands, so that all of it can be tested without a window.
/// </summary>
public sealed class StatusViewModel : ObservableObject
{
    private readonly IStatusGateway _gateway;
    private readonly string _appVersion;
    private StatusDto? _status;
    private StatusSummary _summary;
    private bool _detailsOpen;
    private JobRowViewModel? _selected;
    private DateTime _lastJobsRefresh = DateTime.MinValue;

    public StatusViewModel(IStatusGateway gateway, string appVersion, bool detailsOpen = false)
    {
        _gateway = gateway;
        _appVersion = appVersion;
        _detailsOpen = detailsOpen;
        _summary = StatusRules.Derive(null, serviceAvailable: false, serviceError: null);

        ConnectOrDisconnect = Command(() => _status?.Link == "Connected" ? _gateway.DisconnectAsync() : _gateway.ConnectAsync(), HasPrinterToDrive);
        TestPrint = Command(() => _gateway.TestPrintAsync(), HasPrinterToDrive);
        Feed = Command(() => _gateway.FeedAsync(), HasPrinterToDrive);
        ToggleSampling = Command(() => _status?.SamplingUntil is null ? _gateway.StartSamplingAsync(TimeSpan.FromHours(8)) : _gateway.StopSamplingAsync(), HasPrinterToDrive);
        CancelJob = Command(async () =>
        {
            if (_selected is { CanCancel: true } job)
                await _gateway.CancelJobAsync(job.Id);
        }, () => _selected is { CanCancel: true });
        ReprintJob = Command(async () =>
        {
            if (_selected is not { CanReprint: true } job)
                return;
            var again = await _gateway.ReprintJobAsync(job.Id);
            Reprinted?.Invoke(again);
        }, () => _selected is { CanReprint: true });
        Diagnose = new RelayCommand(() => DiagnosticsRequested?.Invoke());
        SummaryAction = new RelayCommand(() => ActionRequested?.Invoke(_summary.Action), () => _summary.Action != StatusAction.None);
    }

    /// <summary>Connecting, printing a test and the like need a service that answers and a printer chosen.</summary>
    private bool HasPrinterToDrive() => _summary.Kind is not (StatusKind.ServiceUnavailable or StatusKind.NoPrinter);

    private AsyncCommand Command(Func<Task> run, Func<bool>? canRun = null)
    {
        var command = new AsyncCommand(run, canRun);
        command.Failed += ex => Failed?.Invoke(ex);
        return command;
    }

    /// <summary>Raised with the problem when an action fails (the window shows it).</summary>
    public event Action<Exception>? Failed;

    /// <summary>Raised when the button of the card is pressed (go and find printers, connect…).</summary>
    public event Action<StatusAction>? ActionRequested;

    /// <summary>Raised with the new job when a job was printed again.</summary>
    public event Action<JobDto>? Reprinted;

    /// <summary>Raised when the user opens or closes the details (to remember it).</summary>
    public event Action<bool>? DetailsOpenChanged;

    // ---- the card ---------------------------------------------------------------------------------------------------

    public StatusSummary Summary
    {
        get => _summary;
        private set
        {
            if (Set(ref _summary, value))
                SummaryAction.RaiseCanExecuteChanged();
        }
    }

    public RelayCommand SummaryAction { get; }

    /// <summary>Opens the diagnostics (from the card when something is wrong, and from the menu).</summary>
    public RelayCommand Diagnose { get; }

    /// <summary>Whether the card offers the diagnostics next to its own button: when the state is a warning or an error.</summary>
    public bool ShowsDiagnose => _summary.Severity is StatusSeverity.Warn or StatusSeverity.Error;

    /// <summary>Raised when the diagnostics are asked for.</summary>
    public event Action? DiagnosticsRequested;

    /// <summary>The battery as a percentage for the bar, or null when the unit is not known.</summary>
    public int? BatteryPercent => _status?.BatteryPercent;

    public bool ShowsBatteryBar => BatteryPercent is not null;

    public bool BatteryIsLow => _status?.LowBattery == true;

    /// <summary>The battery as text: "39 %", "3,9 V (≈40 %)" or the raw value.</summary>
    public string BatteryText => _status is null ? "" : BatteryInterpreter.Describe(_status.BatteryLevel, _status.BatteryUnit)
                                                   + (_status.LowBattery ? Strings.Get("Main.BatteryLow") : "");

    public bool ShowsBattery => _status is { Printer: not null } && _status.BatteryLevel is not null;

    // ---- actions ----------------------------------------------------------------------------------------------------

    public AsyncCommand ConnectOrDisconnect { get; }

    public AsyncCommand TestPrint { get; }

    public AsyncCommand Feed { get; }

    public AsyncCommand ToggleSampling { get; }

    public string ConnectText => _status?.Link == "Connected" ? Strings.Get("Main.Disconnect") : Strings.Get("Main.Conectar");

    public string SamplingText => _status?.SamplingUntil is null ? Strings.Get("Main.MuestrearBateria8H") : Strings.Get("Main.SamplingStop");

    // ---- details ----------------------------------------------------------------------------------------------------

    public bool DetailsOpen
    {
        get => _detailsOpen;
        set
        {
            if (Set(ref _detailsOpen, value))
                DetailsOpenChanged?.Invoke(value);
        }
    }

    public string PrinterText => _status?.Printer is { } printer ? $"{printer.Name}  ·  {printer.Address}  ·  {printer.Transport}" : Strings.Get("Main.PrinterNone");

    public string LinkText => _status is null ? "" : _status.Reconnecting ? Strings.Get("Main.LinkReconnecting", _status.LastError) : _status.Link switch
    {
        "Connected" => _status.KeepAlive ? Strings.Get("Main.LinkConnectedKeepAlive") : Strings.Get("Main.LinkConnected"),
        "Connecting" => Strings.Get("Main.LinkConnecting"),
        "Error" => Strings.Get("Main.LinkError", _status.LastError),
        _ => Strings.Get("Main.LinkDisconnected"),
    };

    public string FirmwareText => _status?.Firmware ?? "—";

    public string UrlsText => _status is null ? "" : string.Join(Environment.NewLine, _status.IppUrls);

    public string ServiceText { get; private set; } = "";

    public string SamplingInfo => _status?.SamplingUntil is { } until ? Strings.Get("Main.SamplingUntil", until.ToLocalTime()) : "";

    // ---- jobs -------------------------------------------------------------------------------------------------------

    public ObservableCollection<JobRowViewModel> Jobs { get; } = [];

    public JobRowViewModel? SelectedJob
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value))
                return;
            CancelJob.RaiseCanExecuteChanged();
            ReprintJob.RaiseCanExecuteChanged();
        }
    }

    public AsyncCommand CancelJob { get; }

    public AsyncCommand ReprintJob { get; }

    /// <summary>Takes what the service reports and updates the card, the details and the actions.</summary>
    public void Update(StatusDto? status, bool serviceAvailable, string? serviceError)
    {
        _status = serviceAvailable ? status : null;
        Summary = StatusRules.Derive(status, serviceAvailable, serviceError);
        ServiceText = (serviceAvailable ? Strings.Get("Main.ServiceRunning", status?.Version) : serviceError ?? Strings.Get("Main.ServiceNotAvailable"))
                      + "  ·  " + Strings.Get("Main.AppVersion", _appVersion);
        OnPropertyChanged(string.Empty);
        foreach (var command in new[] { ConnectOrDisconnect, TestPrint, Feed, ToggleSampling, CancelJob, ReprintJob })
            command.RaiseCanExecuteChanged();
    }

    /// <summary>Whether the list of jobs should be asked for again (not more than twice a second, whatever the service reports).</summary>
    public bool ShouldRefreshJobs(DateTime now)
    {
        if (now - _lastJobsRefresh <= TimeSpan.FromMilliseconds(500))
            return false;
        _lastJobsRefresh = now;
        return true;
    }

    /// <summary>Reads the jobs and merges them into the list, keeping the rows that stay (so the selection and the scroll stay).</summary>
    public async Task RefreshJobsAsync()
    {
        IReadOnlyList<JobDto> jobs;
        try
        {
            jobs = await _gateway.GetJobsAsync();
        }
        catch (Exception)
        {
            // The card already reports a service that does not answer.
            return;
        }
        MergeJobs(jobs);
    }

    /// <summary>Puts the jobs in the list, newest first, reusing the row of each job that was already there.</summary>
    public void MergeJobs(IReadOnlyList<JobDto> jobs)
    {
        var byId = Jobs.ToDictionary(j => j.Id);
        var incoming = jobs.Select(j => j.Id).ToHashSet();
        foreach (var gone in Jobs.Where(j => !incoming.Contains(j.Id)).ToList())
        {
            if (ReferenceEquals(_selected, gone))
                SelectedJob = null;
            Jobs.Remove(gone);
        }
        for (var i = 0; i < jobs.Count; i++)
        {
            var job = jobs[i];
            if (byId.TryGetValue(job.Id, out var row))
            {
                row.Update(job);
                var at = Jobs.IndexOf(row);
                if (at != i)
                    Jobs.Move(at, i);
            }
            else
            {
                Jobs.Insert(i, new JobRowViewModel(job));
            }
        }
        CancelJob.RaiseCanExecuteChanged();
        ReprintJob.RaiseCanExecuteChanged();
    }
}
