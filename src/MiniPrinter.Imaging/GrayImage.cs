namespace MiniPrinter.Imaging;

/// <summary>8-bit luminance image: 0 = black, 255 = white.</summary>
public sealed class GrayImage
{
    public GrayImage(int width, int height, byte[]? pixels = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        Pixels = pixels ?? Enumerable.Repeat((byte)255, width * height).ToArray();
        if (Pixels.Length != width * height)
            throw new ArgumentException($"Expected {width * height} pixels, got {Pixels.Length}.", nameof(pixels));
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Row-major luminance values.</summary>
    public byte[] Pixels { get; }

    /// <summary>Horizontal resolution of the source in dpi, when known.</summary>
    public int? Dpi { get; init; }

    public byte this[int x, int y]
    {
        get => Pixels[y * Width + x];
        set => Pixels[y * Width + x] = value;
    }

    public Span<byte> Row(int y) => Pixels.AsSpan(y * Width, Width);
}
