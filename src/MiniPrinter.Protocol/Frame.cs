namespace MiniPrinter.Protocol;

/// <summary>
/// One protocol packet: <c>51 78 | cmd | flags | len_lo len_hi | payload | crc8(payload) | FF</c>.
/// Flags are <c>00</c> for host→printer and <c>01</c> for printer→host.
/// </summary>
public sealed record Frame(byte Command, byte Flags, byte[] Payload)
{
    public const byte Prefix0 = 0x51;
    public const byte Prefix1 = 0x78;
    public const byte Terminator = 0xFF;
    public const int HeaderLength = 6;
    public const int Overhead = HeaderLength + 2;

    public const byte FlagsToPrinter = 0x00;
    public const byte FlagsFromPrinter = 0x01;

    public static byte[] Encode(byte command, ReadOnlySpan<byte> payload, byte flags = FlagsToPrinter)
    {
        if (payload.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(payload), "Payload exceeds 65535 bytes.");

        var packet = new byte[Overhead + payload.Length];
        packet[0] = Prefix0;
        packet[1] = Prefix1;
        packet[2] = command;
        packet[3] = flags;
        packet[4] = (byte)(payload.Length & 0xFF);
        packet[5] = (byte)(payload.Length >> 8);
        payload.CopyTo(packet.AsSpan(HeaderLength));
        packet[^2] = Crc8.Compute(payload);
        packet[^1] = Terminator;
        return packet;
    }

    public byte[] ToBytes() => Encode(Command, Payload, Flags);

    public override string ToString() => $"{Command:X2}/{Flags:X2} [{Convert.ToHexString(Payload)}]";
}
