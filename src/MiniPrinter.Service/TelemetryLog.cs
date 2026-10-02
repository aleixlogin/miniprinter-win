using System.Globalization;
using MiniPrinter.Protocol;
using MiniPrinter.Transport;

namespace MiniPrinter.Service;

/// <summary>
/// Appends every <c>A3</c> reply to a daily CSV (<c>status-YYYYMMDD.csv</c>) so the battery byte
/// can be analysed over a discharge. Rotates at <see cref="MaxFileBytes"/> and keeps
/// <see cref="RetentionDays"/> days.
/// </summary>
public sealed class TelemetryLog
{
    public long MaxFileBytes { get; init; } = 5 * 1024 * 1024;
    public const int RetentionDays = 30;
    public const string Header = "timestamp,link,alarm,paperSensor,battery";

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<TelemetryLog> _logger;
    private DateOnly _lastCleanup;

    public TelemetryLog(ServicePaths paths, ILogger<TelemetryLog> logger, PrinterManager? printer = null, Func<DateTimeOffset>? clock = null)
    {
        Directory = Path.Combine(paths.DataDirectory, "telemetry");
        System.IO.Directory.CreateDirectory(Directory);
        _logger = logger;
        _clock = clock ?? (() => DateTimeOffset.Now);
        if (printer is not null)
            printer.MessageReceived += m =>
            {
                if (m is DeviceState state)
                    Record(state, printer.Status.Link);
            };
        Cleanup();
    }

    public string Directory { get; }

    public void Record(DeviceState state, LinkState link)
    {
        var now = _clock();
        var line = string.Join(',',
            now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            link,
            state.AlarmByte.ToString(CultureInfo.InvariantCulture),
            state.PaperSensor?.ToString(CultureInfo.InvariantCulture) ?? "",
            state.BatteryLevel?.ToString(CultureInfo.InvariantCulture) ?? "");
        lock (_gate)
        {
            try
            {
                var path = CurrentFile(now);
                if (!File.Exists(path))
                    File.WriteAllText(path, Header + Environment.NewLine);
                File.AppendAllText(path, line + Environment.NewLine);
                if (DateOnly.FromDateTime(now.DateTime) != _lastCleanup)
                    Cleanup();
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Could not write telemetry");
            }
        }
    }

    /// <summary>All retained rows, oldest first, with a single header.</summary>
    public string Export()
    {
        lock (_gate)
        {
            var writer = new StringWriter();
            writer.WriteLine(Header);
            foreach (var file in Files())
                foreach (var row in File.ReadLines(file).Skip(1))
                    writer.WriteLine(row);
            return writer.ToString();
        }
    }

    private IEnumerable<string> Files() =>
        System.IO.Directory.GetFiles(Directory, "status-*.csv").OrderBy(f => f, StringComparer.Ordinal);

    /// <summary>Today's file, or the next numbered part when it grew past the size limit.</summary>
    private string CurrentFile(DateTimeOffset now)
    {
        var stem = $"status-{now:yyyyMMdd}";
        var path = Path.Combine(Directory, stem + ".csv");
        var part = 1;
        while (File.Exists(path) && new FileInfo(path).Length >= MaxFileBytes)
            path = Path.Combine(Directory, $"{stem}-{part++:D2}.csv");
        return path;
    }

    private void Cleanup()
    {
        var now = _clock();
        _lastCleanup = DateOnly.FromDateTime(now.DateTime);
        var cutoff = now.AddDays(-RetentionDays).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        foreach (var file in Files())
        {
            var date = Path.GetFileNameWithoutExtension(file)["status-".Length..];
            if (date.Length >= 8 && string.CompareOrdinal(date[..8], cutoff) < 0)
            {
                try { File.Delete(file); }
                catch (IOException ex) { _logger.LogDebug(ex, "Could not delete {File}", file); }
            }
        }
    }
}
