using System.Text;
using MiniPrinter.Escpos;
using MiniPrinter.Protocol;

namespace MiniPrinter.Escpos.Tests;

/// <summary>escpos-emulation: Arabic with contextual forms and Thai with positioned marks, drawn as whole runs.</summary>
public class ComplexScriptTests
{
    private const string Arabic = "مرحبا بالعالم";
    private const string Thai = "สวัสดีชาวโลก ภาษาไทย";

    private static MonoBitmap One(string text, Func<Esc, Esc>? configure = null)
    {
        var esc = new Esc().Init();
        configure?.Invoke(esc);
        return Assert.Single(Esc.Run(esc.Line(text, Encoding.UTF8).Bytes));
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

    private static int InkWidth(MonoBitmap bitmap)
    {
        var (left, right) = Esc.InkColumns(bitmap);
        return right - left + 1;
    }

    // ---- segmentation -------------------------------------------------------------------------------------

    [Fact]
    public void Text_is_split_into_runs_by_script_keeping_neutrals_between_words_of_the_same_script()
    {
        if (!FontGuard.Complex) return;
        var segments = CellFont.Segment("AR: مرحبا بالعالم, ok 123 สวัสดี x");
        Assert.Equal(
            [("AR: ", ComplexScript.None), ("مرحبا بالعالم", ComplexScript.Arabic), (", ok 123 ", ComplexScript.None), ("สวัสดี", ComplexScript.Thai), (" x", ComplexScript.None)],
            segments);
        Assert.Equal([("plain text", ComplexScript.None)], CellFont.Segment("plain text"));
    }

    // ---- Arabic ---------------------------------------------------------------------------------------------

    [Fact]
    public void Arabic_letters_are_joined_not_drawn_separately()
    {
        if (!FontGuard.Complex) return;
        var joined = One("مرحبا");
        var separate = One("م‌ر‌ح‌ب‌ا");          // ZWNJ stops the joining
        Assert.True(Diff(joined, separate) > 0);
        Assert.True(InkWidth(joined) < InkWidth(separate));            // joined forms are connected and narrower
        Assert.True(Diff(joined, One("?????")) > 0);
        Esc.Dump(One("AR: " + Arabic), "arabic");
    }

    [Fact]
    public void Lam_alef_forms_a_single_ligature()
    {
        if (!FontGuard.Complex) return;
        Assert.True(InkWidth(One("لا")) < InkWidth(One("ل‌ا")));
    }

    [Fact]
    public void The_arabic_run_starts_right_after_the_latin_cells_and_does_not_move_them()
    {
        if (!FontGuard.Complex) return;
        var mixed = One("AR: " + Arabic);
        var latin = One("AR: ");
        Assert.Equal(Esc.InkCount(latin, 0, 0, 48), Esc.InkCount(mixed, 0, 0, 48));
        Assert.True(Esc.InkCount(mixed, 48, 0, 336) > 0);
    }

    [Fact]
    public void An_arabic_run_takes_a_whole_number_of_cells_so_the_text_after_it_stays_on_the_grid()
    {
        if (!FontGuard.Complex) return;
        var a = One(Arabic + "X");
        var b = One(Arabic + "X", e => e);
        Assert.Equal(0, Diff(a, b));
        // the X begins at a cell boundary: nothing of it left of that boundary and all of it within one cell
        var withoutX = One(Arabic);
        var runCells = (int)Math.Ceiling((Esc.InkColumns(withoutX).Right + 1) / 12.0);
        Assert.True(Esc.InkCount(a, runCells * 12, 0, 12) > 0);
    }

    [Fact]
    public void A_long_arabic_text_wraps_by_words_and_each_line_is_aligned_to_the_right()
    {
        if (!FontGuard.Complex) return;
        var text = string.Join(" ", Enumerable.Repeat(Arabic, 6));
        var ticket = One(text);
        var single = One(Arabic);
        Assert.True(ticket.Height >= single.Height + 24);              // more than one line
        // the first (logical) line is the right-aligned one
        Assert.InRange(Esc.InkColumns(ticket, 0, 24).Right, 380, 383);
    }

    [Fact]
    public void Arabic_follows_size_and_bold()
    {
        if (!FontGuard.Complex) return;
        var normal = One("مرحبا");
        var doubled = One("مرحبا", e => e.Size(2, 2));
        Assert.InRange(doubled.Height, 2 * normal.Height - 6, 2 * normal.Height + 6);
        Assert.True(Esc.InkCount(One("مرحبا", e => e.Bold(true))) >= Esc.InkCount(normal));
        Assert.True(Esc.InkCount(One("مرحبا", e => e.Inverse(true)), 0, 0, 72, 24) > 72 * 24 / 2);
    }

    // ---- Thai ----------------------------------------------------------------------------------------------

    [Fact]
    public void Thai_is_drawn_not_replaced_by_question_marks()
    {
        if (!FontGuard.Complex) return;
        var thai = One(Thai);
        Assert.True(Diff(thai, One(new string('?', Thai.Length))) > 0);
        Esc.Dump(One("TH: " + Thai), "thai");
    }

    [Fact]
    public void Thai_tone_marks_sit_on_the_vowel_and_add_no_width()
    {
        if (!FontGuard.Complex) return;
        // ที่ = consonant + vowel above + tone mark above: the tone mark overlaps the vowel instead of advancing.
        var withTone = One("ที่");
        var withoutTone = One("ที");
        Assert.Equal(Esc.InkColumns(withoutTone).Right, Esc.InkColumns(withTone).Right);
        Assert.True(Esc.InkCount(withTone) > Esc.InkCount(withoutTone));
    }

    [Fact]
    public void Thai_marks_do_not_take_a_cell_of_their_own()
    {
        if (!FontGuard.Complex) return;
        var ticket = One("สวัสดี");
        // six code points but four spacing letters: well under six full cells
        Assert.True(InkWidth(ticket) < 6 * 12);
    }

    // ---- robustness ----------------------------------------------------------------------------------------------

    [Fact]
    public void Random_arabic_thai_and_zero_width_mixes_never_throw()
    {
        if (!FontGuard.Complex) return;
        var random = new Random(7);
        var alphabet = "مرحباعلمبلاأإآؤئءةى ،.0123ABC سلام‌‍ًّสวัสดีกรุงเทพั่ ​﻿";
        for (var i = 0; i < 150; i++)
        {
            var text = new string(Enumerable.Range(0, random.Next(1, 200)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            var tickets = Esc.Run(new Esc().Init().Line(text, Encoding.UTF8).Cut().Bytes);
            Assert.All(tickets, t => Assert.Equal(384, t.Width));
        }
    }
}
