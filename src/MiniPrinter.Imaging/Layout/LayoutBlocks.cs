using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using MiniPrinter.Protocol;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ZXing;
using ZXing.QrCode.Internal;

namespace MiniPrinter.Imaging;

internal enum PropKind
{
    Text,
    Int,
    Number,
    Bool,
    Enum,
}

/// <summary>Schema of one block property: used to validate literals and to read values at render time.</summary>
internal sealed record PropDef(PropKind Kind, double Min = 0, double Max = 0, double Default = 0, string[]? Values = null,
    string? Label = null, bool Multiline = false)
{
    /// <summary>The same property with a label (shown by the editor) and whether its text is multiline.</summary>
    public PropDef L(string label, bool multiline = false) => this with { Label = label, Multiline = multiline };

    public static readonly PropDef Text = new(PropKind.Text);
    public static readonly PropDef Bool = new(PropKind.Bool);
    public static PropDef Int(int min, int max, int def) => new(PropKind.Int, min, max, def);
    public static PropDef Num(double min, double max, double def) => new(PropKind.Number, min, max, def);
    public static PropDef Choice(params string[] values) => new(PropKind.Enum, Values: values);
}

internal delegate MonoBitmap? BlockRenderer(BlockSpec block, BlockContext context);

/// <summary>A block type: its allowed properties and how it renders (design.md D2).</summary>
internal sealed record BlockType(string Name, string Title, IReadOnlyDictionary<string, PropDef> Props, BlockRenderer Render,
    Action<BlockSpec, IReadOnlyList<TemplateField>, Func<string, bool>?>? Extra = null)
{
    public void Validate(BlockSpec block, IReadOnlyList<TemplateField> fields, Func<string, bool>? assetExists)
    {
        foreach (var (key, node) in block.Json)
        {
            if (key is "type")
                continue;
            if (key is "when")
            {
                if (BlockContext.ReadRaw(node) is { } w)
                    TemplateLayout.CheckPlaceholders(w, fields, block.Where, block.Index, "when");
                continue;
            }
            if (!Props.TryGetValue(key, out var def))
                throw new TemplateException($"{block.Where}: propiedad desconocida '{key}'. Disponibles: {string.Join(", ", Props.Keys)}.", block.Index, key);
            var raw = BlockContext.ReadRaw(node);
            if (raw is null)
                continue;
            TemplateLayout.CheckPlaceholders(raw, fields, block.Where, block.Index, key);
            if (!raw.Contains("{{"))
                BlockContext.Parse(def, raw, block, key);
        }
        Extra?.Invoke(block, fields, assetExists);
    }
}

/// <summary>What a block needs to render: width, interpolation, and typed property access.</summary>
internal sealed class BlockContext
{
    public required int Width { get; init; }
    public required Interpolator Interpolator { get; init; }
    public string? AssetsDir { get; init; }

    /// <summary>Where an image is looked for when it is not in <see cref="AssetsDir"/>.</summary>
    public string? FallbackAssetsDir { get; init; }

    public static string? ReadRaw(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
        JsonValue v when v.TryGetValue<double>(out var d) => d.ToString(CultureInfo.InvariantCulture),
        _ => throw new TemplateException("Las propiedades de un bloque deben ser texto, número o booleano."),
    };

    /// <summary>Interpolated value, or null when the property is absent.</summary>
    public string? Raw(BlockSpec block, string name) =>
        ReadRaw(block.Json[name]) is { } raw ? Interpolator.Apply(raw) : null;

    /// <summary>Interpolated value, or null when absent or blank.</summary>
    public string? Str(BlockSpec block, string name) => Raw(block, name) is { } s && !string.IsNullOrWhiteSpace(s) ? s : null;

    public bool Present(BlockSpec block, string name) => block.Json.ContainsKey(name);

    private PropDef Def(BlockSpec block, string name) => LayoutBlocks.Find(block.Type)!.Props[name];

    public int Int(BlockSpec block, string name)
    {
        var def = Def(block, name);
        return Str(block, name) is { } s ? (int)Parse(def, s, block, name)! : (int)def.Default;
    }

    public double Num(BlockSpec block, string name)
    {
        var def = Def(block, name);
        return Str(block, name) is { } s ? (double)Parse(def, s, block, name)! : def.Default;
    }

