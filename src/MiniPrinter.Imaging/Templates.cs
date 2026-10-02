using System.Text.RegularExpressions;
using MiniPrinter.Protocol;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ZXing;
using ZXing.QrCode.Internal;

namespace MiniPrinter.Imaging;

public enum TemplateFieldKind
{
    Text,
    MultilineText,
    Choice,
    /// <summary>Image bytes, Base64-encoded when sent as text (API) or a file path (CLI).</summary>
    Image,
}

public sealed record TemplateField(string Name, string Label, TemplateFieldKind Kind, bool Required, IReadOnlyList<string>? Choices = null, string? Default = null);

public sealed record TemplateDefinition(string Name, string Title, string Description, IReadOnlyList<TemplateField> Fields, bool IsImage = false);

/// <summary>A template request could not be rendered (missing field, invalid code, too long…).</summary>
public sealed class TemplateException(string message) : Exception(message);

/// <summary>
/// Built-in print templates rendered at the head width (design.md D7): QR, barcode, to-do list,
/// label and sticker. Line art is produced directly in 1 bit (no dithering).
/// </summary>
public static partial class TemplateRenderer
{
    public const int MinQrModule = 4;
    public const int MinBarWidth = 2;
    private const int Margin = TextRenderer.MarginPx;

    public static readonly IReadOnlyList<TemplateDefinition> Definitions =
    [
        new("qr", "Código QR", "Código QR con texto opcional debajo.",
        [
            new("data", "Contenido (texto o URL)", TemplateFieldKind.MultilineText, true),
            new("caption", "Texto debajo", TemplateFieldKind.Text, false),
        ]),
        new("barcode", "Código de barras", "Code 128 (cualquier texto) o EAN-13 (12 o 13 dígitos).",
        [
            new("data", "Contenido", TemplateFieldKind.Text, true),
            new("format", "Formato", TemplateFieldKind.Choice, false, ["code128", "ean13"], "code128"),
            new("caption", "Texto debajo (por defecto, el contenido)", TemplateFieldKind.Text, false),
        ]),
        new("todo", "Lista de tareas", "Título y una tarea por línea, cada una con su casilla.",
        [
            new("title", "Título", TemplateFieldKind.Text, false),
            new("items", "Tareas (una por línea)", TemplateFieldKind.MultilineText, true),
        ]),
        new("label", "Etiqueta", "Título grande y una o dos líneas de texto, centrados.",
        [
            new("title", "Título", TemplateFieldKind.Text, true),
            new("line1", "Línea 1", TemplateFieldKind.Text, false),
            new("line2", "Línea 2", TemplateFieldKind.Text, false),
        ]),
        new("sticker", "Pegatina", "Imagen ajustada al ancho con texto opcional.",
        [
            new("image", "Imagen", TemplateFieldKind.Image, true),
            new("caption", "Texto debajo", TemplateFieldKind.Text, false),
        ], IsImage: true),
    ];

    public static TemplateDefinition? Find(string name) =>
        Definitions.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

    public static MonoBitmap Render(string name, IReadOnlyDictionary<string, string> fields, int width = 384)
    {
        var definition = Find(name) ?? throw new TemplateException(
            $"Plantilla desconocida: '{name}'. Disponibles: {string.Join(", ", Definitions.Select(d => d.Name))}.");
        foreach (var field in definition.Fields.Where(f => f.Required))
            if (string.IsNullOrWhiteSpace(Get(fields, field.Name)))
                throw new TemplateException($"Falta el campo obligatorio '{field.Name}' ({field.Label}).");

        return definition.Name switch
        {
            "qr" => Qr(Get(fields, "data")!, Get(fields, "caption"), width),
            "barcode" => Barcode(Get(fields, "data")!, Get(fields, "format") ?? "code128", Get(fields, "caption"), width),
            "todo" => Todo(Get(fields, "title"), Get(fields, "items")!, width),
            "label" => Label(Get(fields, "title")!, Get(fields, "line1"), Get(fields, "line2"), width),
            "sticker" => Sticker(Get(fields, "image")!, Get(fields, "caption"), width),
            _ => throw new TemplateException($"Plantilla desconocida: '{name}'."),
        };
    }

