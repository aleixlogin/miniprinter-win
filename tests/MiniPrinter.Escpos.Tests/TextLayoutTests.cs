using System.Text;
using MiniPrinter.Escpos;

namespace MiniPrinter.Escpos.Tests;

/// <summary>escpos-emulation: cell layout, character tables, styles, alignment, feeds and cuts.</summary>
public class TextLayoutTests
{
    [Fact]
    public void A_full_line_of_32_characters_fits_a_row_and_the_ticket_is_384_wide()
    {
        var ticket = new Esc().Init().Line(new string('W', 32)).Single();
        Assert.Equal(384, ticket.Width);
        Assert.InRange(ticket.Height, 12, 24);        // a single 24-dot cell row (the blank gap after it is trimmed)
        var (left, right) = Esc.InkColumns(ticket);
        Assert.InRange(left, 0, 3);
        Assert.InRange(right, 380, 383);
        Esc.Dump(ticket, "full-line");
    }

    [Fact]
    public void A_line_longer_than_32_columns_continues_on_the_next_line()
    {
        var one = new Esc().Init().Line(new string('A', 32)).Single();
        var ticket = new Esc().Init().Line(new string('A', 40)).Single();
        Assert.Equal(one.Height + 30, ticket.Height);   // one more row of 24 dots + 6 of gap
        // 8 letters on the second row: ink only in the left 8 cells.
        var (_, right) = Esc.InkColumns(ticket, top: 30);
        Assert.InRange(right, 8 * 12 - 12, 8 * 12);
    }

    [Fact]
    public void Two_columns_of_a_receipt_line_are_aligned_to_the_right_edge()
    {
        var line = "Cafe" + new string(' ', 32 - 4 - 4) + "1,50";
        var ticket = new Esc().Init().Line(line).Line("Te" + new string(' ', 32 - 2 - 4) + "2,10").Single();
        var (_, rightFirst) = Esc.InkColumns(ticket, 0, 24);
        var (_, rightSecond) = Esc.InkColumns(ticket, 30, 24);
        Assert.InRange(rightFirst, 384 - 12, 383);
        Assert.InRange(rightSecond, 384 - 12, 383);
        Esc.Dump(ticket, "two-columns");
    }

    [Fact]
    public void Font_B_fits_42_columns()
    {
        var ticket = new Esc().Init().FontB(true).Line(new string('W', 42)).Single();
        Assert.InRange(ticket.Height, 8, 17);         // 42 × 9 = 378 dots: still one row
        var wrapped = new Esc().Init().FontB(true).Line(new string('W', 43)).Single();
        Assert.Equal(ticket.Height + 17 + 6, wrapped.Height);
    }

    [Fact]
    public void Text_is_drawn_in_black_dots_only_where_cells_have_characters()
    {
        var ticket = new Esc().Init().Line("HOLA").Single();
        var (left, right) = Esc.InkColumns(ticket);
        Assert.InRange(left, 0, 6);
        Assert.InRange(right, 4 * 12 - 8, 4 * 12);
    }

    [Fact]
    public void Centered_and_right_aligned_lines_move_the_whole_line()
    {
        var left = new Esc().Init().Align(0).Line("ABCD").Single();
        var center = new Esc().Init().Align(1).Line("ABCD").Single();
        var right = new Esc().Init().Align(2).Line("ABCD").Single();
        var (l0, _) = Esc.InkColumns(left);
        var (c0, c1) = Esc.InkColumns(center);
        var (r0, r1) = Esc.InkColumns(right);
        Assert.InRange((c0 + c1) / 2, 384 / 2 - 6, 384 / 2 + 6);
        Assert.InRange(r1, 384 - 12, 383);
        Assert.True(l0 < c0 && c0 < r0);
    }

    [Fact]
    public void Double_width_and_height_double_the_size_of_the_characters()
    {
        var normal = new Esc().Init().Line("A").Single();
        var doubled = new Esc().Init().Size(2, 2).Line("A").Single();
        Assert.InRange(doubled.Height, 2 * normal.Height - 2, 2 * normal.Height + 2);
        var (_, nr) = Esc.InkColumns(normal);
        var (_, dr) = Esc.InkColumns(doubled);
        Assert.InRange(dr, 2 * nr - 2, 2 * nr + 2);
        Assert.Equal(4 * Esc.InkCount(normal), Esc.InkCount(doubled));
    }

