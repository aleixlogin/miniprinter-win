using MiniPrinter.Imaging;
using MiniPrinter.Protocol;

namespace MiniPrinter.Imaging.Tests;

public class ExamplesRenderTests
{
    [Fact]
    public void Every_built_in_template_renders_with_its_example_values()
    {
        var catalog = TemplateCatalog.ForDataDirectory(Path.Combine(Path.GetTempPath(), "mp-ex-" + Guid.NewGuid().ToString("N")));
        var failures = new List<string>();
        foreach (var definition in catalog.Definitions)
        {
            var layout = catalog.FindLayout(definition.Name)!;
            var uses = layout.FieldRules();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in definition.Fields.Where(f => f.Kind != TemplateFieldKind.Image))
            {
                var kind = field.Kind switch { TemplateFieldKind.Number => "number", TemplateFieldKind.Choice => "choice", TemplateFieldKind.Boolean => "boolean", _ => "text" };
                var rules = uses.Where(u => u.Field.Equals(field.Name, StringComparison.OrdinalIgnoreCase)).Select(u => u.Rule).ToList();
                var value = TemplateExamples.Choose(kind, field.Default, field.Choices?.FirstOrDefault(), field.Label, rules);
                if (value.Length > 0)
                    values[field.Name] = value;
            }
            try
            {
                catalog.RenderDetailed(definition.Name, values, new RenderOptions { Lenient = true });
            }
            catch (TemplateException ex)
            {
                failures.Add($"{definition.Name}: {ex.Message}");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}

public class SampleImageTests
{
    private const string Sticker = """{ "name": "p", "fields": [ { "name": "foto", "kind": "image" } ], "blocks": [ { "type": "image", "data": "{{foto}}", "caption": "Pie" } ] }""";

    private static TemplateCatalog Catalog()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mp-img-" + Guid.NewGuid().ToString("N"));
        return TemplateCatalog.ForDataDirectory(dir);
    }

    [Fact]
    public void A_preview_without_a_picture_shows_a_generic_one()
    {
        var catalog = Catalog();
        var layout = catalog.ValidateDraft(Sticker);
        var blank = catalog.RenderLayout(layout, new Dictionary<string, string>(), new RenderOptions { Lenient = true }).Bitmap;
        var ink = Enumerable.Range(0, blank.Height).Count(y => !blank.IsRowBlank(y));
        Assert.True(blank.Height > 100, $"the picture takes room: {blank.Height} rows");
        Assert.True(ink > 100);
    }

    [Fact]
    public void Printing_without_a_picture_still_prints_nothing_for_it()
    {
        var catalog = Catalog();
        var layout = catalog.ValidateDraft(Sticker);
        // On paper the generic picture never appears: with nothing else in the template there is nothing to print.
        Assert.Throws<TemplateException>(() => catalog.RenderLayout(layout, new Dictionary<string, string>(), new RenderOptions { Lenient = false }));
    }

    [Fact]
    public void The_generic_picture_fits_the_width_it_is_given()
    {
        foreach (var width in new[] { 384, 200, 100 })
            Assert.Equal(width, LayoutBlocks.SampleImage(width).Width);
    }
}
