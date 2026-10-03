using System.Globalization;
using System.Text;
using MiniPrinter.Control;

namespace MiniPrinter.Gui;

/// <summary>What the report says about the machine (the window collects it).</summary>
public sealed record DiagnosticsReportContext(string AppVersion, string ServiceVersion, string Windows, ServiceSettings? Settings, PrinterSelection? Printer,
    string? LastError, DateTimeOffset Now);

/// <summary>
/// The text a user can paste into an incident: versions, a summary of the settings and the result of each check. It is made to be
/// shared, so it leaves out the token of the API, the names of templates, the contents of jobs, the name and the address of the
/// printer and the addresses of the clients of the port.
/// </summary>
public static class DiagnosticsReport
{
    public static string Build(DiagnosticsReportContext context, IReadOnlyList<DiagnosticRow> rows)
    {
        var text = new StringBuilder();
        text.AppendLine(Strings.Get("Diag.Report.Header"));
        text.AppendLine(Strings.Get("Diag.Report.Date", context.Now.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)));
        text.AppendLine(Strings.Get("Diag.Report.Versions", context.AppVersion, context.ServiceVersion, context.Windows));
        text.AppendLine(context.Printer is { } printer
            ? Strings.Get("Diag.Report.Printer", printer.ProfileKey, printer.Transport)
            : Strings.Get("Diag.Report.NoPrinter"));
        if (context.Settings is { } settings)
            text.AppendLine(SettingsLine(settings));
        text.AppendLine();
        text.AppendLine(Strings.Get("Diag.Report.Results"));
        foreach (var row in rows)
        {
            // The addresses the port listens on are the machine's own, but a report is for sharing: it only says that it listens.
            var message = row is { Id: DiagnosticIds.Raw, Status: DiagnosticStatus.Ok } && row.Detail is { Length: > 0 } and not "Desactivado"
                ? Strings.Get("Diag.Report.RawListening")
                : row.Message;
            text.Append("  [").Append(Mark(row.Status)).Append("] ").Append(row.Title).Append(": ").Append(message);
            text.AppendLine();
            if (row.NeedsAttention && row.Hint.Length > 0)
                text.Append("      → ").AppendLine(row.Hint);
        }
        if (!string.IsNullOrWhiteSpace(context.LastError))
        {
            text.AppendLine();
            text.AppendLine(Strings.Get("Diag.Report.LastError", context.LastError));
        }
        return text.ToString();
    }

    private static string Mark(string status) => status switch
    {
        DiagnosticStatus.Ok => "ok",
        DiagnosticStatus.Warn => "aviso",
        DiagnosticStatus.Fail => "fallo",
        _ => "?",
    };

    private static string SettingsLine(ServiceSettings settings)
    {
        string OnOff(bool on) => Strings.Get(on ? "Diag.Report.On" : "Diag.Report.Off");
        var sizes = PaperCatalog.All(settings);
        return Strings.Get("Diag.Report.Settings",
            Strings.Get(settings.NetworkMode == NetworkMode.Lan ? "Diag.Report.Lan" : "Diag.Report.Local"),
            settings.IppPort,
            settings.RawPortEnabled ? Strings.Get("Diag.Report.RawOn", settings.RawPort) : OnOff(false),
            OnOff(settings.AutomationApiEnabled),
            OnOff(settings.KeepAlive),
            Strings.Get("Diag.Report.Paper", sizes.Count(s => s.Enabled), sizes.Count),
            settings.JobHistoryKeep);
    }
}
