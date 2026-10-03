using System.Text;
using MiniPrinter.Escpos;
using MiniPrinter.Protocol;

namespace MiniPrinter.Escpos.Tests;

/// <summary>escpos-emulation: full-width characters (two cells), kanji mode, symbols, composition and zero-width characters.</summary>
public class CjkTests
{
    static CjkTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static MonoBitmap One(string text, Func<Esc, Esc>? configure = null, int kanjiCodePage = 932)
    {
        var esc = new Esc().Init();
        configure?.Invoke(esc);
        return Assert.Single(Esc.Run(esc.Line(text, Encoding.UTF8).Bytes, kanjiCodePage: kanjiCodePage));
    }

    private static int Diff(MonoBitmap a, MonoBitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
            return int.MaxValue;
        var diff = 0;
        for (var y = 0; y < a.Height; y++)
        for (var x = 0; x < a.Width; x++)
            if (a[x, y] != b[x, y]) diff++;
        return diff;
    }

    private static int[] Bytes(string text, int codePage) => Encoding.GetEncoding(codePage).GetBytes(text).Select(b => (int)b).ToArray();

    [Theory]
    [InlineData("こんにちは世界")]
    [InlineData("你好，世界 简体中文")]
    [InlineData("繁體中文")]
    [InlineData("안녕하세요")]
    public void Full_width_text_is_drawn_not_replaced_by_question_marks(string text)
    {
        if (!FontGuard.Cjk) return;
        var drawn = One(text);
        var questions = One(new string('?', text.Length));
        Assert.True(Diff(drawn, questions) > 0);
        Esc.Dump(drawn, "cjk-" + text.Length);
        // two cells (24 dots) per character, the space of the Chinese sample one cell
        var cells = text.Sum(c => c == ' ' ? 1 : c == '，' ? 2 : 2);
        var (_, right) = Esc.InkColumns(drawn);
        Assert.InRange(right, 12 * cells - 30, 12 * cells);
    }

    [Fact]
    public void Each_full_width_character_advances_24_dots_and_the_latin_columns_stay_aligned()
    {
        if (!FontGuard.Cjk) return;
        var mixed = One("A日B本C");
        var (_, rightMixed) = Esc.InkColumns(mixed);
        Assert.InRange(rightMixed, 12 + 24 + 12 + 24 + 12 - 12, 12 + 24 + 12 + 24 + 12);   // A(12) 日(24) B(12) 本(24) C(12)
        Assert.True(Esc.InkCount(mixed, 36, 0, 12) > 0);                                     // B sits in columns 36..48
    }

    [Fact]
    public void A_line_of_16_wide_characters_fits_and_17_wrap()
    {
        if (!FontGuard.Cjk) return;
        var sixteen = One(new string('日', 16));
        var seventeen = One(new string('日', 17));
        Assert.Equal(sixteen.Height + 30, seventeen.Height);
    }

    [Fact]
    public void Wide_characters_follow_size_inverse_and_bold()
    {
        if (!FontGuard.Cjk) return;
        var normal = One("日本");
        var doubled = One("日本", e => e.Size(2, 2));
        Assert.InRange(doubled.Height, 2 * normal.Height - 4, 2 * normal.Height + 4);
        var inverse = One("日本", e => e.Inverse(true));
        Assert.True(Esc.InkCount(inverse, 0, 0, 48, 24) > 48 * 24 / 2);
        var bold = One("日本", e => e.Bold(true));
        Assert.True(Esc.InkCount(bold) >= Esc.InkCount(normal));
    }

    [Fact]
    public void Kana_select_the_japanese_forms()
    {
        if (!FontGuard.Cjk) return;
        // The same ideograph looks different in the Japanese and Chinese fonts.
        Assert.True(Diff(One("直ぁ"), One("直ａ")) > 0);
    }

    // ---- kanji mode ------------------------------------------------------------------------------------

    [Fact]
    public void Shift_jis_in_kanji_mode_prints_the_same_as_utf8()
    {
        if (!FontGuard.Cjk) return;
        var utf8 = One("日本語テキスト");
        var sjis = Assert.Single(Esc.Run(new Esc().Init().Raw(0x1C, '&').Raw(Bytes("日本語テキスト", 932)).Raw(0x1C, '.').Raw(0x0A).Bytes));
        Assert.Equal(0, Diff(utf8, sjis));
    }

    [Fact]
    public void Leaving_kanji_mode_returns_to_the_code_page()
    {
        if (!FontGuard.Cjk) return;
        var expected = One("日本AB");
        var actual = Assert.Single(Esc.Run(new Esc().Init().Raw(0x1C, '&').Raw(Bytes("日本", 932)).Raw(0x1C, '.').Text("AB").Raw(0x0A).Bytes));
        Assert.Equal(0, Diff(expected, actual));
    }

