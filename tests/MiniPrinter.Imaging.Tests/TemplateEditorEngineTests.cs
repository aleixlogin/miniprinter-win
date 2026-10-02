using System.Globalization;

namespace MiniPrinter.Imaging.Tests;

/// <summary>Engine support for the template editor: block rows, lenient previews, structured errors, schema.</summary>
public class TemplateEditorEngineTests
{
    private static Dictionary<string, string> F(params (string Key, string Value)[] fields) =>
        fields.ToDictionary(f => f.Key, f => f.Value);

    private static RenderOptions Opts() => new() { Now = new DateTime(2026, 10, 2), Culture = CultureInfo.InvariantCulture };

    private static string Template(string blocks, string fields = "", string extra = "") =>
        $$"""{ "name": "t", {{extra}} "fields": [ {{fields}} ], "blocks": [ {{blocks}} ] }""";

    private static RenderResult Render(string json, Dictionary<string, string>? fields = null, RenderOptions? options = null)
    {
        using var temp = new TempCatalog();
        var layout = temp.Catalog.ValidateDraft(json);
        return temp.Catalog.RenderLayout(layout, fields ?? [], options ?? Opts());
    }

    // ---- 1.1 rows per block --------------------------------------------------------------------

    [Fact]
    public void Block_rows_are_in_order_and_cover_the_page()
    {
        var result = Render(Template("""{ "type": "text", "value": "Hola" }, { "type": "line" }, { "type": "qr", "data": "https://example.com" }"""));
        Assert.Equal([1, 2, 3], result.Blocks.Select(b => b.Index));
        Assert.Equal(["text", "line", "qr"], result.Blocks.Select(b => b.Type));
        Assert.Equal(0, result.Blocks[0].Top);
        for (var i = 1; i < result.Blocks.Count; i++)
            Assert.Equal(result.Blocks[i - 1].Top + result.Blocks[i - 1].Height + 8, result.Blocks[i].Top);   // default gap
        var last = result.Blocks[^1];
        Assert.Equal(result.Bitmap.Height, last.Top + last.Height);
    }

    [Fact]
    public void Block_rows_match_what_is_drawn()
    {
        var result = Render(Template("""{ "type": "spacer", "mm": 2 }, { "type": "line", "thickness": 4 }"""));
        var line = result.Blocks[1];
        Assert.Equal(4, line.Height);
        // The line's rows have ink; the spacer's rows do not.
        Assert.True(Enumerable.Range(line.Top, line.Height).All(y => !result.Bitmap.IsRowBlank(y)));
        Assert.True(Enumerable.Range(result.Blocks[0].Top, result.Blocks[0].Height).All(y => result.Bitmap.IsRowBlank(y)));
    }

    [Fact]
    public void Skipped_blocks_are_not_listed()
    {
        var json = Template("""{ "type": "text", "value": "A" }, { "type": "line", "when": "{{linea}}" }, { "type": "text", "value": "{{vacio}}" }""",
            """{ "name": "linea", "kind": "boolean" }, { "name": "vacio", "kind": "text" }""");
        Assert.Equal([1], Render(json).Blocks.Select(b => b.Index));
        Assert.Equal([1, 2], Render(json, F(("linea", "true"))).Blocks.Select(b => b.Index));
    }

    [Fact]
    public void Frame_shifts_the_rows_by_its_padding()
    {
        var json = Template("""{ "type": "text", "value": "A" }""", "", "\"frame\": \"true\",");
        var framed = Render(json);
        var plain = Render(Template("""{ "type": "text", "value": "A" }"""));
        Assert.Equal(8, framed.Blocks[0].Top);
        Assert.Equal(plain.Bitmap.Height + 16, framed.Bitmap.Height);
    }

    // ---- 1.2 lenient ---------------------------------------------------------------------------

    [Fact]
    public void Lenient_preview_does_not_require_required_fields_but_still_validates()
    {
        var json = Template("""{ "type": "text", "value": "Hola {{n}}" }, { "type": "text", "value": "fin" }""",
            """{ "name": "n", "kind": "text", "required": true }""");
        var strict = Assert.Throws<TemplateException>(() => Render(json));
        Assert.Contains("'n'", strict.Message);
        Assert.Equal(2, Render(json, options: Opts() with { Lenient = true }).Blocks.Count);

        // Other problems are still errors in lenient mode.
        var bad = Template("""{ "type": "barcode", "data": "5901234123450", "format": "ean13" }""");
        Assert.Throws<TemplateException>(() => Render(bad, options: Opts() with { Lenient = true }));
    }

