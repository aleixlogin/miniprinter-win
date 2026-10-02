using System.Globalization;
using MiniPrinter.Protocol;
using ZXing;
using SixLabors.ImageSharp.PixelFormats;

namespace MiniPrinter.Imaging.Tests;

public sealed class TempCatalog : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "mp-tpl-" + Guid.NewGuid().ToString("N"));
    public MemoryCounterStore Counters { get; } = new();
    public TemplateCatalog Catalog { get; }

    public TempCatalog()
    {
        Catalog = new TemplateCatalog(Dir, Counters);
    }

    public void Write(string name, string json)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, name + ".json"), json);
    }

    public void Dispose()
    {
        if (Directory.Exists(Dir))
            Directory.Delete(Dir, recursive: true);
    }
}

public class TemplateLayoutTests
{
    private static Dictionary<string, string> F(params (string Key, string Value)[] fields) =>
        fields.ToDictionary(f => f.Key, f => f.Value);

    private static readonly DateTime Now = new(2026, 10, 2, 9, 30, 0);

    private static RenderOptions Opts(ICounterSource? counters = null, bool consume = false) => new()
    {
        Now = Now,
        Culture = CultureInfo.InvariantCulture,
        Counters = counters,
        ConsumeCounters = consume,
    };

    private static Result? Decode(MonoBitmap bitmap, BarcodeFormat format)
    {
        var luminance = new byte[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            luminance[y * bitmap.Width + x] = bitmap[x, y] ? (byte)0 : (byte)255;
        var reader = new BarcodeReaderGeneric { Options = { PossibleFormats = [format], TryHarder = true } };
        return reader.Decode(new RGBLuminanceSource(luminance, bitmap.Width, bitmap.Height, RGBLuminanceSource.BitmapFormat.Gray8));
    }

    private static int Ink(MonoBitmap bitmap)
    {
        var n = 0;
        for (var y = 0; y < bitmap.Height; y++)
            n += bitmap.Row(y).Count((byte)1);
        return n;
    }

    private static string Template(string blocks, string fields = "", string extra = "") =>
        $$"""{ "name": "t", {{extra}} "fields": [ {{fields}} ], "blocks": [ {{blocks}} ] }""";

    private static MonoBitmap Render(string json, Dictionary<string, string>? fields = null, RenderOptions? options = null)
    {
        using var temp = new TempCatalog();
        temp.Write("t", json);
        return temp.Catalog.Render("t", fields ?? [], options ?? Opts(temp.Counters));
    }

    // ---- fields and interpolation ---------------------------------------------------------------

    [Fact]
    public void Placeholders_are_replaced_and_empty_optional_blocks_take_no_height()
    {
        var json = Template("""{ "type": "text", "value": "Hola {{nombre}}" }, { "type": "text", "value": "{{vacio}}" }""",
            """{ "name": "nombre", "kind": "text" }, { "name": "vacio", "kind": "text" }""");
        var with = Render(json, F(("nombre", "Ana")));
        var single = Render(Template("""{ "type": "text", "value": "Hola Ana" }"""));
        Assert.Equal(single.Height, with.Height);
    }

    [Fact]
    public void Undeclared_placeholder_is_rejected()
    {
        var error = Assert.Throws<TemplateException>(() =>
            TemplateLayout.Parse(Template("""{ "type": "text", "value": "{{cliente}}" }""")));
        Assert.Contains("cliente", error.Message);
    }

    [Fact]
    public void Now_is_formatted_with_the_given_format()
    {
        var parsed = TemplateLayout.Parse(Template("""{ "type": "text", "value": "{{now:dd/MM/yyyy}}" }"""));
        var interpolator = new Interpolator { Values = new Dictionary<string, string>(), Now = Now, Culture = CultureInfo.InvariantCulture };
        Assert.Equal("02/10/2026", interpolator.Apply("{{now:dd/MM/yyyy}}"));
        Assert.Single(parsed.Blocks);
    }

    [Fact]
    public void Filters_escape_wifi_and_vcard_values()
    {
        var interpolator = new Interpolator
        {
            Values = new Dictionary<string, string> { ["p"] = "a;b\\c", ["n"] = "Ana, Sánchez\nX" },
            Now = Now,
            Culture = CultureInfo.InvariantCulture,
        };
        Assert.Equal("a\\;b\\\\c", interpolator.Apply("{{p|wifi}}"));
        Assert.Equal("Ana\\, Sánchez\\nX", interpolator.Apply("{{n|vcard}}"));
        Assert.Equal("ANA", interpolator.Apply("{{n|upper}}")[..3]);
    }

    [Fact]
    public void Substituted_values_are_not_interpolated_again()
    {
        var interpolator = new Interpolator { Values = new Dictionary<string, string> { ["a"] = "{{b}}", ["b"] = "X" }, Now = Now };
        Assert.Equal("{{b}}", interpolator.Apply("{{a}}"));
    }

    // ---- counters ---------------------------------------------------------------------------------

    [Fact]
    public void Counter_advances_only_when_printing()
    {
        using var temp = new TempCatalog();
        temp.Write("t", Template("""{ "type": "text", "value": "N {{counter}}" }"""));
        temp.Catalog.Render("t", F(), Opts(temp.Counters)); // preview
        temp.Catalog.Render("t", F(), Opts(temp.Counters));
        Assert.Equal(1, temp.Counters.Peek("default"));
        temp.Catalog.Render("t", F(), Opts(temp.Counters, consume: true));
        temp.Catalog.Render("t", F(), Opts(temp.Counters, consume: true));
        Assert.Equal(3, temp.Counters.Peek("default"));
    }

    [Fact]
    public void Counter_used_twice_in_a_label_takes_one_number()
    {
        using var temp = new TempCatalog();
        temp.Write("t", Template("""{ "type": "text", "value": "{{counter}} {{counter}}" }"""));
        temp.Catalog.Render("t", F(), Opts(temp.Counters, consume: true));
        Assert.Equal(2, temp.Counters.Peek("default"));
    }

    [Fact]
    public void File_counter_survives_a_restart_and_keeps_named_counters_apart()
    {
        var path = Path.Combine(Path.GetTempPath(), "mp-counter-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var first = new FileCounterStore(path);
            Assert.Equal(1, first.Peek("a"));
            Assert.Equal(1, first.Next("a"));
            Assert.Equal(2, first.Next("a"));
            Assert.Equal(1, first.Next("b"));

            var second = new FileCounterStore(path); // "restart"
            Assert.Equal(3, second.Peek("a"));
            Assert.Equal(2, second.Peek("b"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- validation -------------------------------------------------------------------------------

    [Theory]
    [InlineData("""{ "type": "video" }""", "video")]
    [InlineData("""{ "type": "text", "colour": "red" }""", "colour")]
    [InlineData("""{ "type": "text", "size": 500 }""", "size")]
    [InlineData("""{ "type": "line", "style": "wavy" }""", "style")]
    public void Invalid_blocks_are_rejected_with_the_reason(string block, string expected)
    {
        var error = Assert.Throws<TemplateException>(() => TemplateLayout.Parse(Template(block)));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void More_than_100_blocks_are_rejected()
    {
        var blocks = string.Join(",", Enumerable.Repeat("""{ "type": "spacer" }""", 101));
        var error = Assert.Throws<TemplateException>(() => TemplateLayout.Parse(Template(blocks)));
        Assert.Contains("100", error.Message);
    }

    [Fact]
    public void Oversized_json_is_rejected()
    {
        var error = Assert.Throws<TemplateException>(() => TemplateLayout.Parse(new string(' ', 70 * 1024)));
        Assert.Contains("64", error.Message);
    }

    [Fact]
    public void Missing_image_asset_is_reported()
    {
        var error = Assert.Throws<TemplateException>(() =>
            TemplateLayout.Parse(Template("""{ "type": "image", "source": "logo.png" }"""), _ => false));
        Assert.Contains("logo.png", error.Message);
    }

    // ---- blocks -------------------------------------------------------------------------------------

    [Fact]
    public void Spacer_adds_eight_rows_per_millimetre()
    {
        var bitmap = Render(Template("""{ "type": "spacer", "mm": 5 }"""));
        Assert.Equal(40, bitmap.Height);
        Assert.Equal(0, Ink(bitmap));
    }

    [Fact]
    public void Dotted_line_has_less_ink_than_a_solid_one()
    {
        var solid = Ink(Render(Template("""{ "type": "line" }""")));
        var dotted = Ink(Render(Template("""{ "type": "line", "style": "dotted" }""")));
        Assert.True(dotted > 0 && dotted < solid);
    }

    [Fact]
    public void Blocks_stack_in_order_with_a_gap()
    {
        var single = Render(Template("""{ "type": "spacer", "mm": 1 }"""));
        var three = Render(Template("""{ "type": "spacer", "mm": 1 }, { "type": "line", "thickness": 2 }, { "type": "spacer", "mm": 1 }"""));
        Assert.Equal(single.Height * 2 + 2 + 2 * 8, three.Height);
    }

    [Fact]
    public void Columns_put_the_right_text_at_the_right_edge_with_dot_leaders()
    {
        var json = Template("""{ "type": "columns", "left": "Café", "right": "1,50", "leader": "dots" }""");
        var withLeader = Render(json);
        var without = Render(json.Replace("\"leader\": \"dots\"", "\"leader\": \"none\""));
        Assert.True(Ink(withLeader) > Ink(without)); // dots between the texts
        // Ink near the right margin, none beyond it.
        var rightEdge = 0;
        for (var y = 0; y < without.Height; y++)
        for (var x = 0; x < without.Width; x++)
            if (without[x, y]) rightEdge = Math.Max(rightEdge, x);
        Assert.InRange(rightEdge, 384 - TextRenderer.MarginPx - 6, 384 - TextRenderer.MarginPx + 2);
    }

    [Fact]
    public void Columns_wrap_a_long_left_text()
    {
        var shortRow = Render(Template("""{ "type": "columns", "left": "Café", "right": "1,50" }"""));
        var longRow = Render(Template($$"""{ "type": "columns", "left": "{{new string('x', 5)}} {{string.Join(' ', Enumerable.Repeat("producto", 6))}}", "right": "1,50" }"""));
        Assert.True(longRow.Height > shortRow.Height);
    }

    [Fact]
    public void List_markers_and_empty_list()
    {
        var box = Render(Template("""{ "type": "list", "items": "{{i}}", "marker": "box" }""", """{ "name": "i", "kind": "multiline" }"""), F(("i", "A\nB\nC")));
        var none = Render(Template("""{ "type": "list", "items": "{{i}}", "marker": "none" }""", """{ "name": "i", "kind": "multiline" }"""), F(("i", "A\nB\nC")));
        Assert.True(box.Height > none.Height);
        var number = Render(Template("""{ "type": "list", "items": "{{i}}", "marker": "number" }""", """{ "name": "i", "kind": "multiline" }"""), F(("i", "A\nB\nC")));
        Assert.True(Ink(number) > Ink(none)); // "1. " prefixes

        var error = Assert.Throws<TemplateException>(() =>
            Render(Template("""{ "type": "list", "items": "{{i}}" }""", """{ "name": "i", "kind": "multiline" }"""), F()));
        Assert.Equal("La lista no tiene tareas.", error.Message);
    }

    [Fact]
    public void When_hides_a_block_for_an_empty_or_false_field()
    {
        var json = Template("""{ "type": "text", "value": "X" }, { "type": "line", "when": "{{linea}}" }""",
            """{ "name": "linea", "kind": "boolean" }""");
        var without = Render(json, F());
        var with = Render(json, F(("linea", "true")));
        var off = Render(json, F(("linea", "false")));
        Assert.True(with.Height > without.Height);
        Assert.Equal(without.Height, off.Height);
    }

    [Fact]
    public void Asset_image_is_rendered_from_the_template_folder()
    {
        using var temp = new TempCatalog();
        using var png = new MemoryStream();
        using (var image = new SixLabors.ImageSharp.Image<L8>(100, 50, new L8(0)))
            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, png);
        temp.Write("t", Template("""{ "type": "image", "source": "logo.png" }"""));
        temp.Catalog.SaveAsset("t", "logo.png", png.ToArray());
        var bitmap = temp.Catalog.Render("t", F(), Opts());
        Assert.Equal(384, bitmap.Width);
        Assert.True(bitmap.Height >= 190);
    }

    // ---- qr / barcodes --------------------------------------------------------------------------------

    [Fact]
    public void Qr_ecc_level_and_module_size_are_applied()
    {
        var low = TemplateRenderer.Render("qr", F(("data", "https://example.com/a/b"), ("ecc", "L")));
        var high = TemplateRenderer.Render("qr", F(("data", "https://example.com/a/b"), ("ecc", "H")));
        Assert.NotEqual(low.Height, high.Height); // more modules at a higher correction level
        Assert.Equal("https://example.com/a/b", Decode(high, BarcodeFormat.QR_CODE)?.Text);

        var small = TemplateRenderer.Render("qr", F(("data", "hola"), ("module", "4")));
        var auto = TemplateRenderer.Render("qr", F(("data", "hola")));
        Assert.True(small.Height < auto.Height);
        Assert.Equal("hola", Decode(small, BarcodeFormat.QR_CODE)?.Text);

        var error = Assert.Throws<TemplateException>(() => TemplateRenderer.Render("qr", F(("data", "hola"), ("module", "2"))));
        Assert.Contains("4", error.Message);
    }

    [Fact]
    public void Upca_is_readable_and_its_check_digit_is_computed_and_validated()
    {
        var bitmap = TemplateRenderer.Render("barcode", F(("data", "03600029145"), ("format", "upca")));
        Assert.Equal("036000291452", Decode(bitmap, BarcodeFormat.UPC_A)?.Text);
        var error = Assert.Throws<TemplateException>(() =>
            TemplateRenderer.Render("barcode", F(("data", "036000291450"), ("format", "upca"))));
        Assert.Contains("debería ser 2", error.Message);
    }

    [Fact]
    public void Code39_is_readable_and_rejects_unsupported_characters()
    {
        var bitmap = TemplateRenderer.Render("barcode", F(("data", "mini-42"), ("format", "code39")));
        Assert.Equal("MINI-42", Decode(bitmap, BarcodeFormat.CODE_39)?.Text);
        var error = Assert.Throws<TemplateException>(() =>
            TemplateRenderer.Render("barcode", F(("data", "a@b"), ("format", "code39"))));
        Assert.Contains("'@'", error.Message);
    }

    [Fact]
    public void Barcode_height_is_configurable()
    {
        var normal = TemplateRenderer.Render("barcode", F(("data", "ABC")));
        var tall = TemplateRenderer.Render("barcode", F(("data", "ABC"), ("height", "20")));
        Assert.Equal(normal.Height + (20 - 12) * 8, tall.Height);
    }

    // ---- built-in templates ------------------------------------------------------------------------------

    [Fact]
    public void Label_border_size_and_alignment_options()
    {
        var plain = TemplateRenderer.Render("label", F(("title", "Cajón 3")));
        var framed = TemplateRenderer.Render("label", F(("title", "Cajón 3"), ("border", "true")));
        Assert.Equal(plain.Height + 16, framed.Height);
        Assert.True(framed[0, framed.Height / 2] && framed[383, framed.Height / 2]);
        var big = TemplateRenderer.Render("label", F(("title", "Cajón 3"), ("size", "40")));
        Assert.True(big.Height > plain.Height);
        var invalid = Assert.Throws<TemplateException>(() => TemplateRenderer.Render("label", F(("title", "x"), ("align", "diagonal"))));
        Assert.Contains("align", invalid.Message);
    }

    [Fact]
    public void Wifi_qr_contains_the_escaped_credentials()
    {
        var bitmap = TemplateRenderer.Render("wifi", F(("ssid", "Casa"), ("password", "secreto")));
        Assert.Equal("WIFI:T:WPA;S:Casa;P:secreto;;", Decode(bitmap, BarcodeFormat.QR_CODE)?.Text);
        var escaped = TemplateRenderer.Render("wifi", F(("ssid", "Casa;1"), ("password", "a\\b;c")));
        Assert.Equal("WIFI:T:WPA;S:Casa\\;1;P:a\\\\b\\;c;;", Decode(escaped, BarcodeFormat.QR_CODE)?.Text);
    }

    [Fact]
    public void Contact_qr_is_a_vcard()
    {
        var bitmap = TemplateRenderer.Render("contact", F(("name", "Ana Pérez"), ("phone", "+34600111222")));
        var text = Decode(bitmap, BarcodeFormat.QR_CODE)?.Text;
        Assert.Contains("BEGIN:VCARD", text);
        Assert.Contains("FN:Ana Pérez", text);
        Assert.Contains("TEL:+34600111222", text);
    }

    [Fact]
    public void Countdown_shows_the_days_left_and_rejects_past_dates()
    {
        using var temp = new TempCatalog();
        var future = Now.AddDays(10).ToString("yyyy-MM-dd");
        var bitmap = temp.Catalog.Render("countdown", F(("title", "Viaje"), ("date", future)), Opts());
        Assert.True(bitmap.Height > 80);
        var error = Assert.Throws<TemplateException>(() =>
            temp.Catalog.Render("countdown", F(("title", "Viaje"), ("date", "2020-01-01")), Opts()));
        Assert.Equal("La fecha ya pasó.", error.Message);
    }

    [Fact]
    public void Receipt_and_shopping_and_cable_and_bookmark_render()
    {
        using var temp = new TempCatalog();
        var receipt = temp.Catalog.Render("receipt",
            F(("title", "Bar Paco"), ("items", "Café;1,50\nTostada;2,20"), ("total", "3,70")), Opts(temp.Counters, consume: true));
        Assert.True(receipt.Height > 200);
        Assert.Equal(2, temp.Counters.Peek("receipt"));
        Assert.True(temp.Catalog.Render("shopping", F(("items", "Leche;2\nPan")), Opts()).Height > 60);
        Assert.True(temp.Catalog.Render("cable", F(("text", "ROUTER-1")), Opts()).Height > 60);
        Assert.True(temp.Catalog.Render("bookmark", F(("title", "Dune"), ("author", "Herbert"), ("quote", "El miedo es el asesino de la mente.")), Opts()).Height > 100);
    }

    [Fact]
    public void Shopping_list_shows_quantities_on_the_right()
    {
        var withQty = TemplateRenderer.Render("shopping", F(("items", "Leche;2")));
        var withoutQty = TemplateRenderer.Render("shopping", F(("items", "Leche")));
        Assert.True(Ink(withQty) > Ink(withoutQty));
        var rightInk = false;
        for (var y = 0; y < withQty.Height; y++)
        for (var x = 330; x < 384; x++)
            rightInk |= withQty[x, y];
        Assert.True(rightInk);
    }

    // ---- catalog ---------------------------------------------------------------------------------------

    private const string RecipeJson = """
        { "name": "recibo", "title": "Recibo", "fields": [ { "name": "cliente", "kind": "text", "required": true } ],
          "blocks": [ { "type": "text", "value": "Cliente: {{cliente}}" } ] }
        """;

    [Fact]
    public void User_template_appears_without_restarting()
    {
        using var temp = new TempCatalog();
        Assert.Null(temp.Catalog.Find("recibo"));
        temp.Write("recibo", RecipeJson);
        Assert.Equal("user", temp.Catalog.Find("recibo")!.Source);
        Assert.Contains(temp.Catalog.Definitions, d => d.Name == "recibo");
        Assert.True(temp.Catalog.Render("recibo", F(("cliente", "Ana")), Opts()).Height > 20);
    }

    [Fact]
    public void User_template_replaces_a_builtin_and_deleting_it_restores_it()
    {
        using var temp = new TempCatalog();
        temp.Catalog.Save("label", """{ "name": "label", "blocks": [ { "type": "text", "value": "MI ETIQUETA" } ] }""");
        Assert.Equal("override", temp.Catalog.Find("label")!.Source);
        Assert.Empty(temp.Catalog.FindLayout("label")!.Fields); // the user's version
        temp.Catalog.Delete("label");
        Assert.Equal("builtin", temp.Catalog.Find("label")!.Source);
        Assert.NotEmpty(temp.Catalog.FindLayout("label")!.Fields);
    }

    [Fact]
    public void Broken_user_template_does_not_hide_the_valid_ones()
    {
        using var temp = new TempCatalog();
        temp.Write("roto", "{ esto no es json");
        temp.Write("recibo", RecipeJson);
        Assert.NotNull(temp.Catalog.Find("recibo"));
        Assert.Null(temp.Catalog.Find("roto"));
        Assert.Contains(temp.Catalog.Errors, e => e.StartsWith("roto.json"));
        Assert.NotNull(temp.Catalog.Find("qr"));
    }

    [Fact]
    public void Builtin_without_user_version_cannot_be_deleted_and_names_are_validated()
    {
        using var temp = new TempCatalog();
        var error = Assert.Throws<TemplateException>(() => temp.Catalog.Delete("qr"));
        Assert.Contains("integrada", error.Message);
        Assert.Throws<TemplateException>(() => temp.Catalog.Save("../config", RecipeJson));
        Assert.Throws<TemplateException>(() => temp.Catalog.Save("otro", RecipeJson)); // name mismatch
        Assert.False(File.Exists(Path.Combine(temp.Dir, "..", "config.json")));
    }

    [Fact]
    public void Assets_are_validated_and_removed_with_the_template()
    {
        using var temp = new TempCatalog();
        temp.Catalog.Save("recibo", RecipeJson);
        Assert.Throws<TemplateException>(() => temp.Catalog.SaveAsset("recibo", "../x.png", new byte[20]));
        Assert.Throws<TemplateException>(() => temp.Catalog.SaveAsset("recibo", "big.png", new byte[2 * 1024 * 1024]));
        Assert.Throws<TemplateException>(() => temp.Catalog.SaveAsset("recibo", "fake.png", new byte[100]));
        temp.Catalog.Delete("recibo");
        Assert.False(Directory.Exists(temp.Catalog.AssetsDirectory("recibo")));
    }

    [Fact]
    public void Unknown_template_lists_user_templates_too()
    {
        using var temp = new TempCatalog();
        temp.Write("recibo", RecipeJson);
        var error = Assert.Throws<TemplateException>(() => temp.Catalog.Render("nada", F()));
        Assert.Contains("recibo", error.Message);
    }
}
