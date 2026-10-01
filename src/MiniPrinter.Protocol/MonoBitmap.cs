namespace MiniPrinter.Protocol;

/// <summary>A 1-bit raster: one byte per pixel, 1 = black (heated dot), 0 = white.</summary>
public sealed class MonoBitmap
{
    private readonly byte[] _pixels;

    public MonoBitmap(int width, int height)
        : this(width, height, new byte[checked(width * height)])
    {
    }

    public MonoBitmap(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (pixels.Length != width * height)
            throw new ArgumentException($"Expected {width * height} pixels, got {pixels.Length}.", nameof(pixels));
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }

    public bool this[int x, int y]
    {
        get => _pixels[y * Width + x] != 0;
        set => _pixels[y * Width + x] = value ? (byte)1 : (byte)0;
    }

    public ReadOnlySpan<byte> Row(int y) => _pixels.AsSpan(y * Width, Width);

    public Span<byte> MutableRow(int y) => _pixels.AsSpan(y * Width, Width);

    public bool IsRowBlank(int y) => !Row(y).ContainsAnyExcept((byte)0);

    /// <summary>Returns rows <paramref name="start"/> .. start+count-1 as a new bitmap.</summary>
    public MonoBitmap Slice(int start, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (start + count > Height)
            throw new ArgumentOutOfRangeException(nameof(count));
        return new MonoBitmap(Width, count, _pixels.AsSpan(start * Width, count * Width).ToArray());
    }

    /// <summary>Reads a binary PBM (P4) image, where 1 bits are black and rows are MSB-first.</summary>
    public static MonoBitmap FromPbm(byte[] data)
    {
        var pos = 0;
        string Token()
        {
            while (pos < data.Length && (char.IsWhiteSpace((char)data[pos]) || data[pos] == '#'))
            {
                if (data[pos] == '#')
                    while (pos < data.Length && data[pos] != '\n') pos++;
                else
                    pos++;
            }
            var start = pos;
            while (pos < data.Length && !char.IsWhiteSpace((char)data[pos])) pos++;
            return System.Text.Encoding.ASCII.GetString(data, start, pos - start);
        }

        if (Token() != "P4")
            throw new InvalidDataException("Only binary PBM (P4) is supported.");
        var width = int.Parse(Token());
        var height = int.Parse(Token());
        pos++; // single whitespace before raster data

        var stride = (width + 7) / 8;
        if (data.Length - pos < stride * height)
            throw new InvalidDataException("Truncated PBM raster.");

        var bitmap = new MonoBitmap(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            bitmap[x, y] = (data[pos + y * stride + x / 8] & (0x80 >> (x % 8))) != 0;
        return bitmap;
    }
}
