using MiniPrinter.Protocol;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MiniPrinter.Imaging;

public enum TextAlign
{
    Left,
    Center,
    Right,
}

public sealed record TextStyle
{
    public string FontFamily { get; init; } = "Segoe UI";

    /// <summary>Font size in points (6–48).</summary>
    public float SizePt { get; init; } = 10;

    public bool Bold { get; init; }

    public TextAlign Align { get; init; } = TextAlign.Left;
}

/// <summary>
/// Renders plain text to a 1-bit raster at the head width (203 dpi): word wrapping inside 2 mm
/// side margins, system fonts with an emoji fallback, threshold conversion (no dithering).
/// </summary>
public static class TextRenderer
{
    public const int Dpi = 203;
    public const int MarginPx = 16; // 2 mm
    private static readonly string[] FallbackFamilies = ["Segoe UI", "Arial", "Tahoma"];

    public static MonoBitmap Render(string text, TextStyle? style = null, int widthPx = 384)
    {
        style ??= new TextStyle();
        using var image = RenderGray(text, style, widthPx);
        return ToMono(image);
    }

    /// <summary>Renders onto an L8 canvas (used by templates to compose several blocks).</summary>
    public static Image<L8> RenderGray(string text, TextStyle style, int widthPx)
    {
        var normalized = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        if (normalized.Length == 0)
            normalized = " ";

        var options = Options(style, widthPx);
        var size = TextMeasurer.MeasureAdvance(normalized, options);
        var height = Math.Max(1, (int)Math.Ceiling(size.Height) + 4);

        var image = new Image<L8>(widthPx, height, new L8(255));
        image.Mutate(c => c.DrawText(options, normalized, Color.Black));
        return image;
    }

    /// <summary>Text options positioned at the top-left margin, wrapping within the margins.</summary>
    public static RichTextOptions Options(TextStyle style, int widthPx, float top = 0)
    {
        var font = ResolveFamily(style.FontFamily).CreateFont(Math.Clamp(style.SizePt, 6, 48), style.Bold ? FontStyle.Bold : FontStyle.Regular);
        var wrap = widthPx - 2 * MarginPx;
        return new RichTextOptions(font)
        {
            Dpi = Dpi,
            Origin = new PointF(MarginPx, top),
            WrappingLength = wrap,
            WordBreaking = WordBreaking.BreakWord,
            TextAlignment = style.Align switch
            {
                TextAlign.Center => TextAlignment.Center,
                TextAlign.Right => TextAlignment.End,
                _ => TextAlignment.Start,
            },
            FallbackFontFamilies = Fallbacks(),
        };
    }

    public static FontFamily ResolveFamily(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && SystemFonts.TryGet(name, out var family))
            return family;
        foreach (var fallback in FallbackFamilies)
            if (SystemFonts.TryGet(fallback, out family))
                return family;
        return SystemFonts.Families.First();
    }

    public static MonoBitmap ToMono(Image<L8> image, byte threshold = 128)
    {
        var mono = new MonoBitmap(image.Width, image.Height);
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    mono[x, y] = row[x].PackedValue < threshold;
            }
        });
        return mono;
    }

    private static IReadOnlyList<FontFamily> Fallbacks()
    {
        var list = new List<FontFamily>();
        foreach (var name in new[] { "Segoe UI Emoji", "Segoe UI Symbol" })
            if (SystemFonts.TryGet(name, out var family))
                list.Add(family);
        return list;
    }
}
