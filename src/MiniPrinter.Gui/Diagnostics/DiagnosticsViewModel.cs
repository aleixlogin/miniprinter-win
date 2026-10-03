using System.Collections.ObjectModel;
using MiniPrinter.Control;

namespace MiniPrinter.Gui;

/// <summary>What the diagnostics need from outside: the service, and what only the tray can see (the Bluetooth radio and the pairing).</summary>
public interface IDiagnosticsSource
{
    /// <summary>The checks the service makes. Throws when the service does not answer.</summary>
    Task<IReadOnlyList<DiagnosticCheckDto>> GetServiceChecksAsync();

    /// <summary>Whether the Bluetooth radio of Windows is on; null when it cannot be told.</summary>
    Task<bool?> IsBluetoothOnAsync();

    /// <summary>Whether the printer is among the paired devices; null when it cannot be told.</summary>
    Task<bool?> IsPairedAsync(PrinterSelection printer);

    /// <summary>The printer chosen (the last one known, also when the service is down), or null.</summary>
    PrinterSelection? Printer { get; }
}

/// <summary>What the button of a row of the diagnostics does.</summary>
public enum DiagnosticAction
{
    None,
    FindPrinters,
    ConnectPrinter,
    OpenBluetoothSettings,
    OpenSettingsPaper,
    OpenSettingsNetwork,
    OpenSettingsRaw,
}

/// <summary>One row of the window: a check, how it went, what it means and what to do.</summary>
public sealed record DiagnosticRow(string Id, string Status, string Title, string Message, string Hint, DiagnosticAction Action, string ActionText, string? Detail)
{
    public string Glyph => Status switch
    {
        DiagnosticStatus.Ok => "✔",
        DiagnosticStatus.Warn => "⚠",
        DiagnosticStatus.Fail => "✖",
        _ => "?",
    };

    public bool HasAction => Action != DiagnosticAction.None;

    public bool NeedsAttention => Status is DiagnosticStatus.Warn or DiagnosticStatus.Fail;
}

/// <summary>
/// The diagnostics: runs the checks of the service and of the tray, puts them in order, says what each result means and what to do,
/// and sums up. A check that could not be made is shown as unknown, never as a failure: nothing is blamed that was not looked at.
/// </summary>
public sealed class DiagnosticsViewModel : ObservableObject
{
    /// <summary>The order of the rows: from the outside in, so that the first problem is usually the cause of the ones below it.</summary>
    private static readonly string[] Order =
    [
        DiagnosticIds.Service, DiagnosticIds.Bluetooth, DiagnosticIds.Paired, DiagnosticIds.Link, DiagnosticIds.Paper,
        DiagnosticIds.Queue, DiagnosticIds.Ipp, DiagnosticIds.Raw, DiagnosticIds.Fonts,
    ];

    private readonly IDiagnosticsSource _source;
    private string _summary = "";
    private bool _isRunning;
    private IReadOnlyList<DiagnosticCheckDto> _lastServiceChecks = [];

    public DiagnosticsViewModel(IDiagnosticsSource source)
    {
        _source = source;
        Run = new AsyncCommand(RunAsync);
    }

    public ObservableCollection<DiagnosticRow> Rows { get; } = [];

    public AsyncCommand Run { get; }

    /// <summary>"Todo en orden", "Hay 2 problemas"…</summary>
    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    /// <summary>The worst result of the rows (what colours the summary).</summary>
    public string OverallStatus { get; private set; } = DiagnosticStatus.Unknown;

    public bool IsRunning
    {
        get => _isRunning;
        private set => Set(ref _isRunning, value);
    }

