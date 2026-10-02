using System.Buffers.Binary;
using System.Text;

namespace MiniPrinter.Imaging;

/// <summary>Fields of a PWG Raster page header (PWG 5102.4) that the pipeline needs.</summary>
public sealed record PwgPageHeader(
    int Width,
    int Height,
    int BitsPerColor,
    int BitsPerPixel,
    int BytesPerLine,
    int ColorSpace,
    int HwResolutionX,
    int HwResolutionY)
{
    public const int Size = 1796;

    // cups_cspace_t values used by PWG Raster.
    public const int ColorSpaceRgb = 1;
    public const int ColorSpaceBlack = 3;
    public const int ColorSpaceSGray = 18;
    public const int ColorSpaceSRgb = 19;

    public static PwgPageHeader Parse(ReadOnlySpan<byte> header) => new(
        Width: U32(header, 372),
        Height: U32(header, 376),
        BitsPerColor: U32(header, 384),
        BitsPerPixel: U32(header, 388),
        BytesPerLine: U32(header, 392),
        ColorSpace: U32(header, 400),
        HwResolutionX: U32(header, 276),
        HwResolutionY: U32(header, 280));

    private static int U32(ReadOnlySpan<byte> header, int offset) =>
        checked((int)BinaryPrimitives.ReadUInt32BigEndian(header[offset..]));
}

/// <summary>
/// Streaming reader for PWG Raster (<c>image/pwg-raster</c>): the "RaS2" sync word followed by
/// pages, each a 1796-byte header plus PackBits-style compressed lines. Supports 8-bit grey
/// (<c>sgray_8</c>, black), 24-bit RGB (<c>srgb_8</c>, rgb) and 1-bit black, converted to grey.
/// </summary>
public sealed class PwgRasterReader
{
    private static readonly byte[] SyncWord = "RaS2"u8.ToArray();
    private readonly Stream _stream;
    private bool _syncRead;

