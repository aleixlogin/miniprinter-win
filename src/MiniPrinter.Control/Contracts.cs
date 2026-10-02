using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniPrinter.Control;

public enum NetworkMode
{
    /// <summary>IPP only on 127.0.0.1: printable from this PC.</summary>
    Local,
    /// <summary>IPP on all interfaces, advertised over mDNS: printable from the LAN.</summary>
    Lan,
}

public enum TransportChoice
{
    Rfcomm,
    Serial,
    Simulated,
}

/// <summary>Printer mode per page: automatic (text pages in text mode) or forced.</summary>
public enum PrintModeChoice
{
    Auto,
    Image,
    Text,
}

/// <summary>How to interpret byte 2 of the A3 reply (battery).</summary>
public enum BatteryUnit
{
    /// <summary>Show the raw value; no percentage or low-battery warning.</summary>
    Unknown,
    /// <summary>The value is a percentage.</summary>
    Percent,
    /// <summary>The value is the cell voltage in tenths of a volt.</summary>
    Decivolts,
}

public enum DitherChoice
{
    Auto,
    Atkinson,
    FloydSteinberg,
    Threshold,
}

/// <summary>The printer the service drives.</summary>
public sealed record PrinterSelection
{
    public required string Name { get; init; }
    public required string Address { get; init; }
    public string ProfileKey { get; init; } = "d1";
    public TransportChoice Transport { get; init; } = TransportChoice.Rfcomm;

    /// <summary>Explicit COM port for <see cref="TransportChoice.Serial"/>; looked up from the address when null.</summary>
    public string? Port { get; init; }
}

/// <summary>User-editable service settings, persisted by the service.</summary>
public sealed record ServiceSettings
{
    public PrinterSelection? Printer { get; init; }
    public int Darkness { get; init; } = 3;
    public DitherChoice Dither { get; init; } = DitherChoice.Auto;
    public int FeedPadding { get; init; } = 12;
    public int ExtraFeedSteps { get; init; }
    public int IdleTimeoutSeconds { get; init; } = 60;
    public NetworkMode NetworkMode { get; init; } = NetworkMode.Local;
    public int IppPort { get; init; } = 8631;
    public string PrinterName { get; init; } = "X5h Thermal Printer";

    /// <summary>Minutes a job waits for an unavailable printer before it is aborted.</summary>
    public int JobRetryMinutes { get; init; } = 10;

    public PrintModeChoice PrintMode { get; init; } = PrintModeChoice.Auto;

    /// <summary>Print the pages of a job as one continuous strip (no paper advance between pages).</summary>
    public bool ContinuousPages { get; init; }

    /// <summary>Gap between pages in continuous mode, in millimetres.</summary>
    public int PageGapMm { get; init; } = 4;

    public BatteryUnit BatteryUnit { get; init; } = BatteryUnit.Unknown;

    /// <summary>Low-battery warning threshold, in percent.</summary>
    public int LowBatteryPercent { get; init; } = 20;

    /// <summary>Enables the /api/v1 automation endpoints on the IPP listener.</summary>
    public bool AutomationApiEnabled { get; init; }

    public string TextFont { get; init; } = "Segoe UI";

    public int TextSizePt { get; init; } = 10;

    /// <summary>Keep the printer connected and send an A3 heartbeat (no idle disconnect).</summary>
    public bool KeepAlive { get; init; }

    /// <summary>Heartbeat interval in keep-alive mode, in seconds (10–300).</summary>
    public int KeepAliveIntervalSeconds { get; init; } = 30;
}

public sealed record JobDto(
    int Id,
    string Name,
    string User,
    string State,
    string? Message,
    DateTimeOffset Created,
    DateTimeOffset? Completed,
    int Pages,
    long SizeBytes);

public sealed record StatusDto
{
    public string Link { get; init; } = "Disconnected";
    public PrinterSelection? Printer { get; init; }
    public bool Ready { get; init; }
    public int? AlarmByte { get; init; }
    public IReadOnlyList<string> Alarms { get; init; } = [];
    /// <summary>Raw battery indicator from <c>A3</c> byte 2 (unit unconfirmed: % or tenths of a volt).</summary>
    public int? BatteryLevel { get; init; }

    /// <summary>Raw paper sensor reading from <c>A3</c> byte 1 (≈15 with paper, ≈27 without).</summary>
    public int? PaperSensor { get; init; }

    /// <summary>Battery percentage per the configured unit (null when the unit is unknown).</summary>
    public int? BatteryPercent { get; init; }

    public BatteryUnit BatteryUnit { get; init; }

    public bool LowBattery { get; init; }

    /// <summary>End of the current battery sampling period, if any.</summary>
    public DateTimeOffset? SamplingUntil { get; init; }

    /// <summary>Keep-alive (persistent connection) is active.</summary>
    public bool KeepAlive { get; init; }

    /// <summary>Keep-alive lost the link and is retrying.</summary>
    public bool Reconnecting { get; init; }
    public string? Firmware { get; init; }
    public bool Printing { get; init; }
    public string? LastError { get; init; }
    public string? LastErrorKind { get; init; }
    public DateTimeOffset? LastSeen { get; init; }
    public NetworkMode NetworkMode { get; init; }
    public IReadOnlyList<string> IppUrls { get; init; } = [];
    public int QueuedJobs { get; init; }
    public string Version { get; init; } = "";
}

public sealed record ApiError(string Error);

/// <summary>Automation API state shown in the tray (GET/POST /api/automation…).</summary>
public sealed record AutomationInfo(bool Enabled, string Token, IReadOnlyList<string> Urls);

/// <summary>Template description as returned by GET /api/templates.</summary>
public sealed record TemplateFieldDto(string Name, string Label, string Kind, bool Required, IReadOnlyList<string>? Choices, string? Default);

public sealed record TemplateDto(string Name, string Title, string Description, IReadOnlyList<TemplateFieldDto> Fields, bool IsImage);

public static class ControlDefaults
{
    public const int Port = 8632;
    public const string TokenHeader = "X-MiniPrinter-Token";

    /// <summary>
    /// %ProgramData%\MiniPrinter, shared by the service and the tray app (overridable with the
    /// MINIPRINTER_DATA environment variable for development).
    /// </summary>
    public static string DataDirectory =>
        Environment.GetEnvironmentVariable("MINIPRINTER_DATA")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MiniPrinter");

    public static string TokenPath => Path.Combine(DataDirectory, "control.token");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };
}
