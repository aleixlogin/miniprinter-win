namespace MiniPrinter.Protocol;

/// <summary>Command opcodes of the "tiny" protocol family.</summary>
public static class Opcode
{
    public const byte RetractPaper = 0xA0;
    public const byte FeedPaper = 0xA1;
    public const byte RasterRow = 0xA2;
    public const byte GetDeviceState = 0xA3;
    public const byte Darkness = 0xA4;
    public const byte GetDeviceInfo = 0xA8;
    public const byte FlowControl = 0xAE;
    public const byte Energy = 0xAF;
    public const byte GetDeviceId = 0xBB;
    public const byte Speed = 0xBD;
    public const byte PrintMode = 0xBE;
    public const byte RleRow = 0xBF;
}

/// <summary>Builders for host→printer command packets.</summary>
public static class Commands
{
    /// <summary>Darkness level 1..5 (clamped), sent as <c>0x30 + level</c>.</summary>
    public static byte[] Darkness(int level) =>
        Frame.Encode(Opcode.Darkness, [(byte)(0x30 + Math.Clamp(level, 1, 5))]);

    /// <summary>Heating energy as uint16 little-endian. Returns an empty array for energy ≤ 0 (command omitted).</summary>
    public static byte[] Energy(int energy) =>
        energy <= 0 ? [] : Frame.Encode(Opcode.Energy, [(byte)(energy & 0xFF), (byte)((energy >> 8) & 0xFF)]);

    public static byte[] PrintMode(bool isText) => Frame.Encode(Opcode.PrintMode, [(byte)(isText ? 1 : 0)]);

    /// <summary>Print speed / feed padding (one byte).</summary>
    public static byte[] Speed(int value) => Frame.Encode(Opcode.Speed, [(byte)(value & 0xFF)]);

    /// <summary>Advance the paper by <paramref name="dots"/> dot rows.</summary>
    public static byte[] FeedPaper(int dots) => Frame.Encode(Opcode.FeedPaper, LittleEndian16(dots));

    /// <summary>Pull the paper back by <paramref name="dots"/> dot rows.</summary>
    public static byte[] RetractPaper(int dots) => Frame.Encode(Opcode.RetractPaper, LittleEndian16(dots));

    /// <summary>Paper-position step appended after each page: 48 dots at 200 dpi, 72 at 300 dpi.</summary>
    public static byte[] PaperStep(int dpi) => FeedPaper(dpi == 300 ? 72 : 48);

    public static byte[] GetDeviceState() => Frame.Encode(Opcode.GetDeviceState, [0x00]);

    public static byte[] GetDeviceInfo() => Frame.Encode(Opcode.GetDeviceInfo, [0x00]);

    public static byte[] GetDeviceId() => Frame.Encode(Opcode.GetDeviceId, [0x01]);

    private static byte[] LittleEndian16(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, ushort.MaxValue);
        return [(byte)(value & 0xFF), (byte)(value >> 8)];
    }
}
