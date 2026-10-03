namespace MiniPrinter.Gui;

/// <summary>
/// Translates what the service reports (job states and messages are English strings of the IPP backend). What is
/// not in the table is shown as it came, so nothing is lost when the service learns a new message.
/// </summary>
public static class ServiceTexts
{
    private static readonly HashSet<string> States = ["Pending", "PendingHeld", "Processing", "ProcessingStopped", "Completed", "Canceled", "Aborted"];

    private static readonly Dictionary<string, string> Messages = new(StringComparer.Ordinal)
    {
        ["Printed"] = "Message.Printed",
        ["Canceled"] = "Message.Canceled",
        ["Printing"] = "Message.Printing",
        ["Service stopped"] = "Message.ServiceStopped",
        ["Printer did not answer"] = "Message.PrinterDidNotAnswer",
        ["Printer is in use by another application"] = "Message.PrinterBusy",
    };

    private const string AttentionPrefix = "Printer needs attention: ";
    private const string UnavailablePrefix = "Printer not available: ";

    /// <summary>The job state ("Completed") in the interface language.</summary>
    public static string State(string state) =>
        States.Contains(state) ? Strings.Get("Job.State." + state) : state;

    /// <summary>The job message ("Printer did not answer") in the interface language.</summary>
    public static string Message(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return "";
        if (Messages.TryGetValue(message, out var key))
            return Strings.Get(key);
        if (message.StartsWith(AttentionPrefix, StringComparison.Ordinal))
            return Capitalize(AlarmList(message[AttentionPrefix.Length..]));
        if (message.StartsWith(UnavailablePrefix, StringComparison.Ordinal))
            return Strings.Get("Message.PrinterUnavailable", message[UnavailablePrefix.Length..]);
        return message;
    }

    /// <summary>"OutOfPaper, LowBattery" → "sin papel, batería baja" (unknown names stay).</summary>
    public static string AlarmList(string alarms) =>
        string.Join(", ", alarms.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).Select(Alarm));

    /// <summary>One alarm of the printer in the interface language.</summary>
    public static string Alarm(string alarm) => alarm switch
    {
        "OutOfPaper" or "Overheated" or "LowBattery" => Strings.Get("Alarm." + alarm),
        _ => alarm,
    };

    /// <summary>An alarm as shown in notices: the printer cannot tell an open cover from an empty roll, so it says both.</summary>
    public static string AlarmNotice(string alarm) =>
        alarm == "OutOfPaper" ? Strings.Get("Alarm.OutOfPaper.Notice") : Alarm(alarm);

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], System.Globalization.CultureInfo.CurrentCulture) + text[1..];
}
