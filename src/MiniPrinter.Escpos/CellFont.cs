using System.Collections.Concurrent;
using System.Text;
using MiniPrinter.Imaging;
using MiniPrinter.Protocol;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using TextRenderer = MiniPrinter.Imaging.TextRenderer;

namespace MiniPrinter.Escpos;

/// <summary>Scripts that need the font engine to shape whole runs: contextual letter forms and mark positioning.</summary>
internal enum ComplexScript
{
    None,
    Arabic,
    Thai,
}

/// <summary>Which regional style a run of ideographs is drawn in (the same code point looks different in each).</summary>
internal enum TextRegion
{
    Japanese,
    SimplifiedChinese,
    TraditionalChinese,
    Korean,
}

/// <summary>A drawn character: its bitmap (null for a blank) and how many cells wide it is.</summary>
internal readonly record struct GlyphResult(MonoBitmap? Bitmap, int Cells);

/// <summary>
/// Fixed-size character cells like a receipt printer's built-in fonts: 12 × 24 dots for font A
/// (32 columns on 384 dots) and 9 × 17 for font B (42 columns). Narrow glyphs come from a monospaced
/// system font scaled to fit the cell; full-width characters (CJK, Hangul, emoji) take two cells
/// and come from regional fonts, with a symbol font as the last resort. Glyphs are cached.
/// </summary>
internal static class CellFont
{
    public const int WidthA = 12, HeightA = 24, WidthB = 9, HeightB = 17;

    private static readonly string[] Monospaced = ["Consolas", "Cascadia Mono", "Lucida Console", "Courier New"];
    private static readonly string[] Symbols = ["Segoe UI Symbol", "Segoe UI Emoji", "Segoe UI", "Arial Unicode MS"];

    private static readonly Dictionary<TextRegion, string[]> Regional = new()
    {
        [TextRegion.Japanese] = ["MS Gothic", "Yu Gothic", "Meiryo", "Microsoft YaHei", "SimSun", "Microsoft JhengHei", "Malgun Gothic"],
        [TextRegion.SimplifiedChinese] = ["Microsoft YaHei", "SimSun", "NSimSun", "Yu Gothic", "MS Gothic", "Microsoft JhengHei", "Malgun Gothic"],
        [TextRegion.TraditionalChinese] = ["Microsoft JhengHei", "MingLiU", "PMingLiU", "Microsoft YaHei", "SimSun", "Yu Gothic", "MS Gothic", "Malgun Gothic"],
        [TextRegion.Korean] = ["Malgun Gothic", "Gulim", "Batang", "Yu Gothic", "MS Gothic", "Microsoft YaHei", "SimSun"],
    };

    private static readonly ConcurrentDictionary<(int Codepoint, bool Bold, bool FontB, TextRegion Region), GlyphResult> Cache = new();
    private static readonly ConcurrentDictionary<string, FontFamily?> Families = new();

    private static readonly Lazy<FontFamily> Base = new(() =>
    {
        foreach (var name in Monospaced)
            if (SystemFonts.TryGet(name, out var family))
                return family;
        return TextRenderer.ResolveFamily(null);
    });

    public static int CellWidth(bool fontB) => fontB ? WidthB : WidthA;

    public static int CellHeight(bool fontB) => fontB ? HeightB : HeightA;

    // ---- classification -----------------------------------------------------------------------------------

    /// <summary>Characters that take two cells: ideographs, kana, Hangul, full-width forms and emoji.</summary>
    public static bool IsWide(Rune rune)
    {
        var v = rune.Value;
        return v is (>= 0x1100 and <= 0x115F) or (>= 0x2E80 and <= 0x303E) or (>= 0x3041 and <= 0x33FF) or (>= 0x3400 and <= 0x4DBF)
            or (>= 0x4E00 and <= 0x9FFF) or (>= 0xA000 and <= 0xA4CF) or (>= 0xA960 and <= 0xA97F) or (>= 0xAC00 and <= 0xD7A3)
            or (>= 0xF900 and <= 0xFAFF) or (>= 0xFE30 and <= 0xFE6F) or (>= 0xFF01 and <= 0xFF60) or (>= 0xFFE0 and <= 0xFFE6)
            or (>= 0x1F300 and <= 0x1FAFF) or (>= 0x20000 and <= 0x3FFFD);
    }

