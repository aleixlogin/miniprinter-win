using System.Globalization;
using MiniPrinter.Control;

namespace MiniPrinter.Gui;

/// <summary>Printing: quality, text, continuous pages, connection keep-alive and the history of jobs.</summary>
public sealed class PrintSectionViewModel : SettingsSectionViewModel
{
    private int _darkness = 3;
    private PrintModeChoice _printMode;
    private DitherChoice _dither;
    private int _extraFeedSteps;
    private bool _continuousPages;
    private string _pageGapMm = "";
    private string _textFont = "";
    private string _textSizePt = "";
    private bool _keepAlive;
    private string _keepAliveSeconds = "";
    private string _idleSeconds = "";
    private string _retryMinutes = "";
    private string _historyKeep = "";

    public PrintSectionViewModel() : base("Print", "Settings.Section.Print",
    [
        new("Darkness", "Main.Oscuridad", "Settings.Terms.Darkness"),
        new("PrintMode", "Main.ModoDeImpresion", "Settings.Terms.PrintMode"),
        new("Dither", "Main.Tramado", "Settings.Terms.Dither"),
        new("ExtraFeed", "Main.AvanceExtraAlFinal", "Settings.Terms.ExtraFeed"),
        new("Continuous", "Main.PaginasContinuas", "Settings.Terms.Continuous"),
        new("TextFont", "Settings.Print.TextFont", "Settings.Terms.TextFont"),
        new("TextSize", "Settings.Print.TextSize", "Settings.Terms.TextSize"),
        new("KeepAlive", "Main.MantenerActiva", "Settings.Terms.KeepAlive"),
        new("Idle", "Main.DesconectarTras", "Settings.Terms.Idle"),
        new("Retry", "Settings.Print.Retry", "Settings.Terms.Retry"),
        new("History", "Settings.Print.History", "Settings.Terms.History"),
    ],
    [
        nameof(ServiceSettings.Darkness), nameof(ServiceSettings.PrintMode), nameof(ServiceSettings.Dither),
        nameof(ServiceSettings.ExtraFeedSteps), nameof(ServiceSettings.ContinuousPages), nameof(ServiceSettings.PageGapMm),
        nameof(ServiceSettings.TextFont), nameof(ServiceSettings.TextSizePt), nameof(ServiceSettings.KeepAlive),
        nameof(ServiceSettings.KeepAliveIntervalSeconds), nameof(ServiceSettings.IdleTimeoutSeconds),
        nameof(ServiceSettings.JobRetryMinutes), nameof(ServiceSettings.JobHistoryKeep),
    ])
    {
    }

    public int Darkness { get => _darkness; set => Field(ref _darkness, value); }
    public PrintModeChoice PrintMode { get => _printMode; set => Field(ref _printMode, value); }
    public DitherChoice Dither { get => _dither; set => Field(ref _dither, value); }
    public int ExtraFeedSteps { get => _extraFeedSteps; set => Field(ref _extraFeedSteps, value); }
    public bool ContinuousPages { get => _continuousPages; set => Field(ref _continuousPages, value); }
    public string PageGapMm { get => _pageGapMm; set => Field(ref _pageGapMm, value); }
    public string TextFont { get => _textFont; set => Field(ref _textFont, value); }
    public string TextSizePt { get => _textSizePt; set => Field(ref _textSizePt, value); }
    public bool KeepAlive { get => _keepAlive; set => Field(ref _keepAlive, value); }
    public string KeepAliveSeconds { get => _keepAliveSeconds; set => Field(ref _keepAliveSeconds, value); }
    public string IdleSeconds { get => _idleSeconds; set => Field(ref _idleSeconds, value); }
    public string RetryMinutes { get => _retryMinutes; set => Field(ref _retryMinutes, value); }
    public string HistoryKeep { get => _historyKeep; set => Field(ref _historyKeep, value); }

    /// <summary>The idle timeout only matters when the connection is not kept alive.</summary>
    public bool IdleApplies => !_keepAlive;

    /// <summary>The history keeps what was printed on this PC; the text says so next to the setting.</summary>
    public bool KeepsHistory => TryNumber(_historyKeep, 0, 50, out var keep) && keep > 0;

    protected override string[] Values(ServiceSettings s) =>
    [
        Number(s.Darkness), s.PrintMode.ToString(), s.Dither.ToString(), Number(s.ExtraFeedSteps), s.ContinuousPages.ToString(),
        Number(s.PageGapMm), s.TextFont, Number(s.TextSizePt), s.KeepAlive.ToString(), Number(s.KeepAliveIntervalSeconds),
        Number(s.IdleTimeoutSeconds), Number(s.JobRetryMinutes), Number(s.JobHistoryKeep),
    ];

