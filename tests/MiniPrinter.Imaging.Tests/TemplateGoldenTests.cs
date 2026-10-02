using System.Runtime.CompilerServices;
using MiniPrinter.Protocol;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MiniPrinter.Imaging.Tests;

/// <summary>
/// Pixel-for-pixel regression of the built-in templates with their default options
/// (spec template-layouts: "Regresión de una plantilla existente"). Set UPDATE_GOLDEN=1 to
/// regenerate the PNG fixtures in Fixtures/templates.
/// </summary>
public class TemplateGoldenTests
{
    private static string Dir([CallerFilePath] string? self = null) =>
        Path.Combine(Path.GetDirectoryName(self)!, "Fixtures", "templates");

    private static Dictionary<string, string> F(params (string Key, string Value)[] fields) =>
        fields.ToDictionary(f => f.Key, f => f.Value);

    private static string GradientPng()
    {
        using var image = new Image<L8>(200, 100);
        for (var y = 0; y < 100; y++)
        for (var x = 0; x < 200; x++)
            image[x, y] = new L8((byte)((x + y) * 255 / 298));
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    public static TheoryData<string, string> Cases() => new()
    {
        { "qr", "qr" },
        { "barcode", "code128" },
        { "barcode", "ean13" },
        { "todo", "todo" },
        { "label", "label" },
        { "sticker", "sticker" },
    };

    private static Dictionary<string, string> Fields(string template, string variant) => (template, variant) switch
    {
        ("qr", _) => F(("data", "https://example.com"), ("caption", "Escanéame")),
        ("barcode", "code128") => F(("data", "MINI-0042")),
        ("barcode", "ean13") => F(("data", "590123412345"), ("format", "ean13")),
        ("todo", _) => F(("title", "Compra"), ("items", "Pan\nLeche\nHuevos")),
        ("label", _) => F(("title", "FRÁGIL"), ("line1", "Este lado arriba"), ("line2", "Gracias")),
        ("sticker", _) => F(("image", GradientPng()), ("caption", "Hola")),
        _ => throw new ArgumentException(template),
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Default_output_matches_the_reference(string template, string variant)
    {
        var bitmap = TemplateRenderer.Render(template, Fields(template, variant));
        var path = Path.Combine(Dir(), $"{template}-{variant}.png");
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Dir());
            MonoPng.Save(bitmap, path);
        }

        using var reference = Image.Load<L8>(path);
        Assert.Equal(reference.Width, bitmap.Width);
        Assert.Equal(reference.Height, bitmap.Height);
        var diff = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            if (bitmap[x, y] != (reference[x, y].PackedValue < 128)) diff++;
        Assert.Equal(0, diff);
    }
}
