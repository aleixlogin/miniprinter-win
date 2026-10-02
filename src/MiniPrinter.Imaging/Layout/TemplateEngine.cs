using System.Globalization;
using MiniPrinter.Protocol;

namespace MiniPrinter.Imaging;

/// <summary>How a template is rendered: head width, clock, counters and where its images live.</summary>
public sealed record RenderOptions
{
    public int Width { get; init; } = 384;

    /// <summary>True when printing: <c>{{counter}}</c> advances. False for previews (shows the next number).</summary>
    public bool ConsumeCounters { get; init; }

    public ICounterSource? Counters { get; init; }
    public DateTime? Now { get; init; }
    public CultureInfo? Culture { get; init; }
    public string? AssetsDir { get; init; }
}

/// <summary>Turns a validated <see cref="TemplateLayout"/> plus field values into a 1-bit raster.</summary>
internal static class TemplateEngine
{
    /// <summary>Tallest page a template may produce (1 m of paper).</summary>
    public const int MaxHeight = 1000 * LayoutBlocks.Dots;

    public static MonoBitmap Render(TemplateLayout layout, IReadOnlyDictionary<string, string> fields, RenderOptions options)
    {
        var values = Resolve(layout, fields);
        var interpolator = new Interpolator
        {
            Values = values,
            Now = options.Now ?? DateTime.Now,
            Culture = options.Culture ?? CultureInfo.CurrentCulture,
            Counters = options.Counters,
            ConsumeCounters = options.ConsumeCounters,
        };
        var context = new BlockContext { Width = options.Width, Interpolator = interpolator, AssetsDir = options.AssetsDir };

        var blocks = new List<MonoBitmap>();
        foreach (var block in layout.Blocks)
        {
            if (block.Json["when"] is { } when && !BlockContext.Truthy(interpolator.Apply(BlockContext.ReadRaw(when) ?? "")))
                continue;
            if (LayoutBlocks.Find(block.Type)!.Render(block, context) is { } bitmap)
                blocks.Add(bitmap);
        }
        if (blocks.Count == 0)
            throw new TemplateException("La plantilla no produce ningún contenido.");

        var page = LayoutBlocks.Stack(blocks, layout.Gap);
        if (layout.Frame is { } frame && BlockContext.Truthy(interpolator.Apply(frame)))
            page = Frame(page);
        if (page.Height > MaxHeight)
            throw new TemplateException("La plantilla es demasiado larga (máximo 1 m de papel).");
        return page;
    }

    /// <summary>Required fields, defaults and choices (same messages as the old renderer).</summary>
    private static Dictionary<string, string> Resolve(TemplateLayout layout, IReadOnlyDictionary<string, string> fields)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in layout.Fields)
        {
            var value = fields.FirstOrDefault(f => f.Key.Equals(field.Name, StringComparison.OrdinalIgnoreCase)).Value;
            if (string.IsNullOrWhiteSpace(value))
                value = field.Default;
            if (string.IsNullOrWhiteSpace(value))
            {
                if (field.Required)
                    throw new TemplateException($"Falta el campo obligatorio '{field.Name}' ({field.Label}).");
                continue;
            }
            if (field.Kind == TemplateFieldKind.Choice && !field.Choices!.Contains(value, StringComparer.OrdinalIgnoreCase))
                throw new TemplateException($"Valor no válido para '{field.Name}': '{value}'. Opciones: {string.Join(", ", field.Choices!)}.");
            values[field.Name] = value;
        }
        return values;
    }

    /// <summary>Draws a 3-dot border around the page, with a little vertical padding.</summary>
    private static MonoBitmap Frame(MonoBitmap page)
    {
        const int pad = 8, border = 3;
        var framed = new MonoBitmap(page.Width, page.Height + 2 * pad);
        for (var y = 0; y < page.Height; y++)
            page.Row(y).CopyTo(framed.MutableRow(y + pad));
        for (var y = 0; y < framed.Height; y++)
        for (var x = 0; x < framed.Width; x++)
            if (x < border || x >= framed.Width - border || y < border || y >= framed.Height - border)
                framed[x, y] = true;
        return framed;
    }
}
