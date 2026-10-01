using MiniPrinter.Protocol.Catalog;

namespace MiniPrinter.Protocol;

/// <summary>Encodes one raster row as a <c>0xBF</c> (RLE) or <c>0xA2</c> (packed) packet.</summary>
public static class RowEncoder
{
    private const int MaxRun = 127;

    /// <summary>
    /// RLE encoding: each byte is <c>color &lt;&lt; 7 | run</c> with run 1..127. Rows containing
    /// black emit their trailing run; all-white rows emit their single white run.
    /// </summary>
    public static byte[] EncodeRle(ReadOnlySpan<byte> row)
    {
        if (row.IsEmpty)
            return [];

        var runs = new List<byte>(16);
        var previous = row[0] != 0 ? 1 : 0;
        var count = 1;
        var hasBlack = previous == 1;
        for (var i = 1; i < row.Length; i++)
        {
            var pixel = row[i] != 0 ? 1 : 0;
            hasBlack |= pixel == 1;
            if (pixel == previous)
            {
                count++;
            }
            else
            {
                AppendRun(runs, previous, count);
                previous = pixel;
                count = 1;
            }
        }

        if (hasBlack || runs.Count == 0)
            AppendRun(runs, previous, count);
        return runs.ToArray();
    }

    /// <summary>Packs a row to 1 bit per pixel; the leftmost pixel goes to bit 0 when <paramref name="lsbFirst"/>.</summary>
    public static byte[] Pack(ReadOnlySpan<byte> row, bool lsbFirst = true)
    {
        var packed = new byte[(row.Length + 7) / 8];
        for (var x = 0; x < row.Length; x++)
        {
            if (row[x] == 0)
                continue;
            var bit = lsbFirst ? x % 8 : 7 - x % 8;
            packed[x / 8] |= (byte)(1 << bit);
        }
        return packed;
    }

    /// <summary>Encodes a row as a complete packet using the profile's row encoding.</summary>
    public static byte[] EncodeRow(ReadOnlySpan<byte> row, RowEncoding encoding, bool lsbFirst = true)
    {
        if (encoding == RowEncoding.Rle)
        {
            var rle = EncodeRle(row);
            if (rle.Length <= (row.Length + 7) / 8)
                return Frame.Encode(Opcode.RleRow, rle);
        }
        return Frame.Encode(Opcode.RasterRow, Pack(row, lsbFirst));
    }

    private static void AppendRun(List<byte> runs, int color, int count)
    {
        while (count > MaxRun)
        {
            runs.Add((byte)((color << 7) | MaxRun));
            count -= MaxRun;
        }
        if (count > 0)
            runs.Add((byte)((color << 7) | count));
    }
}