    protected override string[] Snapshot() =>
    [
        Number(_darkness), _printMode.ToString(), _dither.ToString(), Number(_extraFeedSteps), _continuousPages.ToString(),
        _pageGapMm.Trim(), _textFont.Trim(), _textSizePt.Trim(), _keepAlive.ToString(), _keepAliveSeconds.Trim(),
        _idleSeconds.Trim(), _retryMinutes.Trim(), _historyKeep.Trim(),
    ];

    protected override void Read(ServiceSettings s)
    {
        Darkness = s.Darkness;
        PrintMode = s.PrintMode;
        Dither = s.Dither;
        ExtraFeedSteps = s.ExtraFeedSteps;
        ContinuousPages = s.ContinuousPages;
        PageGapMm = Number(s.PageGapMm);
        TextFont = s.TextFont;
        TextSizePt = Number(s.TextSizePt);
        KeepAlive = s.KeepAlive;
        KeepAliveSeconds = Number(s.KeepAliveIntervalSeconds);
        IdleSeconds = Number(s.IdleTimeoutSeconds);
        RetryMinutes = Number(s.JobRetryMinutes);
        HistoryKeep = Number(s.JobHistoryKeep);
    }

    public override ServiceSettings Write(ServiceSettings s) => s with
    {
        Darkness = _darkness,
        PrintMode = _printMode,
        Dither = _dither,
        ExtraFeedSteps = _extraFeedSteps,
        ContinuousPages = _continuousPages,
        PageGapMm = int.Parse(_pageGapMm.Trim(), CultureInfo.InvariantCulture),
        TextFont = _textFont.Trim(),
        TextSizePt = int.Parse(_textSizePt.Trim(), CultureInfo.InvariantCulture),
        KeepAlive = _keepAlive,
        KeepAliveIntervalSeconds = int.Parse(_keepAliveSeconds.Trim(), CultureInfo.InvariantCulture),
        IdleTimeoutSeconds = int.Parse(_idleSeconds.Trim(), CultureInfo.InvariantCulture),
        JobRetryMinutes = int.Parse(_retryMinutes.Trim(), CultureInfo.InvariantCulture),
        JobHistoryKeep = int.Parse(_historyKeep.Trim(), CultureInfo.InvariantCulture),
    };

    protected override void Check(Action<string, string> report)
    {
        CheckNumber(report, "PageGap", _pageGapMm, 0, 50);
        CheckNumber(report, "TextSize", _textSizePt, 6, 48);
        CheckNumber(report, "KeepAliveSeconds", _keepAliveSeconds, 10, 300);
        CheckNumber(report, "Idle", _idleSeconds, 10, 3600);
        CheckNumber(report, "Retry", _retryMinutes, 0, 1440);
        CheckNumber(report, "History", _historyKeep, 0, 50);
        if (string.IsNullOrWhiteSpace(_textFont))
            report("TextFont", Strings.Get("Settings.Error.Empty"));
    }
}

/// <summary>Battery: how to read the value the printer reports and when to warn.</summary>
public sealed class BatterySectionViewModel : SettingsSectionViewModel
{
    private BatteryUnit _unit;
    private string _lowPercent = "";

    public BatterySectionViewModel() : base("Battery", "Settings.Section.Battery",
    [
        new("Unit", "Main.UnidadDelValor", "Settings.Terms.Unit"),
        new("Low", "Main.AvisarPorDebajoDe", "Settings.Terms.Low"),
    ],
    [nameof(ServiceSettings.BatteryUnit), nameof(ServiceSettings.LowBatteryPercent)])
    {
    }

    public BatteryUnit Unit { get => _unit; set => Field(ref _unit, value); }
    public string LowPercent { get => _lowPercent; set => Field(ref _lowPercent, value); }

    /// <summary>The warning needs a known unit.</summary>
    public bool LowApplies => _unit != BatteryUnit.Unknown;

    protected override string[] Values(ServiceSettings s) => [s.BatteryUnit.ToString(), Number(s.LowBatteryPercent)];

    protected override string[] Snapshot() => [_unit.ToString(), _lowPercent.Trim()];

    protected override void Read(ServiceSettings s)
    {
        Unit = s.BatteryUnit;
        LowPercent = Number(s.LowBatteryPercent);
    }

    public override ServiceSettings Write(ServiceSettings s) => s with
    {
        BatteryUnit = _unit,
        LowBatteryPercent = int.Parse(_lowPercent.Trim(), CultureInfo.InvariantCulture),
    };

