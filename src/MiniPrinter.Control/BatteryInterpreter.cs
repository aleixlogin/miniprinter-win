namespace MiniPrinter.Control;

/// <summary>
/// Interprets byte 2 of the printer's <c>A3</c> reply. Its unit is not confirmed on the X5h
/// (39–40 observed): it may be a percentage or the cell voltage in tenths of a volt.
/// </summary>
public static class BatteryInterpreter
{
    // Piecewise-linear lithium cell curve: (voltage in tenths of a volt, percent).
    private static readonly (int Decivolts, int Percent)[] Curve = [(33, 0), (37, 50), (42, 100)];

    /// <summary>Battery percentage, or null when the unit is unknown or the value is missing.</summary>
    public static int? Percent(int? raw, BatteryUnit unit)
    {
        if (raw is not { } value)
            return null;
        return unit switch
        {
            BatteryUnit.Percent => Math.Clamp(value, 0, 100),
            BatteryUnit.Decivolts => FromDecivolts(value),
            _ => null,
        };
    }

    public static int FromDecivolts(int decivolts)
    {
        if (decivolts <= Curve[0].Decivolts)
            return 0;
        for (var i = 1; i < Curve.Length; i++)
        {
            var (v1, p1) = Curve[i];
            if (decivolts <= v1)
            {
                var (v0, p0) = Curve[i - 1];
                return p0 + (int)Math.Round((decivolts - v0) * (p1 - p0) / (double)(v1 - v0));
            }
        }
        return 100;
    }

    /// <summary>Human-readable value for the tray.</summary>
    public static string Describe(int? raw, BatteryUnit unit) => (raw, unit) switch
    {
        (null, _) => "—",
        (var v, BatteryUnit.Percent) => $"{Percent(v, unit)} %",
        (var v, BatteryUnit.Decivolts) => $"{v / 10.0:0.0} V (≈{Percent(v, unit)} %)",
        (var v, _) => $"{v} (valor en bruto)",
    };
}

/// <summary>Low-battery state with hysteresis: re-arms once the level rises 5 points above the threshold.</summary>
public sealed class LowBatteryTracker
{
    public const int Hysteresis = 5;

    public bool IsLow { get; private set; }

    /// <summary>Updates the state; returns true when the battery has just become low.</summary>
    public bool Update(int? percent, int threshold)
    {
        if (percent is not { } p)
        {
            IsLow = false;
            return false;
        }
        if (!IsLow && p < threshold)
        {
            IsLow = true;
            return true;
        }
        if (IsLow && p >= threshold + Hysteresis)
            IsLow = false;
        return false;
    }
}