    /// <summary>Characters that take no cell: zero-width spaces and joiners, variation selectors, the BOM.</summary>
    public static bool IsZeroWidth(Rune rune)
    {
        var v = rune.Value;
        return v is (>= 0x200B and <= 0x200F) or (>= 0x202A and <= 0x202E) or 0x2060 or (>= 0x2061 and <= 0x2064) or (>= 0xFE00 and <= 0xFE0F)
            or 0xFEFF or (>= 0xE0100 and <= 0xE01EF) or 0x00AD;
    }

    /// <summary>Hebrew letters, whose runs are written right to left (Arabic goes through the shaper as a whole run).</summary>
    public static bool IsRightToLeft(Rune rune)
    {
        var v = rune.Value;
        return v is (>= 0x0590 and <= 0x05FF) or (>= 0xFB1D and <= 0xFB4F);
    }

    /// <summary>
    /// Puts right-to-left runs (and the spaces and punctuation between their words) in visual order, so that
    /// Hebrew reads correctly when drawn cell by cell.
    /// </summary>
    public static string VisualOrder(string text)
    {
        var runes = text.EnumerateRunes().ToList();
        if (!runes.Any(IsRightToLeft))
            return text;
        var result = new List<Rune>(runes.Count);
        for (var i = 0; i < runes.Count;)
        {
            if (!IsRightToLeft(runes[i]))
            {
                result.Add(runes[i++]);
                continue;
            }
            var end = i;                    // last right-to-left letter of the run
            for (var j = i; j < runes.Count; j++)
            {
                if (IsRightToLeft(runes[j]))
                    end = j;
                else if (!(Rune.IsWhiteSpace(runes[j]) || Rune.IsPunctuation(runes[j])))
                    break;
            }
            for (var j = end; j >= i; j--)
                result.Add(runes[j]);
            i = end + 1;
        }
        return string.Concat(result.Select(r => r.ToString()));
    }


    /// <summary>The regional style of a text run: kana mean Japanese, Hangul Korean, anything else the default.</summary>
    public static TextRegion RegionOf(string text, TextRegion fallback)
    {
        var korean = false;
        foreach (var rune in text.EnumerateRunes())
        {
            var v = rune.Value;
            if (v is (>= 0x3040 and <= 0x30FF) or (>= 0x31F0 and <= 0x31FF) or (>= 0xFF66 and <= 0xFF9F))
                return TextRegion.Japanese;
            if (v is (>= 0xAC00 and <= 0xD7A3) or (>= 0x1100 and <= 0x11FF) or (>= 0x3130 and <= 0x318F))
                korean = true;
        }
        return korean ? TextRegion.Korean : fallback;
    }

    public static TextRegion RegionOfCodePage(int codePage) => codePage switch
    {
        936 or 54936 => TextRegion.SimplifiedChinese,
        950 => TextRegion.TraditionalChinese,
        949 or 51949 => TextRegion.Korean,
        _ => TextRegion.Japanese,
    };

    // ---- complex scripts (Arabic, Thai) ---------------------------------------------------------------------

    private static readonly string[] ArabicFonts = ["Tahoma", "Segoe UI", "Arial", "Times New Roman"];
    private static readonly string[] ThaiFonts = ["Leelawadee UI", "Tahoma", "Leelawadee", "Segoe UI"];

    public static ComplexScript ScriptOf(Rune rune)
    {
        var v = rune.Value;
        if (v is (>= 0x0600 and <= 0x06FF) or (>= 0x0750 and <= 0x077F) or (>= 0x08A0 and <= 0x08FF) or (>= 0xFB50 and <= 0xFDFF) or (>= 0xFE70 and <= 0xFEFC))
            return ComplexScript.Arabic;
        return v is >= 0x0E00 and <= 0x0E7F ? ComplexScript.Thai : ComplexScript.None;
    }