    // ---- 1.3 structured errors --------------------------------------------------------------------

    [Theory]
    [InlineData("""{ "type": "text", "value": "a" }, { "type": "text", "size": 500 }""", 2, "size")]
    [InlineData("""{ "type": "text", "colour": "red" }""", 1, "colour")]
    [InlineData("""{ "type": "line", "style": "wavy" }""", 1, "style")]
    [InlineData("""{ "type": "text", "value": "{{nada}}" }""", 1, "value")]
    [InlineData("""{ "type": "text", "value": "a", "when": "{{nada}}" }""", 1, "when")]
    [InlineData("""{ "type": "image", "source": "x.png" }""", 1, "source")]
    public void Validation_errors_carry_the_block_and_property(string blocks, int block, string property)
    {
        var error = Assert.Throws<TemplateException>(() => TemplateLayout.Parse(Template(blocks), _ => false));
        Assert.Equal(block, error.Block);
        Assert.Equal(property, error.Property);
    }

    [Fact]
    public void Unknown_block_type_points_at_the_block()
    {
        var error = Assert.Throws<TemplateException>(() => TemplateLayout.Parse(Template("""{ "type": "video" }""")));
        Assert.Equal(1, error.Block);
        Assert.Equal("type", error.Property);
    }

    [Fact]
    public void Render_time_errors_are_attributed_to_their_block_without_changing_the_message()
    {
        var json = Template("""{ "type": "text", "value": "a" }, { "type": "barcode", "data": "5901234123450", "format": "ean13" }""");
        var error = Assert.Throws<TemplateException>(() => Render(json));
        Assert.Equal(2, error.Block);
        Assert.Equal("Dígito de control EAN-13 incorrecto: debería ser 7.", error.Message);

        var qr = Assert.Throws<TemplateException>(() => Render(Template("""{ "type": "qr", "data": "hola", "module": 2 }""")));
        Assert.Equal(1, qr.Block);
        Assert.Equal("module", qr.Property);
    }

    [Fact]
    public void Errors_that_are_not_about_a_block_have_none()
    {
        var error = Assert.Throws<TemplateException>(() => TemplateLayout.Parse("{ no es json"));
        Assert.Null(error.Block);
        Assert.Null(error.Property);
    }

    // ---- 1.4 schema -------------------------------------------------------------------------------

    [Fact]
    public void Schema_lists_every_block_type_with_labelled_properties()
    {
        var schema = BlockSchema.Build();
        Assert.Equal(new[] { "text", "spacer", "line", "qr", "barcode", "list", "columns", "image" }.Order(), schema.Blocks.Select(b => b.Type).Order());
        Assert.All(schema.Blocks, b =>
        {
            Assert.False(string.IsNullOrWhiteSpace(b.Title));
            Assert.All(b.Props, p => Assert.NotEqual(p.Name, p.Label));   // every property has a human label
        });
        var text = schema.Blocks.Single(b => b.Type == "text");
        Assert.Equal(["left", "center", "right"], text.Props.Single(p => p.Name == "align").Values);
        var size = text.Props.Single(p => p.Name == "size");
        Assert.Equal((6, 48), (size.Min, size.Max));
        Assert.True(text.Props.Single(p => p.Name == "value").Multiline);
        Assert.Contains(schema.Common, p => p.Name == "when");
        Assert.Contains("daysleft", schema.Filters);
        Assert.Equal(100, schema.Template.MaxBlocks);
    }

    [Fact]
    public void Every_schema_property_is_accepted_by_the_engine_with_a_valid_value()
    {
        foreach (var block in BlockSchema.Build().Blocks)
        foreach (var prop in block.Props)
        {
            var value = prop.Kind switch
            {
                "int" or "number" => Math.Ceiling(prop.Min!.Value).ToString(CultureInfo.InvariantCulture),
                "bool" => "true",
                "enum" => prop.Values![0],
                _ => prop.Name == "source" ? "logo.png" : "x",
            };
            var json = Template($$"""{ "type": "{{block.Type}}", "{{prop.Name}}": "{{value}}" }""");
            TemplateLayout.Parse(json, _ => true);   // must not throw "propiedad desconocida" or a range error
        }
    }
}
