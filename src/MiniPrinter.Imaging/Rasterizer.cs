using MiniPrinter.Protocol;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MiniPrinter.Imaging;

public enum DitherMode
{
    /// <summary>Threshold for line art and text, Atkinson for photos (decided per page).</summary>
    Auto,
    Atkinson,
    FloydSteinberg,
    Threshold,
}

public sealed record RasterOptions
{
    /// <summary>Print head width in dots; pages are scaled to exactly this width.</summary>
    public int WidthPx { get; init; } = 384;

    public DitherMode Dither { get; init; } = DitherMode.Auto;

    /// <summary>Grey level (0–255) below which a pixel is black in <see cref="DitherMode.Threshold"/>.</summary>
    public byte Threshold { get; init; } = 128;

    /// <summary>Brightness adjustment, -100..100 (0 = unchanged).</summary>
    public int Brightness { get; init; }

    /// <summary>Contrast adjustment, -100..100 (0 = unchanged).</summary>
    public int Contrast { get; init; }

    /// <summary>Remove blank rows at the top and bottom of each page.</summary>
    public bool TrimBlank { get; init; } = true;

    /// <summary>Blank rows kept below the last printed row when trimming.</summary>
    public int BottomMarginRows { get; init; } = 8;

    /// <summary>
    /// Crop blank side margins and fit the content to the head width, never enlarging beyond real
    /// size (pages whose resolution is known). Lets apps with A4-style margins print legibly.
    /// </summary>
    public bool FitContentWidth { get; init; } = true;

    /// <summary>Printer resolution, used to keep real size when fitting content.</summary>
    public int PrinterDpi { get; init; } = 203;
}

/// <summary>A rasterized page and whether it was classified as text (line art) or image.</summary>
public sealed record RasterResult(MonoBitmap Bitmap, bool IsText);

/// <summary>Grey page → 1-bit raster at the printer's head width.</summary>
public static class Rasterizer
{
    public static MonoBitmap Rasterize(GrayImage page, RasterOptions options) => RasterizeWithMode(page, options).Bitmap;

    /// <summary>
    /// Rasterizes and classifies the page: it is text when <see cref="ChooseMode"/> picks threshold
    /// (few mid-tones), independently of the dither mode used.
    /// </summary>
    public static RasterResult RasterizeWithMode(GrayImage page, RasterOptions options)
    {
        var source = options.FitContentWidth ? CropToContent(page, options.WidthPx, options.PrinterDpi) : page;
        var scaled = Scale(source, options.WidthPx);
        Adjust(scaled, options.Brightness, options.Contrast);
        var classification = ChooseMode(scaled);
        var mode = options.Dither == DitherMode.Auto ? classification : options.Dither;
        var mono = mode switch
        {
            DitherMode.Atkinson => Atkinson(scaled),
            DitherMode.FloydSteinberg => FloydSteinberg(scaled),
            _ => Threshold(scaled, options.Threshold),
        };
        var result = options.TrimBlank ? TrimBlankRows(mono, options.BottomMarginRows) : mono;
        return new RasterResult(result, classification == DitherMode.Threshold);
    }

    /// <summary>
    /// Picks threshold when fewer than 8 % of pixels are mid-tones (text, line art, anti-aliased
    /// edges), otherwise Atkinson.
    /// </summary>
    public static DitherMode ChooseMode(GrayImage image)
    {
        if (image.Pixels.Length == 0)
            return DitherMode.Threshold;
        var midTones = 0;
        foreach (var p in image.Pixels)
        {
            if (p is > 48 and < 208)
                midTones++;
        }
        return midTones < image.Pixels.Length * 0.08 ? DitherMode.Threshold : DitherMode.Atkinson;
    }

    /// <summary>
    /// Returns the horizontal window of <paramref name="page"/> that should be scaled to the head
    /// width: the inked columns plus 1 mm, widened to the real-size head width when the content
    /// is narrower (so it is never enlarged). Pages without a known resolution are cropped to their
    /// content and fitted. Pages already narrower than the window are returned unchanged.
    /// </summary>
    public static GrayImage CropToContent(GrayImage page, int headWidth, int printerDpi = 203, byte inkLevel = 224)
    {
        var left = -1;
        var right = -1;
        for (var x = 0; x < page.Width && left < 0; x++)
            if (ColumnHasInk(page, x, inkLevel)) left = x;
        if (left < 0)
            return page; // blank page; trimmed later
        for (var x = page.Width - 1; x >= left && right < 0; x--)
            if (ColumnHasInk(page, x, inkLevel)) right = x;

        var dpi = page.Dpi is > 0 ? page.Dpi.Value : printerDpi;
        var pad = (int)Math.Round(dpi / 25.4); // 1 mm
        var start = Math.Max(0, left - pad);
        var end = Math.Min(page.Width - 1, right + pad);
        var contentWidth = end - start + 1;

        // Width (in source pixels) that maps to the head at real size.
        var realSizeWidth = page.Dpi is > 0 ? (int)Math.Round(headWidth * (double)dpi / printerDpi) : contentWidth;
        var window = Math.Max(contentWidth, realSizeWidth);
        if (window >= page.Width)
            return page;

        // Left-align on the content, keeping the window inside the page.
        start = Math.Clamp(start, 0, page.Width - window);
        var cropped = new GrayImage(window, page.Height) { Dpi = page.Dpi };
        for (var y = 0; y < page.Height; y++)
            page.Row(y).Slice(start, window).CopyTo(cropped.Row(y));
        return cropped;
    }

