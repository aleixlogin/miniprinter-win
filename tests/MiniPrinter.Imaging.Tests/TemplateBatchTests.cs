namespace MiniPrinter.Imaging.Tests;

public class TemplateBatchTests
{
    private static Dictionary<string, string> F(params (string Key, string Value)[] fields) =>
        fields.ToDictionary(f => f.Key, f => f.Value);

    private const string Numbered = """
        { "name": "num", "fields": [ { "name": "t", "kind": "text", "required": true } ],
          "blocks": [ { "type": "text", "value": "{{t}} {{counter}}" } ] }
        """;

    // ---- batches ---------------------------------------------------------------------------------

    [Fact]
    public void Batch_repeats_each_label_and_numbers_each_distinct_label_once()
    {
        using var temp = new TempCatalog();
        temp.Catalog.Save("num", Numbered);
        var rows = new IReadOnlyDictionary<string, string>[] { F(("t", "a")), F(("t", "b")) };
        var pages = temp.Catalog.RenderBatch("num", F(), rows, copies: 3);
        Assert.Equal(6, pages.Count);
        Assert.Same(pages[0], pages[2]);       // copies of one label are the same raster
        Assert.NotSame(pages[0], pages[3]);
        Assert.Equal(3, temp.Counters.Peek("default"));   // two distinct labels → numbers 1 and 2 used
    }

    [Fact]
    public void Row_values_override_the_base_fields()
    {
        using var temp = new TempCatalog();
        var rows = new IReadOnlyDictionary<string, string>[] { F(("title", "Fila")), F() };
        var pages = temp.Catalog.RenderBatch("label", F(("title", "Base")), rows, copies: 1);
        Assert.Equal(2, pages.Count);
        Assert.NotEqual(pages[0].Row(30).ToArray(), pages[1].Row(30).ToArray());
    }

    [Fact]
    public void A_bad_row_throws_with_its_number_and_consumes_nothing()
    {
        using var temp = new TempCatalog();
        temp.Catalog.Save("num", Numbered);
        var rows = new IReadOnlyDictionary<string, string>[] { F(("t", "a")), F(("t", "b")), F() };
        var error = Assert.Throws<TemplateException>(() => temp.Catalog.RenderBatch("num", F(), rows, 1));
        Assert.StartsWith("Fila 3:", error.Message);
        Assert.Equal(1, temp.Counters.Peek("default"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Copies_are_limited(int copies)
    {
        using var temp = new TempCatalog();
        var error = Assert.Throws<TemplateException>(() => temp.Catalog.RenderBatch("label", F(("title", "x")), null, copies));
        Assert.Contains("50", error.Message);
    }

    [Fact]
    public void Rows_are_limited_to_200()
    {
        using var temp = new TempCatalog();
        var rows = Enumerable.Range(0, 201).Select(_ => (IReadOnlyDictionary<string, string>)F(("title", "x"))).ToList();
        var error = Assert.Throws<TemplateException>(() => temp.Catalog.RenderBatch("label", F(), rows, 1));
        Assert.Contains("200", error.Message);
        Assert.Equal(200, temp.Catalog.RenderBatch("label", F(), rows.Take(200).ToList(), 1).Count);
    }

    [Fact]
    public void Template_names_cannot_collide_with_cli_subcommands()
    {
        using var temp = new TempCatalog();
        foreach (var reserved in TemplateLayout.ReservedNames)
            Assert.Throws<TemplateException>(() => temp.Catalog.Save(reserved, Numbered.Replace("\"num\"", $"\"{reserved}\"")));
    }
}