    /// <summary>Spaces, punctuation, digits and the joiners: they stay inside a complex run when more of the same script follows.</summary>
    private static bool IsNeutral(Rune rune) =>
        Rune.IsWhiteSpace(rune) || Rune.IsPunctuation(rune) || Rune.IsSymbol(rune) || Rune.IsDigit(rune) && rune.Value < 0x80 || rune.Value is 0x200C or 0x200D;

    /// <summary>Splits text into runs of Arabic, Thai and everything else, keeping the neutrals between words of a script with it.</summary>
    public static List<(string Text, ComplexScript Script)> Segment(string text)
    {
        var runes = text.EnumerateRunes().ToList();
        var result = new List<(string, ComplexScript)>();
        var current = new StringBuilder();
        var script = ComplexScript.None;

        void Flush()
        {
            if (current.Length > 0)
                result.Add((current.ToString(), script));
            current.Clear();
        }

        for (var i = 0; i < runes.Count; i++)
        {
            var kind = ScriptOf(runes[i]);
            if (kind != ComplexScript.None)
            {
                if (script != kind)
                {
                    Flush();
                    script = kind;
                }
                current.Append(runes[i].ToString());
                continue;
            }
            if (script != ComplexScript.None && IsNeutral(runes[i]))
            {
                var j = i;
                while (j < runes.Count && IsNeutral(runes[j]))
                    j++;
                if (j < runes.Count && ScriptOf(runes[j]) == script)
                {
                    for (; i < j; i++)
                        current.Append(runes[i].ToString());
                    i--;
                    continue;
                }
            }
            if (script != ComplexScript.None)
            {
                Flush();
                script = ComplexScript.None;
            }
            current.Append(runes[i].ToString());
        }
        Flush();
        return result;
    }

    /// <summary>
    /// Draws a run of Arabic or Thai as proportional text (the font engine joins the letters, orders them right to
    /// left and places the marks). Each line's width is a whole number of cells; a run wider than
    /// <paramref name="maxWidth"/> is split by words, Arabic lines aligned to the right. Null when no font has the script.
    /// </summary>
    public static List<MonoBitmap>? Run(string text, ComplexScript script, bool bold, bool fontB, int maxWidth)
    {
        try
        {
            return RunCore(text, script, bold, fontB, maxWidth);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;   // the caller falls back to drawing the characters one by one
        }
    }