    protected override void Check(Action<string, string> report) => CheckNumber(report, "Low", _lowPercent, 5, 50);
}

/// <summary>Paper: the name of the printer in Windows and the paper sizes it offers (edited by the paper list of the view).</summary>
public sealed class PaperSectionViewModel : SettingsSectionViewModel
{
    private string _printerName = "";
    private IReadOnlyList<PaperSizeSetting> _sizes = PaperCatalog.Presets;
    private string _defaultId = PaperCatalog.DefaultPresetId;
    private string _queueStatus = "";
    private bool _queueStatusIsProblem;

    public PaperSectionViewModel() : base("Paper", "Settings.Section.Paper",
    [
        new("Name", "Main.NombreEnWindows", "Settings.Terms.Name"),
        new("Sizes", "Main.PapelQueSeMuestraA", "Settings.Terms.Sizes"),
        new("Queue", "Main.ImpresoraDeWindows", "Settings.Terms.Queue"),
    ],
    [nameof(ServiceSettings.PrinterName), nameof(ServiceSettings.PaperSizes), nameof(ServiceSettings.DefaultPaperId)])
    {
    }

    public string PrinterName { get => _printerName; set => Field(ref _printerName, value); }

    public IReadOnlyList<PaperSizeSetting> Sizes => _sizes;

    public string DefaultId => _defaultId;

    /// <summary>Set by the paper list when the user adds, edits, removes or switches sizes.</summary>
    public void SetSizes(IReadOnlyList<PaperSizeSetting> sizes, string defaultId)
    {
        _sizes = [.. sizes];
        _defaultId = defaultId;
        Touch();
    }

    /// <summary>Whether the Windows printer exists and is up to date (shown under the recreate button).</summary>
    public string QueueStatus
    {
        get => _queueStatus;
        set => Set(ref _queueStatus, value);
    }

    public bool QueueStatusIsProblem
    {
        get => _queueStatusIsProblem;
        set => Set(ref _queueStatusIsProblem, value);
    }

    /// <summary>Recreates the printer in Windows (set by the window, which owns that long procedure).</summary>
    public AsyncCommand? RecreateQueue { get; set; }

    private static string Describe(IReadOnlyList<PaperSizeSetting> sizes, string defaultId) =>
        string.Join(";", sizes.Select(p => $"{p.Id}|{p.Name}|{p.WidthMm}x{p.LengthMm}|{p.Enabled}")) + "#" + defaultId;

    private static string Describe(ServiceSettings s) => Describe(PaperCatalog.All(s), PaperCatalog.DefaultId(s));

    protected override string[] Values(ServiceSettings s) => [s.PrinterName.Trim(), Describe(s)];

    protected override string[] Snapshot() => [_printerName.Trim(), Describe(_sizes, _defaultId)];

    protected override void Read(ServiceSettings s)
    {
        PrinterName = s.PrinterName;
        _sizes = [.. PaperCatalog.All(s)];
        _defaultId = PaperCatalog.DefaultId(s);
        OnPropertyChanged(nameof(Sizes));
        OnPropertyChanged(nameof(DefaultId));
    }

    public override ServiceSettings Write(ServiceSettings s) => s with
    {
        PrinterName = _printerName.Trim(),
        PaperSizes = _sizes.SequenceEqual(PaperCatalog.Presets) ? null : _sizes,
        DefaultPaperId = _defaultId,
    };

    protected override void Check(Action<string, string> report)
    {
        if (string.IsNullOrWhiteSpace(_printerName))
            report("Name", Strings.Get("Settings.Error.Empty"));
        if (PaperCatalog.Problem(_sizes) is { } problem)
            report("Sizes", problem);
    }
}

/// <summary>Network: who can print through the Windows printer (IPP) and on which port.</summary>
public sealed class NetworkSectionViewModel : SettingsSectionViewModel
{
    private NetworkMode _mode;
    private string _port = "";

    public NetworkSectionViewModel() : base("Network", "Settings.Section.Network",
    [
        new("Scope", "Main.ImprimirDesde", "Settings.Terms.Scope"),
        new("Port", "Main.PuertoIpp", "Settings.Terms.IppPort"),
    ],
    [nameof(ServiceSettings.NetworkMode), nameof(ServiceSettings.IppPort)])
    {
    }

    public NetworkMode Mode { get => _mode; set => Field(ref _mode, value); }

    public bool IsLan
    {
        get => _mode == NetworkMode.Lan;
        set => Mode = value ? NetworkMode.Lan : NetworkMode.Local;
    }

    public bool IsLocal
    {
        get => _mode == NetworkMode.Local;
        set => Mode = value ? NetworkMode.Local : NetworkMode.Lan;
    }

