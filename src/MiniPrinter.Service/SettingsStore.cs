using System.Text.Json;
using MiniPrinter.Control;

namespace MiniPrinter.Service;

/// <summary>Loads and saves <see cref="ServiceSettings"/> as JSON in the data directory.</summary>
public sealed class SettingsStore
{
    private readonly string _path;
    private readonly ILogger<SettingsStore> _logger;
    private readonly object _gate = new();
    private ServiceSettings _current;

    public SettingsStore(ServicePaths paths, ILogger<SettingsStore> logger)
    {
        _path = Path.Combine(paths.DataDirectory, "settings.json");
        _logger = logger;
        _current = Load();
    }

    public ServiceSettings Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>Raised after settings change, with (old, new).</summary>
    public event Action<ServiceSettings, ServiceSettings>? Changed;

    public ServiceSettings Update(Func<ServiceSettings, ServiceSettings> change)
    {
        ServiceSettings previous, updated;
        lock (_gate)
        {
            previous = _current;
            updated = Validate(change(_current));
            // Same sizes as before: keep the same list instance so record equality does not see a change.
            if (PaperCatalog.SameList(updated.PaperSizes, previous.PaperSizes))
                updated = updated with { PaperSizes = previous.PaperSizes };
            _current = updated;
            Save(updated);
        }
        if (previous != updated)
            Changed?.Invoke(previous, updated);
        return updated;
    }

    public static ServiceSettings Validate(ServiceSettings s) => s with
    {
        Darkness = Math.Clamp(s.Darkness, 1, 5),
        FeedPadding = Math.Clamp(s.FeedPadding, 0, 255),
        ExtraFeedSteps = Math.Clamp(s.ExtraFeedSteps, 0, 10),
        IdleTimeoutSeconds = Math.Clamp(s.IdleTimeoutSeconds, 10, 3600),
        IppPort = s.IppPort is > 0 and < 65536 ? s.IppPort : 8631,
        PrinterName = string.IsNullOrWhiteSpace(s.PrinterName) ? "X5h Thermal Printer" : s.PrinterName.Trim(),
        JobRetryMinutes = Math.Clamp(s.JobRetryMinutes, 0, 1440),
        PageGapMm = Math.Clamp(s.PageGapMm, 0, 50),
        LowBatteryPercent = Math.Clamp(s.LowBatteryPercent, 5, 50),
        TextFont = string.IsNullOrWhiteSpace(s.TextFont) ? "Segoe UI" : s.TextFont.Trim(),
        TextSizePt = Math.Clamp(s.TextSizePt, 6, 48),
        KeepAliveIntervalSeconds = Math.Clamp(s.KeepAliveIntervalSeconds, 10, 300),
        PrinterUuid = Guid.TryParse(s.PrinterUuid, out var uuid) ? uuid.ToString() : null,
        PaperSizes = PaperCatalog.Normalize(s.PaperSizes),
        DefaultPaperId = PaperCatalog.DefaultId(s with { PaperSizes = PaperCatalog.Normalize(s.PaperSizes) }),
    };

    private ServiceSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return Validate(JsonSerializer.Deserialize<ServiceSettings>(File.ReadAllText(_path), ControlDefaults.Json) ?? new());
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Ignoring unreadable settings file {Path}", _path);
        }
        return new ServiceSettings();
    }

    private void Save(ServiceSettings settings)
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions(ControlDefaults.Json) { WriteIndented = true }));
        File.Move(temp, _path, overwrite: true);
    }
}

/// <summary>Resolved data directory (overridable with MINIPRINTER_DATA for development and tests).</summary>
public sealed class ServicePaths
{
    public ServicePaths(string? overrideDirectory = null)
    {
        DataDirectory = overrideDirectory
                        ?? Environment.GetEnvironmentVariable("MINIPRINTER_DATA")
                        ?? ControlDefaults.DataDirectory;
        Directory.CreateDirectory(DataDirectory);
    }

    public string DataDirectory { get; }
    public string TokenPath => Path.Combine(DataDirectory, "control.token");
}