    private static List<MonoBitmap>? RunCore(string text, ComplexScript script, bool bold, bool fontB, int maxWidth)
    {
        var cellW = CellWidth(fontB);
        var cellH = CellHeight(fontB);
        text = new string(text.EnumerateRunes().Where(r => !IsZeroWidth(r) || r.Value is 0x200C or 0x200D).SelectMany(r => r.ToString()).ToArray());
        var first = text.EnumerateRunes().FirstOrDefault(r => ScriptOf(r) == script);
        var family = default(FontFamily?);
        foreach (var name in script == ComplexScript.Arabic ? ArabicFonts : ThaiFonts)
            if (Family(name) is { } candidate && HasGlyph(candidate, first))
            {
                family = candidate;
                break;
            }
        if (family is not { } chosen)
            return null;

        var reference = new TextOptions(chosen.CreateFont(100)) { Dpi = 72 };
        var lineRatio = Math.Max(0.5f, TextMeasurer.MeasureAdvance("Mgกا", reference).Height / 100f);
        var font = chosen.CreateFont(Math.Min(cellH * 0.95f / lineRatio, cellH * 0.9f));
        var options = new TextOptions(font) { Dpi = 72 };
        float Measure(string s) => TextMeasurer.MeasureAdvance(s, options).Width;

        // Lines: the whole run if it fits, otherwise greedy by words.
        var lines = new List<string>();
        if (Measure(text) <= maxWidth - 1)
        {
            lines.Add(text);
        }
        else
        {
            var line = "";
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Measure(candidate) > maxWidth - 1)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }
            if (line.Length > 0)
                lines.Add(line);
        }

        var result = new List<MonoBitmap>();
        foreach (var line in lines)
        {
            var width = Measure(line);
            var wrapped = lines.Count > 1;
            var bitmapWidth = wrapped ? maxWidth : Math.Max(cellW, (int)Math.Ceiling(width / cellW) * cellW);
            using var image = new Image<L8>(bitmapWidth, cellH, new L8(255));
            var x = wrapped && script == ComplexScript.Arabic ? Math.Max(0, bitmapWidth - width - 1) : 1f;
            var draw = new RichTextOptions(font) { Dpi = 72, Origin = new PointF(x, cellH / 2f), VerticalAlignment = VerticalAlignment.Center };
            image.Mutate(c => c.DrawText(draw, line, Color.Black));
            var mono = TextRenderer.ToMono(image, 200);
            if (bold)
            {
                var heavier = new MonoBitmap(bitmapWidth, cellH);
                for (var y = 0; y < cellH; y++)
                for (var px = 0; px < bitmapWidth; px++)
                    heavier[px, y] = mono[px, y] || (px > 0 && mono[px - 1, y]);
                mono = heavier;
            }
            result.Add(mono);
        }
        return result;
    }


    // ---- glyphs -----------------------------------------------------------------------------------------------

    /// <summary>The cell bitmap of one character; a '?' of one cell when no font has it.</summary>
    public static GlyphResult Glyph(Rune rune, bool bold, bool fontB, TextRegion region)
    {
        if (rune.Value is ' ' or 0xA0)
            return new GlyphResult(null, 1);
        if (rune.Value == 0x3000)
            return new GlyphResult(null, 2);
        var key = (rune.Value, bold, fontB, region);
        if (Cache.Count > 20_000)
            Cache.Clear();   // hostile input can ask for every code point: bound the memory
        return Cache.GetOrAdd(key, k => SafeDraw(new Rune(k.Codepoint), k.Bold, k.FontB, k.Region));
    }

    /// <summary>A font engine that chokes on some character or font must cost a '?', never the connection.</summary>
    private static GlyphResult SafeDraw(Rune rune, bool bold, bool fontB, TextRegion region)
    {
        try
        {
            return Draw(rune, bold, fontB, region);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Question(bold, fontB);
        }
    }


    private static FontFamily? Family(string name) =>
        Families.GetOrAdd(name, n => SystemFonts.TryGet(n, out var family) ? family : null);

    private static readonly ConcurrentDictionary<(string Family, int Codepoint), bool> Coverage = new();

    /// <summary>Whether the family has a real glyph for the character (cached: asking creates a font each time).</summary>
    private static bool HasGlyph(FontFamily family, Rune rune) =>
        Coverage.GetOrAdd((family.Name, rune.Value), key =>
        {
            var font = family.CreateFont(12);
            return font.TryGetGlyphs(new CodePoint(key.Codepoint), out var glyphs) && glyphs.Count > 0
                && glyphs.All(g => g.GlyphMetrics.GlyphType != GlyphType.Fallback);
        });

    private static FontFamily? FindFamily(Rune rune, IEnumerable<string> names)
    {
        foreach (var name in names)
            if (Family(name) is { } family && HasGlyph(family, rune))
                return family;
        return null;
    }

    private static GlyphResult Draw(Rune rune, bool bold, bool fontB, TextRegion region)
    {
        var cellW = CellWidth(fontB);
        var cellH = CellHeight(fontB);
        if (IsWide(rune))
        {
            if (FindFamily(rune, [.. Regional[region], .. Symbols]) is { } found)
                return new GlyphResult(DrawCentered(found, rune.ToString(), bold, 2 * cellW, cellH, Math.Min(cellH * 0.92f, 2f * cellW)), 2);
            return Question(bold, fontB);
        }
        if (HasGlyph(Base.Value, rune))
            return new GlyphResult(DrawBase(rune.ToString(), bold, fontB), 1);
        // Not in the monospaced font: a symbol font, scaled into one cell, before giving up with '?'.
        if (FindFamily(rune, [.. Symbols, .. Regional[region]]) is { } symbol)
            return new GlyphResult(DrawCentered(symbol, rune.ToString(), bold, cellW, cellH, FitToCell(symbol, rune.ToString(), cellW, cellH)), 1);
        return Question(bold, fontB);
    }


    /// <summary>The size that fills the cell height, reduced if the glyph is then wider than the cell (half-width kana fit as they are).</summary>
    private static float FitToCell(FontFamily family, string text, int cellW, int cellH)
    {
        var size = cellH * 0.92f;
        var advance = TextMeasurer.MeasureAdvance(text, new TextOptions(family.CreateFont(size)) { Dpi = 72 }).Width;
        return advance > cellW ? size * cellW / advance : size;
    }

    private static readonly ConcurrentDictionary<(bool Bold, bool FontB), GlyphResult> Questions = new();

    /// <summary>The '?' for characters no font has: drawn once per weight and font, not once per character.</summary>
    private static GlyphResult Question(bool bold, bool fontB) =>
        Questions.GetOrAdd((bold, fontB), key => new GlyphResult(DrawBase("?", key.Bold, key.FontB), 1));

    private const string FitText = "ÁÉÑgjpqy";

    private static readonly ConcurrentDictionary<(bool Bold, bool FontB), (Font Font, float Advance, float OffsetY)> Fits = new();

    private static MonoBitmap DrawBase(string text, bool bold, bool fontB)
    {
        var cellW = CellWidth(fontB);
        var cellH = CellHeight(fontB);
        var (font, advance, offsetY) = Fits.GetOrAdd((bold, fontB), key =>
        {
            // Measure at a reference size to find the size whose advance and ink height fit the cell.
            var style = key.Bold ? FontStyle.Bold : FontStyle.Regular;
            var reference = Base.Value.CreateFont(100, style);
            var options = new TextOptions(reference) { Dpi = 72 };
            var advanceRatio = TextMeasurer.MeasureAdvance("M", options).Width / 100f;
            var ink = TextMeasurer.MeasureBounds(FitText, options);
            var size = Math.Min(CellWidth(key.FontB) / advanceRatio, CellHeight(key.FontB) * 100f / Math.Max(1f, ink.Height));
            return (Base.Value.CreateFont(size, style), advanceRatio * size, -ink.Top * size / 100f);
        });
        using var image = new Image<L8>(cellW, cellH, new L8(255));
        var drawOptions = new RichTextOptions(font) { Dpi = 72, Origin = new PointF((cellW - advance) / 2, offsetY) };
        image.Mutate(c => c.DrawText(drawOptions, text, Color.Black));
        return TextRenderer.ToMono(image, 160);
    }

    /// <summary>Draws a glyph centred in a w × h cell (the line box is centred, so baselines agree between characters).</summary>
    private static MonoBitmap DrawCentered(FontFamily family, string text, bool bold, int w, int h, float size)
    {
        var font = family.CreateFont(size, FontStyle.Regular);
        using var image = new Image<L8>(w, h, new L8(255));
        var options = new RichTextOptions(font)
        {
            Dpi = 72,
            Origin = new PointF(w / 2f, h / 2f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        image.Mutate(c => c.DrawText(options, text, Color.Black));
        var mono = TextRenderer.ToMono(image, 215);   // thin strokes (dakuten, long-vowel bars) must survive the threshold
        if (!bold)
            return mono;
        var heavier = new MonoBitmap(w, h);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            heavier[x, y] = mono[x, y] || (x > 0 && mono[x - 1, y]);
        return heavier;
    }
}