    [Fact]
    public void Esc_at_cancels_kanji_mode()
    {
        if (!FontGuard.Cjk) return;
        // After ESC @ the bytes are CP437 text again, not Shift-JIS.
        var stream = new Esc().Init().Raw(0x1C, '&').Init().Raw(0x93, 0xFA, 0x0A).Bytes;
        var expected = new Esc().Init().Raw(0x93, 0xFA, 0x0A).Bytes;
        Assert.Equal(0, Diff(Assert.Single(Esc.Run(stream)), Assert.Single(Esc.Run(expected))));
    }

    [Fact]
    public void The_katakana_table_selects_cp932_for_half_width_katakana_and_shift_jis()
    {
        if (!FontGuard.Cjk) return;
        var utf8 = One("ｱｲｳｴｵ ﾊﾝｶｸ");
        var table1 = Assert.Single(Esc.Run(new Esc().Init().Raw(0x1B, 't', 1).Raw(Bytes("ｱｲｳｴｵ ﾊﾝｶｸ", 932)).Raw(0x0A).Bytes));
        Assert.Equal(0, Diff(utf8, table1));
        var withKanji = Assert.Single(Esc.Run(new Esc().Init().Raw(0x1B, 't', 1).Raw(Bytes("日本", 932)).Raw(0x0A).Bytes));
        Assert.Equal(0, Diff(One("日本"), withKanji));
    }

    [Theory]
    [InlineData(936, "你好世界")]
    [InlineData(950, "繁體中文")]
    [InlineData(949, "안녕하세요")]
    public void The_kanji_code_page_is_configurable(int codePage, string text)
    {
        if (!FontGuard.Cjk) return;
        var viaKanjiMode = Assert.Single(Esc.Run(new Esc().Init().Raw(0x1C, '&').Raw(Bytes(text, codePage)).Raw(0x1C, '.').Raw(0x0A).Bytes, kanjiCodePage: codePage));
        Assert.Equal(0, Diff(One(text, kanjiCodePage: codePage), viaKanjiMode));
    }

    [Fact]
    public void Splitting_a_double_byte_character_across_chunks_does_not_break_it()
    {
        if (!FontGuard.Cjk) return;
        var stream = new Esc().Init().Raw(0x1C, '&').Raw(Bytes("日本語", 932)).Raw(0x1C, '.').Raw(0x0A).Bytes;
        var whole = Assert.Single(Esc.Run(stream));
        foreach (var chunk in new[] { 1, 2, 3 })
            Assert.Equal(0, Diff(whole, Assert.Single(Esc.Run(stream, chunk: chunk))));
    }

    // ---- symbols, composition and zero width -------------------------------------------------------------------

    [Fact]
    public void Emoji_are_drawn_in_two_cells_with_a_symbol_font()
    {
        if (!FontGuard.Cjk) return;
        var emoji = One("😀🚀");
        Assert.True(Diff(emoji, One("????")) > 0);
        Assert.InRange(Esc.InkColumns(emoji).Right, 24 * 2 - 30, 24 * 2);
        Esc.Dump(emoji, "emoji");
    }

    [Fact]
    public void Symbols_missing_in_the_monospaced_font_use_the_symbol_font_instead_of_a_question_mark()
    {
        if (!FontGuard.Cjk) return;
        Assert.True(Diff(One("☕♨⚽⌘"), One("????")) > 0);
    }

    [Fact]
    public void Half_width_katakana_are_drawn_in_one_cell()
    {
        if (!FontGuard.Cjk) return;
        var kana = One("ｱｲｳｴｵ");
        Assert.True(Diff(kana, One("?????")) > 0);
        Assert.InRange(Esc.InkColumns(kana).Right, 5 * 12 - 12, 5 * 12);
    }

    [Fact]
    public void Hebrew_and_arabic_runs_are_put_in_visual_order()
    {
        if (!FontGuard.Cjk) return;
        // Logical order is reversed for drawing left to right; Latin text around a right-to-left run stays put.
        Assert.Equal("םולש", CellFont.VisualOrder("שלום"));
        Assert.Equal("ab דג בא cd", CellFont.VisualOrder("ab אב גד cd"));
        Assert.Equal("abc 123", CellFont.VisualOrder("abc 123"));
        Assert.True(Diff(One("שלום"), One("????")) > 0);   // drawn, not question marks
    }

    [Fact]
    public void Combining_marks_compose_into_one_character()
    {
        if (!FontGuard.Cjk) return;
        Assert.Equal(0, Diff(One("é ñ ä"), One("é ñ ä")));
    }

    [Fact]
    public void Zero_width_characters_take_no_cell()
    {
        if (!FontGuard.Cjk) return;
        Assert.Equal(0, Diff(One("a​b‌‍c️⁠d﻿"), One("abcd")));
    }

    [Fact]
    public void A_character_in_no_font_is_still_a_question_mark_of_one_cell()
    {
        if (!FontGuard.Cjk) return;
        Assert.Equal(0, Diff(One("͸"), One("?")));
    }
}