    public PwgRasterReader(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>True when <paramref name="start"/> begins with the PWG sync word.</summary>
    public static bool IsPwgRaster(ReadOnlySpan<byte> start) => start.StartsWith(SyncWord);

    public IEnumerable<GrayImage> ReadPages()
    {
        while (ReadPage() is { } page)
            yield return page;
    }

    /// <summary>Reads the next page, or returns null at the end of the stream.</summary>
    public GrayImage? ReadPage()
    {
        if (!_syncRead)
        {
            var sync = new byte[4];
            ReadExactly(sync, allowEof: false);
            if (!sync.AsSpan().SequenceEqual(SyncWord))
                throw new InvalidDataException($"Not a PWG Raster stream (sync word '{Encoding.ASCII.GetString(sync)}').");
            _syncRead = true;
        }

        var headerBytes = new byte[PwgPageHeader.Size];
        if (!ReadExactly(headerBytes, allowEof: true))
            return null;
        var header = PwgPageHeader.Parse(headerBytes);
        Validate(header);

        var bytesPerPixel = Math.Max(1, header.BitsPerPixel / 8);
        var line = new byte[header.BytesPerLine];
        var image = new GrayImage(header.Width, header.Height) { Dpi = header.HwResolutionX };
        var white = WhiteValue(header);

        var y = 0;
        while (y < header.Height)
        {
            var repeat = ReadByte() + 1;
            DecodeLine(line, bytesPerPixel, white);
            for (var r = 0; r < repeat && y < header.Height; r++, y++)
                ConvertLine(header, line, image.Row(y));
        }
        return image;
    }

    private void DecodeLine(byte[] line, int bytesPerPixel, byte white)
    {
        var position = 0;
        // One pixel buffer per line (allocating it inside the loop could exhaust the stack).
        Span<byte> pixel = stackalloc byte[bytesPerPixel];
        while (position < line.Length)
        {
            var control = ReadByte();
            if (control == 128)
            {
                // Remainder of the line is white.
                line.AsSpan(position).Fill(white);
                return;
            }
            if (control < 128)
            {
                var count = control + 1;
                ReadExactly(pixel);
                for (var i = 0; i < count && position < line.Length; i++)
                {
                    var n = Math.Min(bytesPerPixel, line.Length - position);
                    pixel[..n].CopyTo(line.AsSpan(position));
                    position += n;
                }
            }
            else
            {
                var count = (257 - control) * bytesPerPixel;
                var n = Math.Min(count, line.Length - position);
                ReadExactly(line.AsSpan(position, n));
                if (count > n)
                    Skip(count - n);
                position += n;
            }
        }
    }

    private static void ConvertLine(PwgPageHeader header, ReadOnlySpan<byte> line, Span<byte> output)
    {
        switch (header.ColorSpace, header.BitsPerPixel)
        {
            case (PwgPageHeader.ColorSpaceSGray, 8):
                line[..output.Length].CopyTo(output);
                break;
            case (PwgPageHeader.ColorSpaceBlack, 8):
                for (var x = 0; x < output.Length; x++)
                    output[x] = (byte)(255 - line[x]);
                break;
            case (PwgPageHeader.ColorSpaceBlack, 1):
                for (var x = 0; x < output.Length; x++)
                    output[x] = (line[x / 8] & (0x80 >> (x % 8))) != 0 ? (byte)0 : (byte)255;
                break;
            case (PwgPageHeader.ColorSpaceSRgb or PwgPageHeader.ColorSpaceRgb, 24):
                for (var x = 0; x < output.Length; x++)
                {
                    var r = line[x * 3];
                    var g = line[x * 3 + 1];
                    var b = line[x * 3 + 2];
                    output[x] = (byte)((r * 299 + g * 587 + b * 114 + 500) / 1000);
                }
                break;
        }
    }

    private static byte WhiteValue(PwgPageHeader header) => header.ColorSpace == PwgPageHeader.ColorSpaceBlack ? (byte)0 : (byte)255;

    private static void Validate(PwgPageHeader header)
    {
        var supported = (header.ColorSpace, header.BitsPerPixel) is
            (PwgPageHeader.ColorSpaceSGray, 8) or (PwgPageHeader.ColorSpaceBlack, 8) or (PwgPageHeader.ColorSpaceBlack, 1) or
            (PwgPageHeader.ColorSpaceSRgb, 24) or (PwgPageHeader.ColorSpaceRgb, 24);
        if (!supported)
            throw new NotSupportedException($"Unsupported PWG raster: color space {header.ColorSpace}, {header.BitsPerPixel} bits per pixel.");
        if (header.Width <= 0 || header.Height < 0 || header.BytesPerLine < (header.Width * header.BitsPerPixel + 7) / 8)
            throw new InvalidDataException($"Invalid PWG page geometry {header.Width}x{header.Height}, {header.BytesPerLine} bytes per line.");
    }

    private int ReadByte()
    {
        var b = _stream.ReadByte();
        return b >= 0 ? b : throw new EndOfStreamException("Truncated PWG raster data.");
    }

    private void ReadExactly(Span<byte> buffer)
    {
        _stream.ReadExactly(buffer);
    }

    private bool ReadExactly(byte[] buffer, bool allowEof)
    {
        var read = _stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (read == 0 && allowEof)
            return false;
        if (read < buffer.Length)
            throw new EndOfStreamException("Truncated PWG raster stream.");
        return true;
    }

    private void Skip(int count)
    {
        Span<byte> scratch = stackalloc byte[256];
        while (count > 0)
        {
            var n = Math.Min(count, scratch.Length);
            ReadExactly(scratch[..n]);
            count -= n;
        }
    }
}

/// <summary>Writes PWG Raster (used by tests and by the CLI to produce sample documents).</summary>
public static class PwgRasterWriter
{
    /// <summary>Writes grey pages as <c>sgray_8</c> with simple run-length compression.</summary>
    public static void Write(Stream stream, IEnumerable<GrayImage> pages, int dpi = 203)
    {
        stream.Write("RaS2"u8);
        foreach (var page in pages)
        {
            var header = new byte[PwgPageHeader.Size];
            Encoding.ASCII.GetBytes("PwgRaster").CopyTo(header, 0);
            void U32(int offset, int value) => BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(offset), (uint)value);
            U32(276, dpi);
            U32(280, dpi);
            U32(372, page.Width);
            U32(376, page.Height);
            U32(384, 8);
            U32(388, 8);
            U32(392, page.Width);
            U32(396, 0);
            U32(400, PwgPageHeader.ColorSpaceSGray);
            U32(420, 1);
            stream.Write(header);

            for (var y = 0; y < page.Height; y++)
            {
                stream.WriteByte(0); // no line repeat
                var row = page.Row(y);
                var x = 0;
                while (x < row.Length)
                {
                    var run = 1;
                    while (x + run < row.Length && run < 128 && row[x + run] == row[x])
                        run++;
                    if (run > 1)
                    {
                        stream.WriteByte((byte)(run - 1));
                        stream.WriteByte(row[x]);
                        x += run;
                        continue;
                    }
                    var literal = 1;
                    while (x + literal < row.Length && literal < 127 &&
                           (x + literal + 1 >= row.Length || row[x + literal] != row[x + literal + 1]))
                        literal++;
                    stream.WriteByte((byte)(257 - literal));
                    stream.Write(row.Slice(x, literal));
                    x += literal;
                }
            }
        }
    }
}