    private static bool ColumnHasInk(GrayImage page, int x, byte inkLevel)
    {
        for (var y = 0; y < page.Height; y++)
            if (page[x, y] < inkLevel) return true;
        return false;
    }

    /// <summary>Scales proportionally so the width equals <paramref name="width"/> (height rounded).</summary>
    public static GrayImage Scale(GrayImage page, int width)
    {
        if (page.Width == width)
            return new GrayImage(page.Width, page.Height, (byte[])page.Pixels.Clone()) { Dpi = page.Dpi };

        var height = Math.Max(1, (int)Math.Round(page.Height * (double)width / page.Width));
        using var image = Image.LoadPixelData<L8>(page.Pixels, page.Width, page.Height);
        // Downscaling text benefits from an area-averaging filter; upscaling from bicubic.
        var sampler = width < page.Width ? KnownResamplers.Box : KnownResamplers.Bicubic;
        image.Mutate(x => x.Resize(width, height, sampler));
        return ImageDecoder.ToGray(image);
    }

    public static void Adjust(GrayImage image, int brightness, int contrast)
    {
        if (brightness == 0 && contrast == 0)
            return;
        var b = Math.Clamp(brightness, -100, 100) * 255 / 100.0;
        var c = Math.Clamp(contrast, -100, 100) / 100.0;
        var factor = c >= 0 ? 1 + c * 3 : 1 + c;
        var lut = new byte[256];
        for (var v = 0; v < 256; v++)
            lut[v] = (byte)Math.Clamp(Math.Round((v - 128) * factor + 128 + b), 0, 255);
        var pixels = image.Pixels;
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = lut[pixels[i]];
    }

    public static MonoBitmap Threshold(GrayImage image, byte threshold)
    {
        var mono = new MonoBitmap(image.Width, image.Height);
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
            mono[x, y] = image[x, y] < threshold;
        return mono;
    }

    public static MonoBitmap FloydSteinberg(GrayImage image) => ErrorDiffusion(image,
    [
        (1, 0, 7 / 16f), (-1, 1, 3 / 16f), (0, 1, 5 / 16f), (1, 1, 1 / 16f),
    ]);

    /// <summary>Atkinson dithering: diffuses 6/8 of the error, which keeps highlights clean on thermal paper.</summary>
    public static MonoBitmap Atkinson(GrayImage image) => ErrorDiffusion(image,
    [
        (1, 0, 1 / 8f), (2, 0, 1 / 8f), (-1, 1, 1 / 8f), (0, 1, 1 / 8f), (1, 1, 1 / 8f), (0, 2, 1 / 8f),
    ]);

    private static MonoBitmap ErrorDiffusion(GrayImage image, (int Dx, int Dy, float Weight)[] kernel)
    {
        var width = image.Width;
        var height = image.Height;
        var work = new float[width * height];
        for (var i = 0; i < work.Length; i++)
            work[i] = image.Pixels[i];

        var mono = new MonoBitmap(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var index = y * width + x;
            var old = work[index];
            var black = old < 128;
            mono[x, y] = black;
            var error = old - (black ? 0 : 255);
            foreach (var (dx, dy, weight) in kernel)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx >= 0 && nx < width && ny < height)
                    work[ny * width + nx] += error * weight;
            }
        }
        return mono;
    }

    /// <summary>Removes blank rows at the top and bottom, keeping <paramref name="bottomMargin"/> blank rows.</summary>
    public static MonoBitmap TrimBlankRows(MonoBitmap bitmap, int bottomMargin)
    {
        var top = 0;
        while (top < bitmap.Height && bitmap.IsRowBlank(top))
            top++;
        if (top == bitmap.Height)
            return new MonoBitmap(bitmap.Width, 0);

        var bottom = bitmap.Height - 1;
        while (bottom > top && bitmap.IsRowBlank(bottom))
            bottom--;
        var end = Math.Min(bitmap.Height - 1, bottom + Math.Max(0, bottomMargin));
        return bitmap.Slice(top, end - top + 1);
    }
}
