using System.Text.RegularExpressions;

namespace MiniPrinter.Protocol;

/// <summary>
/// Alarm bits of the <c>A3</c> reply. <see cref="OutOfPaper"/> is verified on the X5h (an open
/// cover reads the same, there is no separate cover bit). The other bits come from iPrint (GT01)
/// and have not been observed on the X5h; unknown bits stay in <see cref="DeviceState.AlarmByte"/>.
/// </summary>
[Flags]
public enum PrinterAlarms : byte
{
    None = 0,
    OutOfPaper = 0x01,
    Overheated = 0x04,
    LowBattery = 0x08,
}

/// <summary>Any decoded printer→host message.</summary>
public abstract record PrinterMessage(Frame Frame)
{
    /// <summary>Interprets a decoded frame; unknown opcodes become <see cref="UnknownMessage"/>.</summary>
    public static PrinterMessage Parse(Frame frame) => frame.Command switch
    {
        Opcode.GetDeviceState => DeviceState.From(frame),
        Opcode.GetDeviceInfo => DeviceInfo.From(frame),
        Opcode.GetDeviceId => new DeviceId(frame, frame.Payload),
        Opcode.FlowControl when frame.Payload.Length >= 1 => new FlowControl(frame, Paused: frame.Payload[0] == 0x10),
        _ => new UnknownMessage(frame),
    };
}

/// <summary>
/// <c>A3</c> reply: <c>[alarms] [paper sensor] [battery]</c>. Verified on the X5h (2026-10-01):
/// <c>00 0E 27</c> with paper, <c>01 1B 27</c> without paper or with the cover open. Byte 1 is the
/// raw optical paper sensor (≈14–15 with paper, ≈24–27 without); byte 2 is a battery indicator
/// (39–40 observed) whose unit — percent or tenths of a volt — is not confirmed.
/// </summary>
public sealed record DeviceState(Frame Frame, byte AlarmByte, int? PaperSensor, int? BatteryLevel) : PrinterMessage(Frame)
{
    public PrinterAlarms Alarms => (PrinterAlarms)AlarmByte;
    public bool IsReady => AlarmByte == 0;

    internal static DeviceState From(Frame frame)
    {
        var p = frame.Payload;
        var alarm = p.Length > 0 ? p[0] : (byte)0;
        int? sensor = p.Length >= 2 ? p[1] : null;
        int? battery = p.Length >= 3 ? p[2] : null;
        return new DeviceState(frame, alarm, sensor, battery);
    }
}

/// <summary>
/// <c>A8</c> reply. Observed on the X5h: <c>78 00 03 "3.0.5" 44 00</c>. Only the dotted version
/// number is interpreted; whether the following <c>0x44</c> ('D') belongs to it is unknown, so the
/// raw payload stays available through <see cref="PrinterMessage.Frame"/>.
/// </summary>
public sealed partial record DeviceInfo(Frame Frame, string? Firmware) : PrinterMessage(Frame)
{
    internal static DeviceInfo From(Frame frame)
    {
        var ascii = new string(frame.Payload.Select(b => b is >= 0x20 and < 0x7F ? (char)b : ' ').ToArray());
        var match = VersionPattern().Match(ascii);
        return new DeviceInfo(frame, match.Success ? match.Value : null);
    }

    [GeneratedRegex(@"\d+(\.\d+)+")]
    private static partial Regex VersionPattern();
}

/// <summary><c>BB</c> reply: device identifier bytes (all zero when none is programmed).</summary>
public sealed record DeviceId(Frame Frame, byte[] Id) : PrinterMessage(Frame)
{
    public bool IsProgrammed => Id.Any(b => b != 0);
}

/// <summary><c>AE</c> notification: the printer asks the host to pause (buffer full) or resume.</summary>
public sealed record FlowControl(Frame Frame, bool Paused) : PrinterMessage(Frame);

public sealed record UnknownMessage(Frame Frame) : PrinterMessage(Frame);
