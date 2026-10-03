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

    /// <summary>Listens on the RAW/JetDirect port (default 9100) for ESC/POS tickets, images, PDFs and text. Off by default.</summary>
    public bool RawPortEnabled { get; init; }

    public int RawPort { get; init; } = 9100;
    public string PrinterName { get; init; } = "X5h Thermal Printer";

    /// <summary>Minutes a job waits for an unavailable printer before it is aborted.</summary>
    public int JobRetryMinutes { get; init; } = 10;

    /// <summary>How many finished jobs keep their pages, to show them again and print them again (0 to 50; 0 keeps none).</summary>
    public int JobHistoryKeep { get; init; } = 10;

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

    /// <summary>
    /// Paper sizes offered to Windows (presets and the user's own). Null means the seven factory sizes, all
    /// enabled, as before this setting existed.
    /// </summary>
    public IReadOnlyList<PaperSizeSetting>? PaperSizes { get; init; }

    /// <summary>Id of the default size; when missing or disabled the first enabled size is used.</summary>
    public string? DefaultPaperId { get; init; }

    /// <summary>
    /// The `printer-uuid` announced over IPP. Null keeps the built-in one. Windows refuses a second queue for a printer
    /// with the same UUID, so recreating the queue gives the service a new one first (see WindowsQueue).
    /// </summary>
    public string? PrinterUuid { get; init; }
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
    long SizeBytes)
{
    /// <summary>Entrance of the job: Windows, Panel, Api or Raw (empty for services before 0.9).</summary>
    public string? Source { get; init; }

    /// <summary>The Windows user or the address of the client that sent it.</summary>
    public string? Origin { get; init; }

    /// <summary>Whether the service kept the pages and can print them again.</summary>
    public bool CanReprint { get; init; }
}

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

    /// <summary>The task that recreates the Windows print queue.</summary>
    public WindowsQueueTaskDto? WindowsQueue { get; init; }
    public string? LastErrorKind { get; init; }
    public DateTimeOffset? LastSeen { get; init; }
    public NetworkMode NetworkMode { get; init; }
    public IReadOnlyList<string> IppUrls { get; init; } = [];

    /// <summary>The RAW/JetDirect port (ESC/POS, images, PDF and text over TCP).</summary>
    public RawPortDto? RawPort { get; init; }
    public int QueuedJobs { get; init; }
    public string Version { get; init; } = "";
}

/// <param name="Block">1-based block of a template the error belongs to, when known.</param>
/// <param name="Property">Block property the error belongs to, when known.</param>
public sealed record ApiError(string Error, int? Block = null, string? Property = null);

/// <summary>Automation API state shown in the tray (GET/POST /api/automation…).</summary>
public sealed record AutomationInfo(bool Enabled, string Token, IReadOnlyList<string> Urls);

/// <summary>Template description as returned by GET /api/templates.</summary>
public sealed record TemplateFieldDto(string Name, string Label, string Kind, bool Required, IReadOnlyList<string>? Choices, string? Default);

/// <param name="Source">"builtin", "user" or "override" (a user template replacing a built-in).</param>
public sealed record TemplateDto(string Name, string Title, string Description, IReadOnlyList<TemplateFieldDto> Fields, bool IsImage, string Source = "builtin");

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

/// <summary>One property of a template block, as published by GET /api/templates/schema.</summary>
public sealed record SchemaPropDto(string Name, string Label, string Kind, double? Min, double? Max, double? Default,
    IReadOnlyList<string>? Values, bool Multiline);

public sealed record SchemaBlockDto(string Type, string Title, IReadOnlyList<SchemaPropDto> Props);

public sealed record SchemaTemplatePropsDto(int MaxBlocks, int MaxJsonKb, int MinGap, int MaxGap, int DefaultGap, IReadOnlyList<string> Modes);

/// <summary>What the template editor needs to build its controls (GET /api/templates/schema).</summary>
public sealed record TemplateSchemaDto(
    IReadOnlyList<SchemaBlockDto> Blocks,
    IReadOnlyList<SchemaPropDto> Common,
    IReadOnlyList<string> FieldKinds,
    IReadOnlyList<string> Filters,
    SchemaTemplatePropsDto Template);

/// <summary>Rows (of the preview PNG) that one block of a template occupies.</summary>
public sealed record BlockRowsDto(int Index, string Type, int Top, int Height);

/// <summary>A template preview: the PNG and the rows of each block (from the X-Template-Blocks header).</summary>
public sealed record TemplatePreviewDto(byte[] Png, IReadOnlyList<BlockRowsDto> Blocks);

