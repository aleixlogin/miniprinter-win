using System.Runtime.CompilerServices;
using System.Text;
using MiniPrinter.Imaging;
using MiniPrinter.Protocol;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MiniPrinter.Escpos.Tests;

/// <summary>
/// Tickets written by python-escpos (tools/generate_escpos_fixtures.py) against reference rasters.
/// Set UPDATE_GOLDEN=1 to regenerate the PNGs after reviewing a rendering change by eye.
/// </summary>
public class FixtureTests
{
    private static string Dir([CallerFilePath] string? self = null) => Path.Combine(Path.GetDirectoryName(self)!, "Fixtures");

    private static IReadOnlyList<MonoBitmap> Interpret(string name) => Esc.Run(File.ReadAllBytes(Path.Combine(Dir(), name + ".bin")));

    public static TheoryData<string, int> Cases() => new()
    {
        { "receipt", 1 },
        { "styles", 1 },
        { "charset", 1 },
        { "logo-raster", 1 },
        { "logo-columns", 1 },
        { "barcodes", 1 },
        { "qr-image", 1 },
        { "two-tickets", 2 },
        { "cjk-utf8", 1 },
        { "cjk-kanji", 1 },
        { "arabic-thai", 1 },
        { "node-receipt", 1 },
        { "node-styles", 1 },
        { "node-codes", 1 },
        { "node-raw", 2 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_ticket_matches_the_reference_raster(string name, int tickets)
    {
        // CJK, Arabic and Thai come from fonts a CI runner may lack or version differently: skip without them and allow a small pixel drift.
        var scripts = name is "cjk-utf8" or "cjk-kanji" or "arabic-thai";
        if (scripts && !(FontGuard.Cjk && FontGuard.Complex))
            return;
        var result = Interpret(name);
        Assert.Equal(tickets, result.Count);
        for (var i = 0; i < result.Count; i++)
        {
            var path = Path.Combine(Dir(), tickets == 1 ? $"{name}.png" : $"{name}-{i}.png");
            if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
                MonoPng.Save(result[i], path);
            Esc.Dump(result[i], tickets == 1 ? name : $"{name}-{i}");

            using var reference = Image.Load<L8>(path);
            Assert.Equal(reference.Width, result[i].Width);
            Assert.Equal(reference.Height, result[i].Height);
            var diff = 0;
            for (var y = 0; y < result[i].Height; y++)
            for (var x = 0; x < result[i].Width; x++)
                if (result[i][x, y] != (reference[x, y].PackedValue < 128)) diff++;
            Assert.True(diff <= (scripts ? result[i].Width * result[i].Height / 50 : 0), $"{diff} dots differ from the reference");
        }
    }

    [Fact]
    public void The_receipt_has_a_readable_native_qr_code()
    {
        var ticket = Assert.Single(Interpret("receipt"));
        Assert.Contains("https://example.com/ticket/0042", Esc.DecodeAll(ticket));
    }

    [Fact]
    public void The_qr_sent_as_an_image_is_readable()
    {
        var ticket = Assert.Single(Interpret("qr-image"));
        Assert.Contains("https://example.com", Esc.DecodeAll(ticket));
    }

    [Fact]
    public void The_barcodes_of_python_escpos_decode()
    {
        var ticket = Assert.Single(Interpret("barcodes"));
        var codes = Esc.DecodeAll(ticket);
        Assert.Contains("5901234123457", codes);
        Assert.Contains("MINI-0042", codes);
        Assert.Contains("HOLA-12", codes);
    }

    [Fact]
    public void The_code_pages_python_escpos_picks_give_the_same_text_as_utf8()
    {
        // python-escpos switches tables by itself (CP437, ISO 8859-7 for the euro, CP857...): the
        // result must equal the same text sent as UTF-8.
        var expected = Assert.Single(new Esc().Init()
            .Line("Cafe: ñandú, año, € 12,50", Encoding.UTF8)
            .Line("Acentos: áéíóú ÁÉÍÓÚ ü ¿¡", Encoding.UTF8)
            .Line("Cajas: ┌──┐ │ok│ └──┘", Encoding.UTF8).Cut().Run());
        var actual = Assert.Single(Interpret("charset"));
        Assert.Equal(expected.Height, actual.Height);
        var diff = 0;
        for (var y = 0; y < actual.Height; y++)
        for (var x = 0; x < actual.Width; x++)
            if (actual[x, y] != expected[x, y]) diff++;
        Assert.Equal(0, diff);
    }

    // ---- node-thermal-printer ---------------------------------------------------------------------------

    private static List<string> Unsupported(string name)
    {
        var log = new List<string>();
        var interpreter = new EscposInterpreter(log: log.Add);
        interpreter.Feed(File.ReadAllBytes(Path.Combine(Dir(), name + ".bin")));
        interpreter.EndTicket();
        return log;
    }

    [Theory]
    [InlineData("node-receipt")]
    [InlineData("node-styles")]
    [InlineData("node-codes")]
    [InlineData("node-raw")]
    public void The_node_thermal_printer_streams_use_only_supported_commands(string name) =>
        Assert.Empty(Unsupported(name));

    [Fact]
    public void The_codes_of_node_thermal_printer_decode()
    {
        var ticket = Assert.Single(Interpret("node-codes"));
        var codes = Esc.DecodeAll(ticket);
        Assert.Contains("https://example.com/node", codes);
        Assert.Contains("5901234123457", codes);
        Assert.Contains("NODE-0042", codes);
        Assert.Contains("PDF417 desde node", codes);
    }

    [Fact]
    public void The_drawer_and_beep_of_node_thermal_printer_do_not_print_and_the_partial_cut_splits_the_job()
    {
        var tickets = Interpret("node-raw");
        Assert.Equal(2, tickets.Count);
        var plain = Esc.Run(new Esc().Init().Line("Antes").Line("Despues de cajon y pitido").Cut().Bytes);
        Assert.Equal(Esc.InkCount(plain[0]), Esc.InkCount(tickets[0]));
    }


    [Fact]
    public void The_two_tickets_of_one_stream_are_separate_jobs()
    {
        var tickets = Interpret("two-tickets");
        Assert.True(tickets[1].Height > tickets[0].Height);
    }
}
