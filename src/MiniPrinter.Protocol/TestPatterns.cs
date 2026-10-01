namespace MiniPrinter.Protocol;

/// <summary>Built-in rasters for calibration and test prints (no fonts or image libraries needed).</summary>
public static class TestPatterns
{
    private static readonly int[,] Bayer4 =
    {
        { 0, 8, 2, 10 },
        { 12, 4, 14, 6 },
        { 3, 11, 1, 9 },
        { 15, 7, 13, 5 },
    };

    /// <summary>
    /// Calibration page: a frame on the outermost dots, a ruler with ticks every 8 dots (longer
    /// every 32 and 64), five grey blocks (0–100 %) and a single-dot checkerboard. If the frame's
    /// left and right edges both print, the full head width is usable; tick spacing reveals scale.
    /// </summary>
    public static MonoBitmap Calibration(int width = 384)
    {
        const int rulerHeight = 40;
        const int blockHeight = 48;
        const int checkerHeight = 16;
        var height = 4 + rulerHeight + 8 + blockHeight + 8 + checkerHeight + 4;
        var bitmap = new MonoBitmap(width, height);

        // Frame on the outermost dots.
        for (var x = 0; x < width; x++)
        {
            bitmap[x, 0] = true;
            bitmap[x, height - 1] = true;
        }
        for (var y = 0; y < height; y++)
        {
            bitmap[0, y] = true;
            bitmap[width - 1, y] = true;
        }

        // Ruler.
        var top = 4;
        for (var x = 0; x < width; x += 8)
        {
            var length = x % 64 == 0 ? rulerHeight : x % 32 == 0 ? rulerHeight * 2 / 3 : rulerHeight / 3;
            for (var y = top; y < top + length; y++)
                bitmap[x, y] = true;
        }

        // Grey blocks: 0, 25, 50, 75, 100 % with a 4x4 ordered dither.
        top += rulerHeight + 8;
        const int levels = 5;
        for (var x = 2; x < width - 2; x++)
        {
            var level = Math.Min(levels - 1, (x - 2) * levels / (width - 4));
            var coverage = level * 16 / (levels - 1);
            for (var y = top; y < top + blockHeight; y++)
                bitmap[x, y] = Bayer4[y % 4, x % 4] < coverage;
        }

        // Checkerboard.
        top += blockHeight + 8;
        for (var y = top; y < top + checkerHeight; y++)
        for (var x = 2; x < width - 2; x++)
            bitmap[x, y] = ((x + y) & 1) == 1;

        return bitmap;
    }

    /// <summary>A page of <paramref name="rows"/> rows with a diagonal stripe pattern, for long-job tests.</summary>
    public static MonoBitmap Stripes(int width, int rows)
    {
        var bitmap = new MonoBitmap(width, rows);
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < width; x++)
            bitmap[x, y] = ((x + y) / 16 & 1) == 1;
        return bitmap;
    }
}