    public bool Bool(BlockSpec block, string name) => Str(block, name) is { } s && (bool)Parse(PropDef.Bool, s, block, name)!;

    public string Choice(BlockSpec block, string name)
    {
        var def = Def(block, name);
        return Str(block, name) is { } s ? (string)Parse(def, s, block, name)! : def.Values![0];
    }

    public static bool Truthy(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Trim().ToLowerInvariant().Equals("false")
        && value.Trim() is not ("0" or "no" or "off" or "No");

    internal static object? Parse(PropDef def, string raw, BlockSpec block, string name)
    {
        raw = raw.Trim();
        switch (def.Kind)
        {
            case PropKind.Text:
                return raw;
            case PropKind.Bool:
                return Truthy(raw);
            case PropKind.Int:
            case PropKind.Number:
                if (!double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw new TemplateException($"{block.Where}: '{name}' debe ser un número (valor: '{raw}').", block.Index, name);
                if (number < def.Min || number > def.Max)
                    throw new TemplateException($"{block.Where}: '{name}' debe estar entre {def.Min:0.##} y {def.Max:0.##}.", block.Index, name);
                return def.Kind == PropKind.Int ? (object)(int)Math.Round(number) : number;
            default:
                var value = def.Values!.FirstOrDefault(v => v.Equals(raw, StringComparison.OrdinalIgnoreCase))
                    ?? throw new TemplateException($"{block.Where}: '{name}' debe ser uno de: {string.Join(", ", def.Values!)} (valor: '{raw}').", block.Index, name);
                return value;
        }
    }
}

/// <summary>Registry of block types (replaces the per-name switch of the old renderer).</summary>
internal static class LayoutBlocks
{
    private const int Margin = TextRenderer.MarginPx;
    public const int Dots = 8; // dots per millimetre at 203 dpi

    private static readonly Dictionary<string, BlockType> Types = Build();

    public static IEnumerable<string> Names => Types.Keys;

    public static IEnumerable<BlockType> All => Types.Values;

    public static BlockType? Find(string name) => Types.GetValueOrDefault(name);

    private static Dictionary<string, PropDef> P(params (string Name, PropDef Def)[] props) => props.ToDictionary(p => p.Name, p => p.Def);

    private static Dictionary<string, BlockType> Build()
    {
        var types = new[]
        {
            new BlockType("text", "Texto", P(
                ("value", PropDef.Text.L("Texto", multiline: true)), ("size", PropDef.Num(6, 48, 10).L("Tamaño (pt)")),
                ("bold", PropDef.Bool.L("Negrita")), ("align", PropDef.Choice("left", "center", "right").L("Alineación"))), RenderText),
            new BlockType("spacer", "Espacio", P(("mm", PropDef.Num(0.5, 100, 2).L("Alto (mm)"))), RenderSpacer),
            new BlockType("line", "Línea", P(("style", PropDef.Choice("solid", "dotted", "dashed").L("Estilo")),
                ("thickness", PropDef.Int(1, 8, 2).L("Grosor (puntos)"))), RenderLine),
            new BlockType("qr", "Código QR", P(("data", PropDef.Text.L("Contenido", multiline: true)), ("caption", PropDef.Text.L("Texto debajo")),
                ("ecc", PropDef.Choice("M", "L", "Q", "H").L("Corrección de errores")),
                ("module", PropDef.Int(1, 40, 0).L("Tamaño de módulo (vacío = automático)"))), RenderQr),
            new BlockType("barcode", "Código de barras", P(("data", PropDef.Text.L("Contenido")),
                ("format", PropDef.Choice("code128", "code39", "ean13", "upca").L("Formato")), ("caption", PropDef.Text.L("Texto debajo")),
                ("height", PropDef.Num(4, 50, 12).L("Altura (mm)"))), RenderBarcode),
            new BlockType("list", "Lista", P(("items", PropDef.Text.L("Elementos (uno por línea)", multiline: true)),
                ("marker", PropDef.Choice("box", "bullet", "number", "none").L("Marcador")), ("size", PropDef.Num(6, 48, 12).L("Tamaño (pt)")),
                ("gap", PropDef.Int(0, 50, 6).L("Hueco entre elementos")), ("quantities", PropDef.Bool.L("Cantidad después de ;")),
                ("leader", PropDef.Choice("none", "dots").L("Relleno hasta la cantidad"))), RenderList),
            new BlockType("columns", "Dos columnas", P(("left", PropDef.Text.L("Izquierda")), ("right", PropDef.Text.L("Derecha")),
                ("leader", PropDef.Choice("none", "dots").L("Relleno")), ("size", PropDef.Num(6, 48, 12).L("Tamaño (pt)")),
                ("bold", PropDef.Bool.L("Negrita"))), RenderColumns),
            new BlockType("image", "Imagen", P(("data", PropDef.Text.L("Imagen en Base64 (o un campo)")),
                ("source", PropDef.Text.L("Archivo de la plantilla")), ("caption", PropDef.Text.L("Texto debajo"))), RenderImage,
                ValidateImage),
        };
        return types.ToDictionary(t => t.Name);
    }

    // ---- text, spacer, line ------------------------------------------------------------------

    private static MonoBitmap? RenderText(BlockSpec b, BlockContext c)
    {
        var text = c.Str(b, "value");
        if (text is null)
            return null;
        return TextRenderer.Render(text, new TextStyle { SizePt = (float)c.Num(b, "size"), Bold = c.Bool(b, "bold"), Align = Align(c.Choice(b, "align")) }, c.Width);
    }

    private static TextAlign Align(string value) => value switch
    {
        "center" => TextAlign.Center,
        "right" => TextAlign.Right,
        _ => TextAlign.Left,
    };

    private static MonoBitmap? RenderSpacer(BlockSpec b, BlockContext c) =>
        new MonoBitmap(c.Width, Math.Max(1, (int)Math.Round(c.Num(b, "mm") * Dots)));

    private static MonoBitmap? RenderLine(BlockSpec b, BlockContext c)
    {
        var thickness = c.Int(b, "thickness");
        var style = c.Choice(b, "style");
        var (on, off) = style switch
        {
            "dotted" => (thickness, thickness * 2),
            "dashed" => (thickness * 5, thickness * 3),
            _ => (int.MaxValue / 2, 0),
        };
        var line = new MonoBitmap(c.Width, thickness);
        for (var x = Margin; x < c.Width - Margin; x++)
        {
            if ((x - Margin) % (on + off) >= on)
                continue;
            for (var y = 0; y < thickness; y++)
                line[x, y] = true;
        }
        return line;
    }

    // ---- qr ------------------------------------------------------------------------------------

    private static MonoBitmap? RenderQr(BlockSpec b, BlockContext c)
    {
        var data = c.Raw(b, "data");
        if (string.IsNullOrWhiteSpace(data))
            throw new TemplateException($"{b.Where}: falta el contenido del código QR.", b.Index, "data");
        var level = c.Choice(b, "ecc") switch
        {
            "L" => ErrorCorrectionLevel.L,
            "Q" => ErrorCorrectionLevel.Q,
            "H" => ErrorCorrectionLevel.H,
            _ => ErrorCorrectionLevel.M,
        };
        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.CHARACTER_SET] = "UTF-8" };
        QRCode code;
        try
        {
            code = Encoder.encode(data, level, hints);
        }
        catch (WriterException)
        {
            throw new TemplateException("El contenido es demasiado largo para un código QR.");
        }
        var matrix = code.Matrix;
        var modules = matrix.Width;
        var module = c.Int(b, "module");
        var width = c.Width;
        if (module == 0)
        {
            module = width / (modules + 8); // 4-module quiet zone on each side
        }
        else if (module < TemplateRenderer.MinQrModule)
        {
            throw new TemplateException($"El tamaño de módulo mínimo es de {TemplateRenderer.MinQrModule} puntos.", b.Index, "module");
        }
        if (module < TemplateRenderer.MinQrModule || (modules + 8) * module > width)
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
        return Stack([qr, .. Caption(c.Str(b, "caption"), width)]);
    }

