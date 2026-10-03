using MiniPrinter.Control;

namespace MiniPrinter.Gui;

/// <summary>The one state the status card shows, by priority (see <see cref="StatusRules.Derive"/>).</summary>
public enum StatusKind
{
    ServiceUnavailable,
    NoPrinter,
    Alarm,
    LinkError,
    Connecting,
    Printing,
    Ready,
    Disconnected,
}

/// <summary>How the card is coloured; the view maps each to the brushes of the theme.</summary>
public enum StatusSeverity
{
    Neutral,
    Ok,
    Info,
    Warn,
    Error,
}

/// <summary>What the button of the card does.</summary>
public enum StatusAction
{
    None,
    FindPrinters,
    Connect,
}

/// <param name="Glyph">A symbol for the card (the view draws it with the font of symbols).</param>
public sealed record StatusSummary(StatusKind Kind, StatusSeverity Severity, string Glyph, string Title, string Message,
    StatusAction Action = StatusAction.None, string ActionText = "");

/// <summary>Derives the single visible state from what the service reports. A pure function, tested with a table of cases.</summary>
public static class StatusRules
{
    /// <summary>
    /// By priority: service unavailable, no printer chosen, alarm (no paper, overheated, low battery), link error,
    /// connecting or reconnecting, printing, ready, disconnected. A higher state wins over any data kept from before.
    /// </summary>
    public static StatusSummary Derive(StatusDto? status, bool serviceAvailable, string? serviceError)
    {
        if (!serviceAvailable || status is null)
            return new(StatusKind.ServiceUnavailable, StatusSeverity.Error, "✖", Strings.Get("Status.ServiceUnavailable.Title"),
                string.IsNullOrWhiteSpace(serviceError) ? Strings.Get("Status.ServiceUnavailable.Message") : serviceError);

        if (status.Printer is null)
            return new(StatusKind.NoPrinter, StatusSeverity.Info, "?", Strings.Get("Status.NoPrinter.Title"), Strings.Get("Status.NoPrinter.Message"),
                StatusAction.FindPrinters, Strings.Get("Status.NoPrinter.Action"));

        if (status.Alarms.Count > 0)
            return Alarm(status.Alarms);

        if (status.Link == "Error")
            return new(StatusKind.LinkError, StatusSeverity.Error, "✖", Strings.Get("Status.LinkError.Title"),
                string.IsNullOrWhiteSpace(status.LastError) ? Strings.Get("Status.LinkError.Message") : status.LastError,
                StatusAction.Connect, Strings.Get("Status.LinkError.Action"));

        if (status.Reconnecting)
            return new(StatusKind.Connecting, StatusSeverity.Info, "⟳", Strings.Get("Status.Reconnecting.Title"), Strings.Get("Status.Reconnecting.Message"));

        if (status.Link == "Connecting")
            return new(StatusKind.Connecting, StatusSeverity.Info, "⟳", Strings.Get("Status.Connecting.Title"), Strings.Get("Status.Connecting.Message"));

        if (status.Printing)
            return new(StatusKind.Printing, StatusSeverity.Info, "●", Strings.Get("Status.Printing.Title"), Strings.Get("Status.Printing.Message"));

        if (status.Link == "Connected")
            return new(StatusKind.Ready, StatusSeverity.Ok, "✔", Strings.Get("Status.Ready.Title"),
                status.KeepAlive ? Strings.Get("Status.Ready.KeepAlive") : Strings.Get("Status.Ready.Message"));

        return new(StatusKind.Disconnected, StatusSeverity.Neutral, "○", Strings.Get("Status.Disconnected.Title"), Strings.Get("Status.Disconnected.Message"),
            StatusAction.Connect, Strings.Get("Status.Disconnected.Action"));
    }

    private static StatusSummary Alarm(IReadOnlyList<string> alarms)
    {
        // No paper first (jobs wait for it), then overheating, then a low battery; any other alarm keeps its own name.
        if (alarms.Contains("OutOfPaper"))
            return new(StatusKind.Alarm, StatusSeverity.Warn, "⚠", Strings.Get("Status.OutOfPaper.Title"), Strings.Get("Status.OutOfPaper.Message"));
        if (alarms.Contains("Overheated"))
            return new(StatusKind.Alarm, StatusSeverity.Warn, "⚠", Strings.Get("Status.Overheated.Title"), Strings.Get("Status.Overheated.Message"));
        if (alarms.Contains("LowBattery"))
            return new(StatusKind.Alarm, StatusSeverity.Warn, "⚠", Strings.Get("Status.LowBatteryAlarm.Title"), Strings.Get("Status.LowBatteryAlarm.Message"));
        var name = ServiceTexts.AlarmList(string.Join(", ", alarms));
        return new(StatusKind.Alarm, StatusSeverity.Warn, "⚠", char.ToUpperInvariant(name[0]) + name[1..], Strings.Get("Status.OtherAlarm.Message"));
    }
}