    // ---- QR ------------------------------------------------------------------------------------

    public static MonoBitmap Qr(string data, string? caption, int width = 384)
    {
        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.CHARACTER_SET] = "UTF-8" };
        QRCode code;
        try
        {
            code = Encoder.encode(data, ErrorCorrectionLevel.M, hints);
        }
        catch (WriterException)
        {
            throw new TemplateException("El contenido es demasiado largo para un código QR.");
        }
        var matrix = code.Matrix;
        var modules = matrix.Width;
        var module = width / (modules + 8); // 4-module quiet zone on each side
        if (module < MinQrModule)
            throw new TemplateException("El contenido es demasiado largo para el ancho del papel.");

        var size = modules * module;
        var qr = new MonoBitmap(width, size + 8 * module);
        var left = (width - size) / 2;
        var top = 4 * module;
        for (var y = 0; y < modules; y++)
        for (var x = 0; x < modules; x++)
        {
            if (matrix[x, y] != 1)
                continue;
            for (var dy = 0; dy < module; dy++)
            for (var dx = 0; dx < module; dx++)
                qr[left + x * module + dx, top + y * module + dy] = true;
        }
        return Stack([qr, .. Caption(caption, width)]);
    }

    // ---- Barcode -------------------------------------------------------------------------------

    public static MonoBitmap Barcode(string data, string format, string? caption, int width = 384)
    {
        data = data.Trim();
        ZXing.Common.BitMatrix matrix;
        if (string.Equals(format, "ean13", StringComparison.OrdinalIgnoreCase))
        {
            if (!EanDigits().IsMatch(data))
                throw new TemplateException("Un EAN-13 necesita 12 o 13 dígitos.");
            var expected = Ean13CheckDigit(data[..12]);
            if (data.Length == 13 && data[12] - '0' != expected)
                throw new TemplateException($"Dígito de control EAN-13 incorrecto: debería ser {expected}.");
            matrix = new ZXing.OneD.EAN13Writer().encode(data[..12] + expected, BarcodeFormat.EAN_13, 0, 0);
        }
        else if (string.Equals(format, "code128", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                matrix = new ZXing.OneD.Code128Writer().encode(data, BarcodeFormat.CODE_128, 0, 0);
            }
            catch (ArgumentException ex)
            {
                throw new TemplateException($"No se puede codificar en Code 128: {ex.Message}");
            }
        }
        else
        {
            throw new TemplateException($"Formato de código de barras desconocido: '{format}' (code128 o ean13).");
        }

        // The writer returns one pixel per module when asked for width 0.
        var modules = matrix.Width;
        var bar = (width - 2 * Margin) / modules;
        if (bar < MinBarWidth)
            throw new TemplateException("El contenido es demasiado largo para el ancho del papel.");
        const int height = 96; // 12 mm
        var image = new MonoBitmap(width, height);
        var left = (width - modules * bar) / 2;
        for (var x = 0; x < modules; x++)
        {
            if (!matrix[x, 0])
                continue;
            for (var dx = 0; dx < bar; dx++)
            for (var y = 0; y < height; y++)
                image[left + x * bar + dx, y] = true;
        }
        var text = caption ?? (string.Equals(format, "ean13", StringComparison.OrdinalIgnoreCase) ? data[..12] + Ean13CheckDigit(data[..12]) : data);
        return Stack([image, .. Caption(text, width)]);
    }

    public static int Ean13CheckDigit(string twelveDigits)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++)
            sum += (twelveDigits[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - sum % 10) % 10;
    }

    // ---- To-do list ----------------------------------------------------------------------------

    public static MonoBitmap Todo(string? title, string items, int width = 384)
    {
        var blocks = new List<MonoBitmap>();
        if (!string.IsNullOrWhiteSpace(title))
            blocks.Add(TextRenderer.Render(title, new TextStyle { SizePt = 16, Bold = true }, width));

        const int box = 40;      // 5 mm
        const int gapAfterBox = 12;
        var indent = Margin + box + gapAfterBox;
        foreach (var item in items.Replace("\r", "").Split('\n').Select(i => i.Trim()).Where(i => i.Length > 0))
        {
            var text = TextRenderer.Render(item, new TextStyle { SizePt = 12 }, width - indent + Margin);
            var height = Math.Max(text.Height, box + 8);
            var line = new MonoBitmap(width, height);
            // Text (rendered with its own left margin) shifted right of the box.
            for (var y = 0; y < text.Height; y++)
            for (var x = 0; x < text.Width; x++)
                if (text[x, y]) line[x - Margin + indent, y] = true;
            // Checkbox with a 3-dot border.
            var top = 4;
            for (var y = top; y < top + box; y++)
            for (var x = Margin; x < Margin + box; x++)
                if (y < top + 3 || y >= top + box - 3 || x < Margin + 3 || x >= Margin + box - 3)
                    line[x, y] = true;
            blocks.Add(line);
        }
        if (blocks.Count == 0)
            throw new TemplateException("La lista no tiene tareas.");
        return Stack(blocks, gap: 6);
    }

    // ---- Label ---------------------------------------------------------------------------------

    public static MonoBitmap Label(string title, string? line1, string? line2, int width = 384)
    {
        var blocks = new List<MonoBitmap> { TextRenderer.Render(title, new TextStyle { SizePt = 26, Bold = true, Align = TextAlign.Center }, width) };
        foreach (var line in new[] { line1, line2 }.Where(l => !string.IsNullOrWhiteSpace(l)))
            blocks.Add(TextRenderer.Render(line!, new TextStyle { SizePt = 14, Align = TextAlign.Center }, width));
        return Stack(blocks, gap: 8);
    }

    // ---- Sticker -------------------------------------------------------------------------------

    public static MonoBitmap Sticker(string imageBase64, string? caption, int width = 384)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(imageBase64);
        }
        catch (System.FormatException)
        {
            throw new TemplateException("La imagen debe enviarse en Base64.");
        }
        GrayImage page;
        try
        {
            page = ImageDecoder.Decode(new MemoryStream(bytes)).First();
        }
        catch (NotSupportedException)
        {
            throw new TemplateException("Formato de imagen no admitido (PNG o JPEG).");
        }
        var picture = Rasterizer.Rasterize(page, new RasterOptions { WidthPx = width, Dither = DitherMode.Atkinson, BottomMarginRows = 0 });
        return Stack([picture, .. Caption(caption, width)]);
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static IEnumerable<MonoBitmap> Caption(string? caption, int width) =>
        string.IsNullOrWhiteSpace(caption) ? [] : [TextRenderer.Render(caption, new TextStyle { SizePt = 12, Align = TextAlign.Center }, width)];

    /// <summary>Stacks blocks vertically with <paramref name="gap"/> blank rows between them.</summary>
    public static MonoBitmap Stack(IReadOnlyList<MonoBitmap> blocks, int gap = 8)
    {
        var width = blocks.Max(b => b.Width);
        var height = blocks.Sum(b => b.Height) + gap * Math.Max(0, blocks.Count - 1);
        var result = new MonoBitmap(width, height);
        var top = 0;
        foreach (var block in blocks)
        {
            for (var y = 0; y < block.Height; y++)
                block.Row(y).CopyTo(result.MutableRow(top + y));
            top += block.Height + gap;
        }
        return result;
    }

    private static string? Get(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    [GeneratedRegex(@"^\d{12,13}$")]
    private static partial Regex EanDigits();
}