    public string Port { get => _port; set => Field(ref _port, value); }

    protected override string[] Values(ServiceSettings s) => [s.NetworkMode.ToString(), Number(s.IppPort)];

    protected override string[] Snapshot() => [_mode.ToString(), _port.Trim()];

    protected override void Read(ServiceSettings s)
    {
        Mode = s.NetworkMode;
        Port = Number(s.IppPort);
    }

    public override ServiceSettings Write(ServiceSettings s) => s with
    {
        NetworkMode = _mode,
        IppPort = int.Parse(_port.Trim(), CultureInfo.InvariantCulture),
    };

    protected override void Check(Action<string, string> report) => CheckNumber(report, "Port", _port, 1, 65535);
}

/// <summary>Direct printing: the RAW port 9100.</summary>
public sealed class RawPortSectionViewModel : SettingsSectionViewModel
{
    private bool _enabled;
    private string _port = "";
    private string _status = "";
    private RawPortState _state = RawPortState.Unknown;

    public RawPortSectionViewModel() : base("Raw", "Settings.Section.Raw",
    [
        new("Enabled", "Main.Estado", "Settings.Terms.RawEnabled"),
        new("Port", "Main.Puerto", "Settings.Terms.RawPort"),
        new("Status", "Main.Situacion", "Settings.Terms.RawStatus"),
    ],
    [nameof(ServiceSettings.RawPortEnabled), nameof(ServiceSettings.RawPort)])
    {
    }

    public bool Enabled { get => _enabled; set => Field(ref _enabled, value); }
    public string Port { get => _port; set => Field(ref _port, value); }

    /// <summary>The addresses to copy, the QR code, the test ticket and the recent clients.</summary>
    public RawPortToolsViewModel Tools { get; } = new();

    /// <summary>What the service says about the port: off, listening on some addresses or why it did not open.</summary>
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public RawPortState State
    {
        get => _state;
        private set => Set(ref _state, value);
    }

    /// <summary>Shows the state reported by the service.</summary>
    public void ShowStatus(RawPortDto? raw, NetworkMode mode = NetworkMode.Local)
    {
        Tools.Update(raw, mode);
        (State, Status) = raw switch
        {
            null => (RawPortState.Unknown, ""),
            { Enabled: false } => (RawPortState.Off, Strings.Get("Main.RawPortOff")),
            { Listening: true } => (RawPortState.Listening, Strings.Get("Main.RawPortListening", string.Join(", ", raw.Addresses))),
            { Error: { } error } => (RawPortState.Failed, error),
            _ => (RawPortState.Starting, Strings.Get("Main.RawPortStarting")),
        };
    }

    protected override string[] Values(ServiceSettings s) => [s.RawPortEnabled.ToString(), Number(s.RawPort)];

    protected override string[] Snapshot() => [_enabled.ToString(), _port.Trim()];

    protected override void Read(ServiceSettings s)
    {
        Enabled = s.RawPortEnabled;
        Port = Number(s.RawPort);
    }

    public override ServiceSettings Write(ServiceSettings s) => s with
    {
        RawPortEnabled = _enabled,
        RawPort = int.Parse(_port.Trim(), CultureInfo.InvariantCulture),
    };

    protected override void Check(Action<string, string> report) => CheckNumber(report, "Port", _port, 1024, 65535);
}

public enum RawPortState
{
    Unknown,
    Off,
    Starting,
    Listening,
    Failed,
}

/// <summary>Automation: the HTTP API for scripts and the token that protects it.</summary>
public sealed class AutomationSectionViewModel : SettingsSectionViewModel
{
    private bool _enabled;
    private string _token = "";
    private string _addresses = "";

    public AutomationSectionViewModel() : base("Automation", "Settings.Section.Automation",
    [
        new("Enabled", "Main.Estado", "Settings.Terms.AutomationEnabled"),
        new("Token", "Main.Token", "Settings.Terms.Token"),
        new("Addresses", "Main.Direcciones", "Settings.Terms.Addresses"),
    ],
    [nameof(ServiceSettings.AutomationApiEnabled)])
    {
    }

    public bool Enabled { get => _enabled; set => Field(ref _enabled, value); }

    public string Token
    {
        get => _token;
        private set => Set(ref _token, value);
    }

    /// <summary>The addresses of the API, or what to do to get them.</summary>
    public string Addresses
    {
        get => _addresses;
        private set => Set(ref _addresses, value);
    }

    /// <summary>Copies the token to the clipboard (set by the window).</summary>
    public RelayCommand? CopyToken { get; set; }