    [Fact]
    public void Inverse_prints_white_on_a_black_cell()
    {
        var plain = new Esc().Init().Line("A").Single();
        var inverse = new Esc().Init().Inverse(true).Line("A").Single();
        Assert.True(Esc.InkCount(inverse, 0, 0, 12, 24) > 12 * 24 / 2);
        Assert.True(Esc.InkCount(plain, 0, 0, 12, 24) < 12 * 24 / 2);
        // A space is a solid black cell.
        var space = new Esc().Init().Inverse(true).Line(" ").Single();
        Assert.Equal(12 * 24, Esc.InkCount(space, 0, 0, 12, 24));
    }

    [Fact]
    public void Underline_draws_a_line_under_the_cell_and_bold_adds_ink()
    {
        var plain = new Esc().Init().Line("i").Single();
        var underlined = new Esc().Init().Underline(2).Line("i").Single();
        Assert.Equal(2 * 12, Esc.InkCount(underlined, 0, 22, 12, 2));   // the two last rows of the cell are solid
        Assert.Equal(0, Esc.InkCount(plain, 0, 22, 12, 2));
        var bold = new Esc().Init().Bold(true).Line("i").Single();
        Assert.True(Esc.InkCount(bold) >= Esc.InkCount(plain));
    }

    [Fact]
    public void Esc_at_resets_the_styles()
    {
        var ticket = new Esc().Init().Size(2, 2).Bold(true).Align(2).Init().Line("A").Single();
        var plain = new Esc().Init().Line("A").Single();
        Assert.Equal(plain.Height, ticket.Height);
        Assert.Equal(Esc.InkCount(plain), Esc.InkCount(ticket));
        Assert.Equal(Esc.InkColumns(plain), Esc.InkColumns(ticket));
    }

    [Fact]
    public void Cp858_prints_the_euro_sign_and_cp437_the_box_drawing()
    {
        var euro = new Esc().Init().Raw(0x1B, 't', 19, 0xD5).Raw(0x0A).Single();   // CP858: 0xD5 = euro
        var asEuro = new Esc().Init().Line("€", Encoding.UTF8).Single();
        Assert.Equal(Esc.InkCount(asEuro), Esc.InkCount(euro));
        var box = new Esc().Init().Raw(0xC9, 0xCD, 0xBB, 0x0A).Single();            // CP437 ╔═╗
        Assert.True(Esc.InkCount(box) > 100);
    }

    [Fact]
    public void Utf8_text_is_decoded_as_utf8()
    {
        var utf8 = new Esc().Init().Line("ñ€é", Encoding.UTF8).Single();
        var cp1252 = new Esc().Init().Raw(0x1B, 't', 16).Raw(0xF1, 0x80, 0xE9, 0x0A).Single();
        Assert.Equal(Esc.InkCount(cp1252), Esc.InkCount(utf8));
        Assert.Equal(Esc.InkColumns(cp1252), Esc.InkColumns(utf8));
    }

    [Fact]
    public void A_character_the_font_cannot_draw_prints_a_question_mark()
    {
        var missing = new Esc().Init().Line("͸", Encoding.UTF8).Single();      // unassigned code point: no font has it
        var question = new Esc().Init().Line("?").Single();
        Assert.Equal(Esc.InkCount(question), Esc.InkCount(missing));
    }

    [Fact]
    public void Feeds_add_blank_lines_and_trailing_blank_rows_are_dropped()
    {
        var ticket = new Esc().Init().Line("A").Feed(3).Line("B").Feed(5).Single();
        var plainAb = new Esc().Init().Line("A").Line("B").Single();
        Assert.Equal(plainAb.Height + 3 * 30, ticket.Height);   // ESC d 3 adds three lines; the last feed is trimmed
        Assert.False(ticket.IsRowBlank(ticket.Height - 1));
        var dots = new Esc().Init().Line("A").Raw(0x1B, 'J', 40).Line("B").Single();
        var none = new Esc().Init().Line("A").Line("B").Single();
        Assert.Equal(none.Height + 40, dots.Height);
    }

    [Fact]
    public void Line_spacing_commands_change_the_pitch()
    {
        var tight = new Esc().Init().Raw(0x1B, '3', 24).Line("A").Line("B").Single();
        var normal = new Esc().Init().Line("A").Line("B").Single();
        Assert.Equal(normal.Height - 6, tight.Height);          // pitch 24 instead of 30
        var back = new Esc().Init().Raw(0x1B, '3', 24).Raw(0x1B, '2').Line("A").Line("B").Single();
        Assert.Equal(normal.Height, back.Height);
    }

