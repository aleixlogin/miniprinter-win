using MiniPrinter.Escpos;
using MiniPrinter.Protocol;

namespace MiniPrinter.Escpos.Tests;

/// <summary>escpos-emulation: raster and bit images, QR codes, barcodes and status replies.</summary>
public class ImagesCodesStatusTests
{
    private static MonoBitmap Checker(int width, int height)
    {
        var image = new MonoBitmap(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            image[x, y] = ((x / 4) + (y / 4)) % 2 == 0;
        return image;
    }

    // ---- images ----------------------------------------------------------------------------------

    [Fact]
    public void A_raster_logo_keeps_its_width_and_height()
    {
        var logo = Checker(256, 100);
        var ticket = new Esc().Init().Raster(logo).Single();
        Assert.Equal(100, ticket.Height);
        for (var y = 0; y < 100; y += 7)
        for (var x = 0; x < 256; x += 5)
            Assert.Equal(logo[x, y], ticket[x, y]);          // left aligned, dot for dot
        Assert.Equal(0, Esc.InkCount(ticket, 256, 0, 128));
    }

    [Fact]
    public void Images_follow_the_alignment()
    {
        var logo = Checker(128, 32);
        var center = new Esc().Init().Align(1).Raster(logo).Single();
        var right = new Esc().Init().Align(2).Raster(logo).Single();
        Assert.Equal((384 - 128) / 2, Esc.InkColumns(center).Left - FirstInk(logo));
        Assert.Equal(384 - 128, Esc.InkColumns(right).Left - FirstInk(logo));
    }

    private static int FirstInk(MonoBitmap image) => Esc.InkColumns(image).Left;

    [Fact]
    public void Double_modes_scale_the_raster_image()
    {
        var logo = Checker(64, 16);
        var wide = new Esc().Init().Raster(logo, mode: 1).Single();
        var tall = new Esc().Init().Raster(logo, mode: 2).Single();
        var quad = new Esc().Init().Raster(logo, mode: 3).Single();
        Assert.Equal(16, wide.Height);
        Assert.Equal(32, tall.Height);
        Assert.Equal(32, quad.Height);
        Assert.Equal(2 * (Esc.InkColumns(logo).Right + 1), Esc.InkColumns(wide).Right + 1);
    }

    [Fact]
    public void An_image_wider_than_the_head_is_cropped_without_failing()
    {
        var wide = Checker(576, 20);
        var ticket = new Esc().Init().Raster(wide).Single();
        Assert.Equal(384, ticket.Width);
        Assert.Equal(20, ticket.Height);
        Assert.Equal(wide[100, 0], ticket[100, 0]);
    }

    [Fact]
    public void A_bit_image_stripe_of_24_dots_prints_columns_of_three_bytes()
    {
        // ESC * 33 nL nH: every column of 24 dots; set only the top and bottom dot of each column.
        var esc = new Esc().Init().Raw(0x1B, '3', 24).Raw(0x1B, '*', 33, 50, 0);
        for (var c = 0; c < 50; c++)
            esc.Raw(0x80, 0x00, 0x01);
        var ticket = esc.Raw(0x0A).Single();
        Assert.Equal(24, ticket.Height);
        for (var x = 0; x < 50; x++)
        {
            Assert.True(ticket[x, 0]);
            Assert.True(ticket[x, 23]);
            Assert.False(ticket[x, 10]);
        }
        Assert.False(ticket[50, 0]);
    }

    [Fact]
    public void A_bit_image_of_8_dots_with_single_density_is_twice_as_wide()
    {
        var esc = new Esc().Init().Raw(0x1B, '*', 0, 10, 0);
        for (var c = 0; c < 10; c++)
            esc.Raw(0xFF);
        var ticket = esc.Raw(0x0A).Single();
        Assert.Equal(20, Esc.InkColumns(ticket).Right + 1);
    }

    // ---- QR -------------------------------------------------------------------------------------

    [Fact]
    public void A_qr_code_decodes_to_the_stored_text()
    {
        var ticket = new Esc().Init().Align(1).Qr("https://example.com").Single();
        Assert.Equal("https://example.com", Esc.Decode(ticket));
        Esc.Dump(ticket, "qr");
    }

    [Theory]
    [InlineData(48)]
    [InlineData(49)]
    [InlineData(50)]
    [InlineData(51)]
    public void Every_error_correction_level_decodes(int ecc)
    {
        var ticket = new Esc().Init().Qr("Mesa 4: 2x café", module: 5, ecc: ecc).Single();
        Assert.Equal("Mesa 4: 2x café", Esc.Decode(ticket));
    }

    [Fact]
    public void The_module_size_scales_the_qr_code()
    {
        var small = new Esc().Init().Qr("hola", module: 3).Single();
        var large = new Esc().Init().Qr("hola", module: 8).Single();
        Assert.True(large.Height > 2 * small.Height - 10);
    }

    [Fact]
    public void A_qr_code_too_big_for_the_paper_shrinks_to_three_dots_per_module_or_is_omitted()
    {
        var text = new string('x', 120);                       // 53 modules at M: fits at 3 dots (159) but not at 16
        var fitted = new Esc().Init().Qr(text, module: 16).Single();
        Assert.Equal(text, Esc.Decode(fitted));
        Assert.True(fitted.Width == 384);

        var huge = new string((char)120, 1500);                // beyond 127 modules at M: never fits in 384 dots
        Assert.Empty(new Esc().Init().Qr(huge, module: 3).Cut().Run());
        // and the rest of the ticket still prints
        Assert.Single(new Esc().Init().Line("antes").Qr(huge).Line("despues").Cut().Run());
    }

    // ---- barcodes ---------------------------------------------------------------------------------

    [Fact]
    public void An_ean13_barcode_is_printed_with_its_check_digit()
    {
        var ticket = new Esc().Init().Barcode(67, "590123412345", height: 60, width: 2, textPosition: 0).Single();
        Assert.Equal("5901234123457", Esc.Decode(ticket));
    }

    [Fact]
    public void A_code128_barcode_with_the_set_selector_decodes()
    {
        var ticket = new Esc().Init().Barcode(73, "{BMINI-0042", height: 60, width: 2, textPosition: 0).Single();
        Assert.Equal("MINI-0042", Esc.Decode(ticket));
    }

    [Fact]
    public void Code39_and_upca_in_the_null_terminated_format_decode()
    {
        var code39 = new Esc().Init().Raw(0x1D, 'h', 60, 0x1D, 'w', 2, 0x1D, 'H', 0).Raw(0x1D, 'k', 4).Text("HOLA-12").Raw(0).Single();
        Assert.Equal("HOLA-12", Esc.Decode(code39));
        var upca = new Esc().Init().Raw(0x1D, 'h', 60, 0x1D, 'w', 2, 0x1D, 'H', 0).Raw(0x1D, 'k', 0).Text("03600029145").Raw(0).Single();
        Assert.Equal("036000291452", Esc.Decode(upca));
    }

    [Fact]
    public void The_height_and_the_text_position_change_the_barcode()
    {
        var none = new Esc().Init().Barcode(73, "{BABC", height: 50, textPosition: 0).Single();
        var below = new Esc().Init().Barcode(73, "{BABC", height: 50, textPosition: 2).Single();
        var both = new Esc().Init().Barcode(73, "{BABC", height: 50, textPosition: 3).Single();
        var tall = new Esc().Init().Barcode(73, "{BABC", height: 100, textPosition: 0).Single();
        Assert.True(below.Height > none.Height + 8);           // the printed digits under the bars
        Assert.True(both.Height > below.Height + 20);          // and over them
        Assert.Equal(none.Height + 50, tall.Height);
    }

    [Fact]
    public void An_invalid_barcode_is_omitted_and_the_ticket_continues()
    {
        var ticket = new Esc().Init().Line("A").Raw(0x1D, 'k', 67, 5).Text("12345").Line("B").Single();   // EAN-13 needs 12 digits
        var plain = new Esc().Init().Line("A").Line("B").Single();
        Assert.Equal(plain.Height, ticket.Height);
    }

    // ---- other 2D symbols ---------------------------------------------------------------------------

    [Theory]
    [InlineData(54, "MINIPRINTER-0042")]
    [InlineData(54, "https://example.com/dm?x=1")]
    [InlineData(48, "PDF417 ticket 0042")]
    [InlineData(53, "AZTEC-0042")]
    public void Native_2d_symbols_decode_to_the_stored_text(int cn, string text)
    {
        var ticket = new Esc().Init().Align(1).Symbol(cn, text).Single();
        Assert.Contains(text, Esc.DecodeAll(ticket));
        Esc.Dump(ticket, $"symbol-{cn}");
    }

    [Fact]
    public void The_module_size_scales_a_data_matrix()
    {
        var small = new Esc().Init().Symbol(54, "hola", module: 3).Single();
        var large = new Esc().Init().Symbol(54, "hola", module: 8).Single();
        Assert.True(large.Height > small.Height * 2);
    }

    [Fact]
    public void A_2d_symbol_that_does_not_fit_or_cannot_be_encoded_is_omitted_and_the_ticket_continues()
    {
        var huge = new string('x', 5000);
        var ticket = new Esc().Init().Line("antes").Symbol(54, huge).Symbol(53, huge).Line("despues").Single();
        var plain = new Esc().Init().Line("antes").Line("despues").Single();
        Assert.Equal(plain.Height, ticket.Height);
    }


    // ---- status ----------------------------------------------------------------------------------

    private static byte[] Replies(byte[] request, PrinterCondition? status)
    {
        var interpreter = new EscposInterpreter(() => status);
        interpreter.Feed(request);
        return interpreter.DrainResponses();
    }

    [Theory]
    [InlineData(1, false, false, false, 0x12)]
    [InlineData(2, false, false, false, 0x12)]
    [InlineData(2, false, true, false, 0x32)]
    [InlineData(3, false, false, false, 0x12)]
    [InlineData(3, false, false, true, 0x72)]
    [InlineData(4, false, false, false, 0x12)]
    [InlineData(4, true, false, false, 0x72)]
    public void Dle_eot_is_answered_from_the_printer_status(int n, bool paperOut, bool cover, bool error, int expected)
    {
        var reply = Replies([0x10, 0x04, (byte)n], new PrinterCondition(paperOut, cover, error));
        Assert.Equal([(byte)expected], reply);
    }

    [Fact]
    public void Gs_r_reports_the_paper_sensor()
    {
        Assert.Equal([(byte)0x00], Replies([0x1D, (byte)'r', 1], new PrinterCondition()));
        Assert.Equal([(byte)0x0C], Replies([0x1D, (byte)'r', 1], new PrinterCondition(PaperOut: true)));
    }

    [Fact]
    public void Without_a_status_the_printer_answers_online_without_waiting()
    {
        Assert.Equal([(byte)0x12], Replies([0x10, 0x04, 4], null));
    }

    [Fact]
    public void Several_requests_are_answered_in_order_and_a_split_request_waits_for_its_last_byte()
    {
        var interpreter = new EscposInterpreter(() => new PrinterCondition(PaperOut: true));
        interpreter.Feed([0x10, 0x04]);
        Assert.Empty(interpreter.DrainResponses());
        interpreter.Feed([4, 0x10, 0x04, 1]);
        Assert.Equal([(byte)0x72, (byte)0x12], interpreter.DrainResponses());
    }

    [Fact]
    public void Status_requests_do_not_end_or_alter_the_ticket()
    {
        var interpreter = new EscposInterpreter();
        interpreter.Feed(new Esc().Init().Line("A").Raw(0x10, 0x04, 4).Bytes);
        interpreter.Feed(new Esc().Line("B").Bytes);
        interpreter.EndTicket();
        Assert.Single(interpreter.DrainTickets());
    }
}
