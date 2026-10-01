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