    // ---- barcode -------------------------------------------------------------------------------

    private static MonoBitmap? RenderBarcode(BlockSpec b, BlockContext c)
    {
        var data = c.Raw(b, "data")?.Trim();
        if (string.IsNullOrEmpty(data))
            throw new TemplateException($"{b.Where}: falta el contenido del código de barras.", b.Index, "data");
        var format = c.Choice(b, "format");
        var width = c.Width;
        ZXing.Common.BitMatrix matrix;
        string normalized = data;
        switch (format)
        {
            case "ean13":
            {
                if (!TemplateRenderer.Digits(data, 12, 13))
                    throw new TemplateException("Un EAN-13 necesita 12 o 13 dígitos.");
                var expected = TemplateRenderer.Ean13CheckDigit(data[..12]);
                if (data.Length == 13 && data[12] - '0' != expected)
                    throw new TemplateException($"Dígito de control EAN-13 incorrecto: debería ser {expected}.");
                normalized = data[..12] + expected;
                matrix = new ZXing.OneD.EAN13Writer().encode(normalized, BarcodeFormat.EAN_13, 0, 0);
                break;
            }
            case "upca":
            {
                if (!TemplateRenderer.Digits(data, 11, 12))
                    throw new TemplateException("Un UPC-A necesita 11 o 12 dígitos.");
                var expected = TemplateRenderer.UpcaCheckDigit(data[..11]);
                if (data.Length == 12 && data[11] - '0' != expected)
                    throw new TemplateException($"Dígito de control UPC-A incorrecto: debería ser {expected}.");
                normalized = data[..11] + expected;
                matrix = new ZXing.OneD.UPCAWriter().encode(normalized, BarcodeFormat.UPC_A, 0, 0);
                break;
            }
            case "code39":
            {
                normalized = data.ToUpperInvariant();
                foreach (var ch in normalized)
                    if (!char.IsAsciiLetterUpper(ch) && !char.IsAsciiDigit(ch) && !"-. $/+%".Contains(ch))
                        throw new TemplateException($"Carácter no admitido en Code 39: '{ch}' (A-Z, 0-9 y - . $ / + % espacio).");
                matrix = new ZXing.OneD.Code39Writer().encode(normalized, BarcodeFormat.CODE_39, 0, 0);
                break;
            }
            default:
                try
                {
                    matrix = new ZXing.OneD.Code128Writer().encode(data, BarcodeFormat.CODE_128, 0, 0);
                }
                catch (ArgumentException ex)
                {
                    throw new TemplateException($"No se puede codificar en Code 128: {ex.Message}");
                }
                break;
        }

        // The writer returns one pixel per module when asked for width 0.
        var modules = matrix.Width;
        var bar = (width - 2 * Margin) / modules;
        if (bar < TemplateRenderer.MinBarWidth)
            throw new TemplateException("El contenido es demasiado largo para el ancho del papel.");
        var height = (int)Math.Round(c.Num(b, "height") * Dots);
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
        // A present-but-blank caption means "show the code itself".
        var caption = c.Present(b, "caption") ? c.Str(b, "caption") ?? normalized : null;
        return Stack([image, .. Caption(caption, width)]);
    }