    public async Task RunAsync()
    {
        IsRunning = true;
        try
        {
            var results = await CollectAsync();
            Rows.Clear();
            foreach (var row in results)
                Rows.Add(row);
            Summarize();
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task<List<DiagnosticRow>> CollectAsync()
    {
        var checks = new Dictionary<string, DiagnosticCheckDto>();

        // 1. The service. If it does not answer, what depends on it is unknown, and only that is explained.
        try
        {
            _lastServiceChecks = await _source.GetServiceChecksAsync();
            foreach (var check in _lastServiceChecks)
                checks[check.Id] = check;
            checks[DiagnosticIds.Service] = new DiagnosticCheckDto(DiagnosticIds.Service, DiagnosticStatus.Ok, null);
        }
        catch (Exception ex)
        {
            checks[DiagnosticIds.Service] = new DiagnosticCheckDto(DiagnosticIds.Service, DiagnosticStatus.Fail, ex.Message);
            foreach (var id in new[] { DiagnosticIds.Link, DiagnosticIds.Paper, DiagnosticIds.Queue, DiagnosticIds.Ipp, DiagnosticIds.Raw, DiagnosticIds.Fonts })
                checks[id] = new DiagnosticCheckDto(id, DiagnosticStatus.Unknown, null);
        }

        // 2. What only the tray sees (these do not need the service).
        var printer = _source.Printer;
        checks[DiagnosticIds.Bluetooth] = FromBool(DiagnosticIds.Bluetooth, await SafeAsync(_source.IsBluetoothOnAsync));
        checks[DiagnosticIds.Paired] = printer is null
            ? new DiagnosticCheckDto(DiagnosticIds.Paired, DiagnosticStatus.Unknown, null)
            : FromBool(DiagnosticIds.Paired, await SafeAsync(() => _source.IsPairedAsync(printer)));

        return [.. Order.Where(checks.ContainsKey).Select(id => Describe(checks[id]))];
    }

    private static async Task<bool?> SafeAsync(Func<Task<bool?>> probe)
    {
        try
        {
            return await probe();
        }
        catch (Exception)
        {
            return null; // it could not be told: unknown, not a failure
        }
    }

    private static DiagnosticCheckDto FromBool(string id, bool? value) => new(id, value switch
    {
        true => DiagnosticStatus.Ok,
        false => DiagnosticStatus.Fail,
        null => DiagnosticStatus.Unknown,
    }, null);

    // ---- what each result means ---------------------------------------------------------------------------------------

    /// <summary>The texts of a check: what it found, what to do about it and where the button leads.</summary>
    public static DiagnosticRow Describe(DiagnosticCheckDto check)
    {
        var status = check.Status is DiagnosticStatus.Ok or DiagnosticStatus.Warn or DiagnosticStatus.Fail ? check.Status : DiagnosticStatus.Unknown;
        var title = TextOrNull($"Diag.{check.Id}.Title") ?? check.Id;
        var message = Message(check, status);
        var hint = status == DiagnosticStatus.Ok ? "" : TextOrNull($"Diag.{check.Id}.{status}.Hint") ?? Strings.Get("Diag.Generic.Hint");
        var action = status == DiagnosticStatus.Ok ? DiagnosticAction.None : ActionFor(check.Id, status, check.Detail);
        var actionText = action == DiagnosticAction.None ? "" : Strings.Get($"Diag.Action.{action}");
        return new DiagnosticRow(check.Id, status, title, message, hint, action, actionText, check.Detail);
    }

    private static string Message(DiagnosticCheckDto check, string status)
    {
        var detail = check.Detail ?? "";
        // What the service reports in a few words is translated here; anything else is shown as it came.
        if (check.Id == DiagnosticIds.Paper && status == DiagnosticStatus.Warn)
            detail = ServiceTexts.AlarmList(detail);
        if (check.Id == DiagnosticIds.Raw && status == DiagnosticStatus.Ok)
            return detail == "Desactivado" ? Strings.Get("Diag.raw.ok.Off") : Strings.Get("Diag.raw.ok", detail);
        if (check.Id == DiagnosticIds.Raw && status == DiagnosticStatus.Warn && detail.Contains("cortafuegos", StringComparison.OrdinalIgnoreCase))
            return Strings.Get("Diag.raw.warn.Firewall");
        if (check.Id == DiagnosticIds.Raw && status == DiagnosticStatus.Unknown && detail.Contains("cortafuegos", StringComparison.OrdinalIgnoreCase))
            return Strings.Get("Diag.raw.unknown.Firewall");
        return TextOrNull($"Diag.{check.Id}.{status}", detail) ?? Strings.Get($"Diag.Generic.{status}");
    }

    private static DiagnosticAction ActionFor(string id, string status, string? detail) => (id, status) switch
    {
        (DiagnosticIds.Bluetooth, _) => DiagnosticAction.OpenBluetoothSettings,
        (DiagnosticIds.Paired, _) => DiagnosticAction.FindPrinters,
        (DiagnosticIds.Link, DiagnosticStatus.Fail) when detail?.Contains("ninguna impresora", StringComparison.OrdinalIgnoreCase) == true => DiagnosticAction.FindPrinters,
        (DiagnosticIds.Link, DiagnosticStatus.Fail) => DiagnosticAction.ConnectPrinter,
        (DiagnosticIds.Queue, DiagnosticStatus.Fail) => DiagnosticAction.OpenSettingsPaper,
        (DiagnosticIds.Ipp, DiagnosticStatus.Fail) => DiagnosticAction.OpenSettingsNetwork,
        (DiagnosticIds.Raw, DiagnosticStatus.Fail or DiagnosticStatus.Warn) => DiagnosticAction.OpenSettingsRaw,
        _ => DiagnosticAction.None,
    };

    private static string? TextOrNull(string key, params object?[] arguments) =>
        Strings.Exists(key) ? Strings.Get(key, arguments) : null;

    // ---- the summary ---------------------------------------------------------------------------------------------------

    private void Summarize()
    {
        var failures = Rows.Count(r => r.Status == DiagnosticStatus.Fail);
        var warnings = Rows.Count(r => r.Status == DiagnosticStatus.Warn);
        var unknown = Rows.Count(r => r.Status == DiagnosticStatus.Unknown);
        OverallStatus = failures > 0 ? DiagnosticStatus.Fail : warnings > 0 ? DiagnosticStatus.Warn : unknown > 0 ? DiagnosticStatus.Unknown : DiagnosticStatus.Ok;
        Summary = failures > 0 ? Strings.Get(failures == 1 ? "Diag.Summary.OneProblem" : "Diag.Summary.Problems", failures)
            : warnings > 0 ? Strings.Get(warnings == 1 ? "Diag.Summary.OneWarning" : "Diag.Summary.Warnings", warnings)
            : unknown > 0 ? Strings.Get(unknown == 1 ? "Diag.Summary.OneUnknown" : "Diag.Summary.Unknowns", unknown)
            : Strings.Get("Diag.Summary.AllGood");
        OnPropertyChanged(nameof(OverallStatus));
    }
}
