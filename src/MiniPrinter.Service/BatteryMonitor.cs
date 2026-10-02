using MiniPrinter.Control;

namespace MiniPrinter.Service;

/// <summary>Tracks the battery percentage (per the configured unit) and the low-battery state.</summary>
public sealed class BatteryMonitor
{
    private readonly PrinterManager _printer;
    private readonly SettingsStore _settings;
    private readonly LowBatteryTracker _tracker = new();
    private readonly object _gate = new();

    public BatteryMonitor(PrinterManager printer, SettingsStore settings)
    {
        _printer = printer;
        _settings = settings;
        printer.StatusChanged += _ => Evaluate();
        settings.Changed += (_, _) => Evaluate();
    }

    public int? Percent => BatteryInterpreter.Percent(_printer.Status.State?.BatteryLevel, _settings.Current.BatteryUnit);

    public bool IsLow
    {
        get { lock (_gate) return _tracker.IsLow; }
    }

    /// <summary>Raised when the battery has just become low.</summary>
    public event Action? BecameLow;

    public void Evaluate()
    {
        bool becameLow;
        lock (_gate)
            becameLow = _tracker.Update(Percent, _settings.Current.LowBatteryPercent);
        if (becameLow)
            BecameLow?.Invoke();
    }
}