    [Fact]
    public void A_cut_ends_the_ticket_and_the_rest_starts_another()
    {
        var tickets = new Esc().Init().Line("UNO").Cut().Line("DOS").Line("DOS").Cut().Run();
        Assert.Equal(2, tickets.Count);
        Assert.True(tickets[1].Height > tickets[0].Height);
    }

    [Fact]
    public void A_ticket_without_ink_is_not_published()
    {
        Assert.Empty(new Esc().Init().Feed(5).Line("   ").Cut().Run());
        Assert.Empty(new Esc().Init().Run());
    }

    [Fact]
    public void Unknown_commands_are_skipped_and_never_printed_as_text()
    {
        var withUnknown = new Esc().Init().Line("UNO").Raw(0x1B, 'Z').Raw(0x1D, 0x7E).Line("DOS").Single();
        var plain = new Esc().Init().Line("UNO").Line("DOS").Single();
        Assert.Equal(plain.Height, withUnknown.Height);
        Assert.Equal(Esc.InkCount(plain), Esc.InkCount(withUnknown));
    }


    [Fact]
    public void Page_mode_user_characters_and_nv_images_leave_no_parameters_behind_as_text()
    {
        var commands = new Esc().Init().Line("ANTES")
            .Raw(0x1B, 'L').Raw(0x1B, 'W', 0, 0, 0, 0, 128, 0, 100, 0).Raw(0x1B, 'T', 1).Raw(0x1B, 0x0C).Raw(0x1B, 'S')
            .Raw(0x1B, '&', 3, 0x20, 0x21, 2, 1, 2, 3, 4, 5, 6, 3, 9, 8, 7, 6, 5, 4, 3, 2, 1)   // two 24-dot characters
            .Raw(0x1C, 'p', 1, 0).Raw(0x1C, 'q', 1, 1, 0, 1, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF)
            .Line("DESPUES").Single();
        var plain = new Esc().Init().Line("ANTES").Line("DESPUES").Single();
        Assert.Equal(plain.Height, commands.Height);
        Assert.Equal(Esc.InkCount(plain), Esc.InkCount(commands));
    }

    [Fact]
    public void Ignored_commands_such_as_the_cash_drawer_do_not_affect_the_ticket()
    {
        var withDrawer = new Esc().Init().Raw(0x1B, 'p', 0, 25, 250).Line("UNO").Raw(0x1B, '=', 1).Raw(0x1D, 'P', 0, 0).Line("DOS").Single();
        var plain = new Esc().Init().Line("UNO").Line("DOS").Single();
        Assert.Equal(Esc.InkCount(plain), Esc.InkCount(withDrawer));
    }

    [Fact]
    public void Splitting_the_stream_in_any_way_gives_the_same_ticket()
    {
        var stream = new Esc().Init().Align(1).Bold(true).Line("TIENDA ñandú", Encoding.UTF8).Bold(false)
            .Align(0).Line("Cafe             1,50").Qr("https://example.com", 4).Barcode(73, "{BABC123").Cut().Bytes;
        var whole = Assert.Single(Esc.Run(stream));
        foreach (var chunk in new[] { 1, 2, 3, 7 })
        {
            var split = Assert.Single(Esc.Run(stream, chunk: chunk));
            Assert.Equal(whole.Height, split.Height);
            Assert.Equal(Esc.InkCount(whole), Esc.InkCount(split));
        }
    }

    [Fact]
    public void Tab_moves_to_the_next_eight_columns()
    {
        var ticket = new Esc().Init().Text("A").Raw(0x09).Line("B").Single();
        var (_, right) = Esc.InkColumns(ticket);
        Assert.InRange(right, 8 * 12, 9 * 12);
    }

    [Fact]
    public void A_very_long_feed_splits_the_ticket_instead_of_growing_without_limit()
    {
        var esc = new Esc().Init().Line("A");
        for (var i = 0; i < 200; i++)
            esc.Feed(255).Line("x");
        var tickets = esc.Run();
        Assert.True(tickets.Count > 1);
        Assert.All(tickets, t => Assert.True(t.Height <= EscposInterpreter.MaxTicketRows + 8000));
    }
}
