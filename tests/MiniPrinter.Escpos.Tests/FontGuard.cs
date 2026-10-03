using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;

namespace MiniPrinter.Escpos.Tests;

/// <summary>
/// The CJK, Arabic and Thai tests need the Windows fonts for those scripts, with real glyphs for them. A CI
/// runner (or a stripped-down install) may lack them or ship other versions; the tests then do nothing
/// instead of failing on a missing font.
/// </summary>
internal static class FontGuard
{
    private static bool Covers(string name, params int[] codePoints)
    {
        if (!SystemFonts.TryGet(name, out var family))
            return false;
        var font = family.CreateFont(12);
        return codePoints.All(cp => font.TryGetGlyphs(new CodePoint(cp), out var glyphs) && glyphs.Count > 0
            && glyphs.All(g => g.GlyphMetrics.GlyphType != GlyphType.Fallback));
    }

    public static bool Cjk { get; } =
        Covers("MS Gothic", 0x65E5, 0x30C6, 0xFF71) && Covers("Microsoft YaHei", 0x4F60, 0x7B80) && Covers("Malgun Gothic", 0xC548) && Covers("Segoe UI Symbol", 0x2615);

    public static bool Complex { get; } = Covers("Tahoma", 0x0645, 0x0644) && Covers("Leelawadee UI", 0x0E01, 0x0E35, 0x0E48);
}
