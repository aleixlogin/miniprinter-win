using SixLabors.Fonts;

namespace MiniPrinter.Escpos.Tests;

/// <summary>
/// The CJK, Arabic and Thai tests need the Windows fonts for those scripts. A CI runner (or a stripped-down
/// install) may not have them; the tests then do nothing instead of failing on a missing font.
/// </summary>
internal static class FontGuard
{
    private static bool Has(string name) => SystemFonts.TryGet(name, out _);

    public static bool Cjk { get; } = Has("MS Gothic") && Has("Microsoft YaHei") && Has("Malgun Gothic") && Has("Segoe UI Symbol");

    public static bool Complex { get; } = Has("Tahoma") && Has("Leelawadee UI");
}