public sealed record DraftDto(string Id);

/// <summary>State of the task that recreates the Windows print queue (State: Idle, Running, Succeeded or Failed).</summary>
/// <param name="NeedsElevation">The task failed for lack of administrator rights: it can be repeated elevated (UAC).</param>
public sealed record WindowsQueueTaskDto(string State, string? Message, string? Step, bool NeedsElevation = false);

/// <summary>GET /api/windows-queue: whether the queue exists in Windows plus the task state.</summary>
public sealed record WindowsQueueDto(bool Exists, string Name, string State, string? Message, string? Step, bool NeedsElevation = false);

/// <summary>Body of POST /api/windows-queue/recreate; <c>PreviousName</c> is the queue to replace after a rename.</summary>
public sealed record RecreateQueueRequest(string? PreviousName = null);

/// <summary>A parameter accepted when printing a template that is not one of its fields (copies, rows, darkness).</summary>
public sealed record TemplateParameterDto(string Name, string Description, string Kind, double? Min, double? Max);

/// <summary>What GET /api/templates/{name}/fields returns: the fields of one template and the print parameters.</summary>
public sealed record TemplateFieldsDto(string Name, string Title, string Source, IReadOnlyList<TemplateFieldDto> Fields, IReadOnlyList<TemplateParameterDto> PrintParameters);

/// <summary>The parameters of <c>POST /print/template/{name}</c> besides the template's own fields.</summary>
public static class TemplatePrintParameters
{
    public static IReadOnlyList<TemplateParameterDto> All { get; } =
    [
        new("copies", "Copias de cada etiqueta (1 a 50).", "int", 1, 50),
        new("rows", "Una etiqueta por fila: lista de hasta 200 objetos con los campos de cada etiqueta (sobrescriben los campos base).", "list", 1, 200),
        new("darkness", "Oscuridad de la impresión (1 a 5); si falta se usa la de los ajustes.", "int", 1, 5),
    ];
}

/// <summary>State of the RAW/JetDirect listener (port 9100) as shown in the tray.</summary>
/// <param name="Listening">The port is open and accepting connections.</param>
/// <param name="Error">Why it is not listening although enabled (port in use, ...).</param>
/// <param name="Addresses">host:port pairs clients can use in the current network mode.</param>
/// <summary>The result of a check (GET /api/diagnostics).</summary>
public static class DiagnosticStatus
{
    public const string Ok = "ok";
    public const string Warn = "warn";
    public const string Fail = "fail";

    /// <summary>The check could not be completed: the window does not accuse what it could not look at.</summary>
    public const string Unknown = "unknown";
}

/// <summary>The names of the checks (what to do about each is text of the interface, not of the service).</summary>
public static class DiagnosticIds
{
    public const string Service = "service";
    public const string Bluetooth = "bluetooth";
    public const string Paired = "paired";
    public const string Link = "link";
    public const string Paper = "paper";
    public const string Queue = "queue";
    public const string Ipp = "ipp";
    public const string Raw = "raw";
    public const string Fonts = "fonts";
}

/// <param name="Id">One of <see cref="DiagnosticIds"/>.</param>
/// <param name="Status">One of <see cref="DiagnosticStatus"/>.</param>
/// <param name="Detail">A fact worth showing next to the result (an error, an address, a name), or null.</param>
public sealed record DiagnosticCheckDto(string Id, string Status, string? Detail);

/// <summary>How a connection to the RAW port ended.</summary>
public static class RawClientResult
{
    public const string Queued = "queued";
    public const string Rejected = "rejected";
    public const string ClosedByLimit = "closed-by-limit";
    public const string RefusedLimit = "refused-limit";
    public const string Empty = "empty";
    public const string Error = "error";
}

/// <summary>One recent client of the RAW port (GET /api/raw-port/clients): who, what it sent, how much and what became of it.</summary>
/// <param name="Kind">ESC/POS, PNG, JPEG, PDF, PWG, text or unknown.</param>
/// <param name="Tickets">Tickets queued (ESC/POS connections).</param>
/// <param name="JobId">The last job queued by the connection, if any.</param>
/// <param name="Result">One of <see cref="RawClientResult"/>.</param>
public sealed record RawClientDto(DateTimeOffset Time, string Address, string Kind, long Bytes, int Tickets, int? JobId, string Result);

public sealed record RawPortDto(bool Enabled, int Port, bool Listening, string? Error, IReadOnlyList<string> Addresses);