    /// <summary>Asks for a new token and shows it (set by <see cref="SettingsViewModel"/>).</summary>
    public AsyncCommand? RegenerateToken { get; set; }

    public void Show(AutomationInfo info)
    {
        Token = info.Token;
        Addresses = info.Enabled
            ? string.Join(Environment.NewLine, info.Urls) + Environment.NewLine + Strings.Get("Main.AutomationHeader")
            : Strings.Get("Main.AutomationOff");
    }

    public void ShowProblem(string message) => Addresses = message;

    protected override string[] Values(ServiceSettings s) => [s.AutomationApiEnabled.ToString()];

    protected override string[] Snapshot() => [_enabled.ToString()];

    protected override void Read(ServiceSettings s) => Enabled = s.AutomationApiEnabled;

    public override ServiceSettings Write(ServiceSettings s) => s with { AutomationApiEnabled = _enabled };

    protected override void Check(Action<string, string> report)
    {
    }
}

/// <summary>Appearance: theme and size of the text. They apply at once (nothing to save).</summary>
public sealed class AppearanceSectionViewModel : SettingsSectionViewModel
{
    private readonly Action<ThemeChoice> _setTheme;
    private readonly Action<TextSizeChoice> _setTextSize;
    private ThemeChoice _theme;
    private TextSizeChoice _textSize;

    public AppearanceSectionViewModel(ThemeChoice theme, TextSizeChoice textSize, Action<ThemeChoice> setTheme, Action<TextSizeChoice> setTextSize)
        : base("Appearance", "Settings.Section.Appearance",
        [
            new("Theme", "Appearance.Theme", "Settings.Terms.Theme"),
            new("TextSize", "Appearance.TextSize", "Settings.Terms.TextSizePanel"),
        ],
        [])
    {
        _theme = theme;
        _textSize = textSize;
        _setTheme = setTheme;
        _setTextSize = setTextSize;
    }

    public override bool IsInstant => true;

    public ThemeChoice Theme
    {
        get => _theme;
        set
        {
            if (Set(ref _theme, value))
                _setTheme(value);
        }
    }

    public TextSizeChoice TextSize
    {
        get => _textSize;
        set
        {
            if (Set(ref _textSize, value))
                _setTextSize(value);
        }
    }

    protected override string[] Values(ServiceSettings s) => [];

    protected override string[] Snapshot() => [];

    protected override void Read(ServiceSettings s)
    {
    }

    public override ServiceSettings Write(ServiceSettings s) => s;

    protected override void Check(Action<string, string> report)
    {
    }
}

/// <summary>General: automatic update checks and the hotkey of the quick note. They apply at once.</summary>
public sealed class GeneralSectionViewModel : SettingsSectionViewModel
{
    private readonly Action<bool> _setAutoUpdate;
    private readonly Func<string, string> _applyHotkey;
    private bool _autoUpdate;
    private string _hotkey;
    private string _hotkeyStatus;

    public GeneralSectionViewModel(bool autoUpdate, string hotkey, Action<bool> setAutoUpdate, Func<string, string> applyHotkey, Func<Task> checkUpdates)
        : base("General", "Settings.Section.General",
        [
            new("Updates", "Main.Comprobar", "Settings.Terms.Updates"),
            new("Hotkey", "Main.AtajoDeNotaRapida", "Settings.Terms.Hotkey"),
        ],
        [])
    {
        _autoUpdate = autoUpdate;
        _hotkey = hotkey;
        _hotkeyStatus = Strings.Get("Main.EjCtrlAltPTambien");
        _setAutoUpdate = setAutoUpdate;
        _applyHotkey = applyHotkey;
        CheckUpdates = new AsyncCommand(checkUpdates);
        ApplyHotkey = new RelayCommand(() => HotkeyStatus = _applyHotkey(_hotkey.Trim()));
    }

    public override bool IsInstant => true;

    public bool AutoUpdate
    {
        get => _autoUpdate;
        set
        {
            if (Set(ref _autoUpdate, value))
                _setAutoUpdate(value);
        }
    }

    public string Hotkey
    {
        get => _hotkey;
        set => Set(ref _hotkey, value);
    }

    public string HotkeyStatus
    {
        get => _hotkeyStatus;
        private set => Set(ref _hotkeyStatus, value);
    }

    public AsyncCommand CheckUpdates { get; }

    public RelayCommand ApplyHotkey { get; }

    protected override string[] Values(ServiceSettings s) => [];

    protected override string[] Snapshot() => [];

    protected override void Read(ServiceSettings s)
    {
    }

    public override ServiceSettings Write(ServiceSettings s) => s;

    protected override void Check(Action<string, string> report)
    {
    }
}
