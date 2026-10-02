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

    /// <summary>Second folder where an image is looked for when it is not in <see cref="AssetsDir"/> (editor drafts).</summary>
    public string? FallbackAssetsDir { get; init; }

    /// <summary>Editor previews: a required field without a value is not an error.</summary>
    public bool Lenient { get; init; }
}

/// <summary>Where a block ended up in the rendered page (rows of the final raster).</summary>
public sealed record BlockRows(int Index, string Type, int Top, int Height);

/// <summary>A rendered template plus the rows each block occupies (for the editor's click-to-select).</summary>
public sealed record RenderResult(MonoBitmap Bitmap, IReadOnlyList<BlockRows> Blocks);

/// <summary>Turns a validated <see cref="TemplateLayout"/> plus field values into a 1-bit raster.</summary>
internal static class TemplateEngine
{
    /// <summary>Tallest page a template may produce (1 m of paper).</summary>
    public const int MaxHeight = 1000 * LayoutBlocks.Dots;

    private const int FramePad = 8, FrameBorder = 3;

    public static MonoBitmap Render(TemplateLayout layout, IReadOnlyDictionary<string, string> fields, RenderOptions options) =>
        RenderDetailed(layout, fields, options).Bitmap;

    public static RenderResult RenderDetailed(TemplateLayout layout, IReadOnlyDictionary<string, string> fields, RenderOptions options)
    {
        var values = Resolve(layout, fields, options.Lenient);
        var interpolator = new Interpolator
        {
            Values = values,
            Now = options.Now ?? DateTime.Now,
            Culture = options.Culture ?? CultureInfo.CurrentCulture,
            Counters = options.Counters,
            ConsumeCounters = options.ConsumeCounters,
        };
        var context = new BlockContext { Width = options.Width, Interpolator = interpolator, AssetsDir = options.AssetsDir, FallbackAssetsDir = options.FallbackAssetsDir };

        var blocks = new List<MonoBitmap>();
        var rendered = new List<BlockSpec>();
        foreach (var block in layout.Blocks)
        {
            try
            {
                if (block.Json["when"] is { } when && !BlockContext.Truthy(interpolator.Apply(BlockContext.ReadRaw(when) ?? "")))
                    continue;
                if (LayoutBlocks.Find(block.Type)!.Render(block, context) is { } bitmap)
                {
                    blocks.Add(bitmap);
                    rendered.Add(block);
                }
            }
            catch (TemplateException ex) when (ex.Block is null)
            {
                // Attribute the error to this block without changing its message.
                throw new TemplateException(ex.Message, block.Index, ex.Property);
            }
        }
        if (blocks.Count == 0 && options.Lenient)
            blocks.Add(new MonoBitmap(options.Width, 8));   // editor preview of a still-empty template
        else if (blocks.Count == 0)
            throw new TemplateException("La plantilla no produce ningún contenido.");

        var page = LayoutBlocks.Stack(blocks, layout.Gap);
        var offset = 0;
        if (layout.Frame is { } frame && BlockContext.Truthy(interpolator.Apply(frame)))
        {
            page = Frame(page);
            offset = FramePad;
        }
        if (page.Height > MaxHeight)
            throw new TemplateException("La plantilla es demasiado larga (máximo 1 m de papel).");

        var rows = new List<BlockRows>(rendered.Count);
        var top = offset;
        for (var i = 0; i < rendered.Count; i++)
        {
            rows.Add(new BlockRows(rendered[i].Index, rendered[i].Type, top, blocks[i].Height));
            top += blocks[i].Height + layout.Gap;
        }
        return new RenderResult(page, rows);
    }

    /// <summary>Required fields, defaults and choices (same messages as the old renderer).</summary>
    private static Dictionary<string, string> Resolve(TemplateLayout layout, IReadOnlyDictionary<string, string> fields, bool lenient)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in layout.Fields)
        {
            var value = fields.FirstOrDefault(f => f.Key.Equals(field.Name, StringComparison.OrdinalIgnoreCase)).Value;
            if (string.IsNullOrWhiteSpace(value))
                value = field.Default;
            if (string.IsNullOrWhiteSpace(value))
            {
                if (field.Required && !lenient)
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
        var framed = new MonoBitmap(page.Width, page.Height + 2 * FramePad);
        for (var y = 0; y < page.Height; y++)
            page.Row(y).CopyTo(framed.MutableRow(y + FramePad));
        for (var y = 0; y < framed.Height; y++)
        for (var x = 0; x < framed.Width; x++)
            if (x < FrameBorder || x >= framed.Width - FrameBorder || y < FrameBorder || y >= framed.Height - FrameBorder)
                framed[x, y] = true;
        return framed;
    }
}