    // ---- list and columns ------------------------------------------------------------------------

    private static MonoBitmap? RenderList(BlockSpec b, BlockContext c)
    {
        var items = (c.Raw(b, "items") ?? "").Replace("\r", "").Split('\n').Select(i => i.Trim()).Where(i => i.Length > 0).ToList();
        if (items.Count == 0)
            throw new TemplateException("La lista no tiene tareas.");
        var width = c.Width;
        var size = (float)c.Num(b, "size");
        var marker = c.Choice(b, "marker");
        var quantities = c.Bool(b, "quantities");
        var dots = c.Choice(b, "leader") == "dots";
        var style = new TextStyle { SizePt = size };

        var box = (int)Math.Round(size * 10 / 3); // 5 mm at 12 pt
        const int gapAfterBox = 12;
        var hasBox = marker is "box" or "bullet";
        var indent = hasBox ? Margin + box + gapAfterBox : Margin;

        var rows = new List<MonoBitmap>();
        for (var i = 0; i < items.Count; i++)
        {
            string text = items[i], qty = "";
            if (quantities && text.LastIndexOf(';') is var cut and >= 0)
                (text, qty) = (text[..cut].Trim(), text[(cut + 1)..].Trim());
            if (marker == "number")
                text = $"{i + 1}. {text}";

            MonoBitmap line;
            if (qty.Length > 0 || (quantities && dots))
            {
                line = TwoColumn(text, qty, style, width, dots, indent);
            }
            else
            {
                var rendered = TextRenderer.Render(text, style, width - indent + Margin);
                line = new MonoBitmap(width, rendered.Height);
                // Text (rendered with its own left margin) shifted right of the marker.
                for (var y = 0; y < rendered.Height; y++)
                for (var x = 0; x < rendered.Width; x++)
                    if (rendered[x, y] && x - Margin + indent is var tx && tx >= 0 && tx < width)
                        line[tx, y] = true;
            }

            if (hasBox)
            {
                var height = Math.Max(line.Height, box + 8);
                var withBox = new MonoBitmap(width, height);
                for (var y = 0; y < line.Height; y++)
                    line.Row(y).CopyTo(withBox.MutableRow(y));
                const int top = 4;
                if (marker == "box")
                {
                    // Checkbox with a 3-dot border.
                    for (var y = top; y < top + box; y++)
                    for (var x = Margin; x < Margin + box; x++)
                        if (y < top + 3 || y >= top + box - 3 || x < Margin + 3 || x >= Margin + box - 3)
                            withBox[x, y] = true;
                }
                else
                {
                    var radius = Math.Max(2, box / 6);
                    var (cx, cy) = (Margin + box / 2, top + box / 2);
                    for (var y = cy - radius; y <= cy + radius; y++)
                    for (var x = cx - radius; x <= cx + radius; x++)
                        if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius)
                            withBox[x, y] = true;
                }
                line = withBox;
            }
            rows.Add(line);
        }
        return Stack(rows, c.Int(b, "gap"));
    }

    private static MonoBitmap? RenderColumns(BlockSpec b, BlockContext c)
    {
        var left = c.Raw(b, "left") ?? "";
        var right = c.Raw(b, "right") ?? "";
        if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
            return null;
        var style = new TextStyle { SizePt = (float)c.Num(b, "size"), Bold = c.Bool(b, "bold") };
        return TwoColumn(left.Trim(), right.Trim(), style, c.Width, c.Choice(b, "leader") == "dots", Margin);
    }

    /// <summary>
    /// A left text and a right-aligned text on the same row; the left one wraps when they do not
    /// both fit, and the right one sits on its last line. Optional dot leader between them.
    /// </summary>
    private static MonoBitmap TwoColumn(string left, string right, TextStyle style, int width, bool dots, int leftX)
    {
        const int gap = 12;
        var probe = TextRenderer.Options(style, width);
        var rightWidth = right.Length == 0 ? 0 : (int)Math.Ceiling(TextMeasurer.MeasureAdvance(right, probe).Width);
        rightWidth = Math.Min(rightWidth, (width - 2 * Margin) / 2);

        var leftOptions = TextRenderer.Options(style, width);
        leftOptions.Origin = new PointF(leftX, 0);
        leftOptions.WrappingLength = Math.Max(40, width - Margin - rightWidth - (rightWidth > 0 ? gap : 0) - leftX);
        var text = left.Length == 0 ? " " : left;
        var lines = Math.Max(1, TextMeasurer.CountLines(text, leftOptions));
        var measured = TextMeasurer.MeasureAdvance(text, leftOptions);
        var lineHeight = measured.Height / lines;
        var height = Math.Max(1, (int)Math.Ceiling(measured.Height) + 4);

        using var canvas = new Image<L8>(width, height, new L8(255));
        canvas.Mutate(ctx => ctx.DrawText(leftOptions, text, Color.Black));
        if (right.Length > 0)
        {
            var rightOptions = TextRenderer.Options(style, width);
            rightOptions.Origin = new PointF(width - Margin - rightWidth, (lines - 1) * lineHeight);
            rightOptions.WrappingLength = -1;
            canvas.Mutate(ctx => ctx.DrawText(rightOptions, right, Color.Black));
        }
        if (dots && right.Length > 0)
        {
            var lastEnd = TextMeasurer.TryMeasureCharacterBounds(text, leftOptions, out var bounds) && bounds.Length > 0
                ? bounds[^1].Bounds.Right
                : leftX;
            var from = (int)Math.Ceiling(lastEnd) + 8;
            var to = width - Margin - rightWidth - 8;
            var y = (int)((lines - 1) * lineHeight + lineHeight * 0.8);
            for (var x = from; x < to; x += 8)
            for (var dy = 0; dy < 2; dy++)
            for (var dx = 0; dx < 2; dx++)
                if (x + dx < width && y + dy < height)
                    canvas[x + dx, y + dy] = new L8(0);
        }
        return TextRenderer.ToMono(canvas);
    }

    // ---- image ---------------------------------------------------------------------------------

    private static readonly ConcurrentDictionary<(string Path, DateTime Stamp, int Width), MonoBitmap> AssetCache = new();

    private static MonoBitmap? RenderImage(BlockSpec b, BlockContext c)
    {
        var data = c.Str(b, "data");
        var source = c.Str(b, "source");
        MonoBitmap picture;
        if (data is not null)
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(data);
            }
            catch (System.FormatException)
            {
                throw new TemplateException("La imagen debe enviarse en Base64.");
            }
            picture = Dither(bytes, c.Width);
        }
        else if (source is not null)
        {
            var path = AssetPath(c.AssetsDir, source) ?? AssetPath(c.FallbackAssetsDir, source) ?? throw new TemplateException($"{b.Where}: la imagen '{source}' no existe.", b.Index, "source");
            var key = (path, File.GetLastWriteTimeUtc(path), c.Width);
            picture = AssetCache.GetOrAdd(key, _ => Dither(File.ReadAllBytes(path), c.Width));
        }
        else
        {
            return null;
        }
        return Stack([picture, .. Caption(c.Str(b, "caption"), c.Width)]);
    }

    private static MonoBitmap Dither(byte[] bytes, int width)
    {
        GrayImage page;
        try
        {
            page = ImageDecoder.Decode(new MemoryStream(bytes)).First();
        }
        catch (NotSupportedException)
        {
            throw new TemplateException("Formato de imagen no admitido (PNG o JPEG).");
        }
        return Rasterizer.Rasterize(page, new RasterOptions { WidthPx = width, Dither = DitherMode.Atkinson, BottomMarginRows = 0 });
    }

    private static string? AssetPath(string? dir, string file)
    {
        if (dir is null || !TemplateAssets.IsValidFileName(file))
            return null;
        var path = Path.Combine(dir, file);
        return File.Exists(path) ? path : null;
    }

    private static void ValidateImage(BlockSpec b, IReadOnlyList<TemplateField> fields, Func<string, bool>? assetExists)
    {
        var source = BlockContext.ReadRaw(b.Json["source"]);
        if (source is null || source.Contains("{{"))
            return;
        if (!TemplateAssets.IsValidFileName(source))
            throw new TemplateException($"{b.Where}: nombre de imagen no válido '{source}' (PNG o JPEG, sin rutas).", b.Index, "source");
        if (assetExists is not null && !assetExists(source))
            throw new TemplateException($"{b.Where}: la imagen '{source}' no existe en los recursos de la plantilla.", b.Index, "source");
    }

    // ---- helpers -------------------------------------------------------------------------------

    internal static IEnumerable<MonoBitmap> Caption(string? caption, int width) =>
        string.IsNullOrWhiteSpace(caption) ? [] : [TextRenderer.Render(caption, new TextStyle { SizePt = 12, Align = TextAlign.Center }, width)];

    /// <summary>Stacks blocks vertically with <paramref name="gap"/> blank rows between them.</summary>
    internal static MonoBitmap Stack(IReadOnlyList<MonoBitmap> blocks, int gap = 8)
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
}

/// <summary>Rules for the image files stored next to a template.</summary>
public static class TemplateAssets
{
    public const int MaxBytes = 1024 * 1024;

    /// <summary>PNG or JPEG by its first bytes.</summary>
    public static bool LooksLikeImage(byte[] c) =>
        c.Length > 8 && ((c[0] == 0x89 && c[1] == 0x50 && c[2] == 0x4E && c[3] == 0x47) || (c[0] == 0xFF && c[1] == 0xD8));

    public static bool IsValidFileName(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}\.(png|jpe?g)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
