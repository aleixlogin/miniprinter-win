using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MiniPrinter.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MiniPrinter.Service;

/// <summary>
/// Small pictures of the templates for the gallery of the tray. Each is the template drawn in tolerant mode with example values
/// (the default of each field, or its label) and scaled down. They are kept in memory under a fingerprint of the template
/// (its JSON and its images), so saving or deleting a template makes the next request draw it again.
/// </summary>
public sealed class TemplateThumbnails
{
    public const int DefaultWidth = 160, MinWidth = 64, MaxWidth = 384;

    private readonly TemplateCatalog _templates;
    private readonly PrinterManager _printer;
    private readonly ConcurrentDictionary<(string Name, int Width), (string Fingerprint, byte[] Png)> _cache = new();

    public TemplateThumbnails(TemplateCatalog templates, PrinterManager printer)
    {
        _templates = templates;
        _printer = printer;
    }

    /// <summary>The PNG of the template, or null when there is no such template.</summary>
    public byte[]? Get(string name, int width = DefaultWidth)
    {
        width = Math.Clamp(width, MinWidth, MaxWidth);
        var json = _templates.GetJson(name);
        if (json is null)
        {
            foreach (var key in _cache.Keys.Where(k => k.Name == name).ToList())
                _cache.TryRemove(key, out _);
            return null;
        }
        var fingerprint = Fingerprint(name, json);
        if (_cache.TryGetValue((name, width), out var cached) && cached.Fingerprint == fingerprint)
            return cached.Png;
        var png = Draw(name, width);
        _cache[(name, width)] = (fingerprint, png);
        return png;
    }

    /// <summary>How many pictures are in memory (for the tests).</summary>
    public int Cached => _cache.Count;

    private string Fingerprint(string name, string json)
    {
        var text = new StringBuilder(json);
        var assets = _templates.AssetsDirectory(name);
        if (Directory.Exists(assets))
        {
            foreach (var file in new DirectoryInfo(assets).EnumerateFiles().OrderBy(f => f.Name, StringComparer.Ordinal))
                text.Append('|').Append(file.Name).Append(':').Append(file.Length).Append(':').Append(file.LastWriteTimeUtc.Ticks);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private byte[] Draw(string name, int width)
    {
        var definition = _templates.Find(name)!;
        var uses = _templates.FindLayout(name)!.FieldRules();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in definition.Fields)
        {
            if (field.Kind == TemplateFieldKind.Image)
                continue; // an image has no example
            var rules = uses.Where(u => u.Field.Equals(field.Name, StringComparison.OrdinalIgnoreCase)).Select(u => u.Rule).ToList();
            var value = MiniPrinter.Protocol.TemplateExamples.Choose(field.Kind switch
            {
                TemplateFieldKind.Number => "number",
                TemplateFieldKind.Choice => "choice",
                TemplateFieldKind.Boolean => "boolean",
                _ => "text",
            }, field.Default, field.Choices is { Count: > 0 } c ? c[0] : null, field.Label, rules);
            if (field.Kind == TemplateFieldKind.Boolean && value.Length == 0)
                value = "true"; // a thumbnail shows the optional parts
            if (value.Length > 0)
                values[field.Name] = value;
        }

        try
        {
            var page = _templates.RenderDetailed(name, values, new RenderOptions { Width = _printer.Profile.WidthPx, Lenient = true }).Bitmap;
            using var image = new Image<L8>(page.Width, Math.Max(1, page.Height), new L8(255));
            for (var y = 0; y < page.Height; y++)
            for (var x = 0; x < page.Width; x++)
                if (page[x, y])
                    image[x, y] = new L8(0);
            // The height follows the template, with a limit so that a roll of paper does not become a huge picture.
            var height = Math.Min(Math.Max(1, (int)Math.Round(page.Height * (double)width / page.Width)), width * 3);
            image.Mutate(x => x.Resize(width, height, KnownResamplers.Box));
            return Encode(image);
        }
        catch (TemplateException)
        {
            return Placeholder(width);
        }
    }

    /// <summary>A grey box for a template that cannot be drawn without something the user has still to provide (an image, a code…).</summary>
    private static byte[] Placeholder(int width)
    {
        var height = width * 3 / 4;
        using var image = new Image<L8>(width, height, new L8(225));
        for (var x = 0; x < width; x++)
        {
            image[x, 0] = new L8(120);
            image[x, height - 1] = new L8(120);
        }
        for (var y = 0; y < height; y++)
        {
            image[0, y] = new L8(120);
            image[width - 1, y] = new L8(120);
        }
        return Encode(image);
    }

    private static byte[] Encode(Image<L8> image)
    {
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
}
