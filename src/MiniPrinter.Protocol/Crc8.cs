namespace MiniPrinter.Protocol;

/// <summary>CRC-8 (polynomial 0x07, initial value 0, no reflection) used by the "tiny" protocol.</summary>
public static class Crc8
{
    private static readonly byte[] Table = BuildTable();

    public static byte Compute(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (var b in data)
            crc = Table[crc ^ b];
        return crc;
    }

    private static byte[] BuildTable()
    {
        var table = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var crc = (byte)i;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x80) != 0 ? (byte)((crc << 1) ^ 0x07) : (byte)(crc << 1);
            table[i] = crc;
        }
        return table;
    }
}
