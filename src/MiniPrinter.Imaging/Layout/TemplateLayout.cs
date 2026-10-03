using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MiniPrinter.Imaging;

/// <summary>One block of a template: its type plus the raw JSON properties.</summary>
public sealed class BlockSpec(int index, string type, JsonObject json)
{
    /// <summary>1-based position in the template, for error messages.</summary>
    public int Index { get; } = index;
    public string Type { get; } = type;
    public JsonObject Json { get; } = json;
    public string Where => $"Bloque {Index} ({Type})";
}

/// <summary>A parsed and validated template (design.md D1).</summary>
public sealed partial class TemplateLayout
{
    public const int MaxJsonBytes = 64 * 1024;
    public const int MaxBlocks = 100;

    public required string Name { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public bool IsImage { get; init; }
    public required IReadOnlyList<TemplateField> Fields { get; init; }
    public required IReadOnlyList<BlockSpec> Blocks { get; init; }

    /// <summary>Blank rows between blocks.</summary>
    public int Gap { get; init; } = 8;

    /// <summary>When it resolves to a true value, a border is drawn around the whole template.</summary>
    public string? Frame { get; init; }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,39}$")]
    private static partial Regex NamePattern();

    /// <summary>CLI subcommands and API routes (templates/schema, preview, drafts): a template cannot take these names.</summary>
    public static readonly string[] ReservedNames = ["list", "show", "add", "remove", "validate", "fields", "schema", "preview", "drafts"];

    public static bool IsValidName(string? name) =>
        name is not null && NamePattern().IsMatch(name) && !ReservedNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    public TemplateDefinition ToDefinition(string source = "builtin") =>
        new(Name, Title, Description, Fields, IsImage, source);

    /// <summary>Parses and fully validates a template; throws <see cref="TemplateException"/> with the reason.</summary>
    /// <param name="assetExists">Checks that an image referenced by a block exists (null skips the check).</param>
    public static TemplateLayout Parse(string json, Func<string, bool>? assetExists = null)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
            throw new TemplateException($"La plantilla supera el máximo de {MaxJsonBytes / 1024} KB.");

        JsonObject root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject ?? throw new TemplateException("La plantilla debe ser un objeto JSON.");
        }
        catch (JsonException ex)
        {
            throw new TemplateException($"JSON no válido: {ex.Message}");
        }

        var name = Text(root, "name") ?? throw new TemplateException("Falta el campo 'name' de la plantilla.");
        if (!IsValidName(name))
            throw new TemplateException($"Nombre de plantilla no válido: '{name}' (letras, dígitos, guion y guion bajo; máximo 40).");
        var title = Text(root, "title") ?? name;
        var mode = Text(root, "mode") ?? "text";
        if (mode is not ("text" or "image"))
            throw new TemplateException("'mode' debe ser 'text' o 'image'.");

        var fields = ParseFields(root["fields"]);
        var blocks = ParseBlocks(root["blocks"], fields, assetExists);
        var gap = root["gap"] is JsonValue g && g.TryGetValue<int>(out var gapValue) ? gapValue : 8;
        if (gap is < 0 or > 100)
            throw new TemplateException("'gap' debe estar entre 0 y 100.");
        var frame = Text(root, "frame");
        if (frame is not null)
            CheckPlaceholders(frame, fields, "frame");

        return new TemplateLayout
        {
            Name = name,
            Title = title,
            Description = Text(root, "description") ?? "",
            IsImage = mode == "image",
            Fields = fields,
            Blocks = blocks,
            Gap = gap,
            Frame = frame,
        };
    }

    private static string? Text(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    private static List<TemplateField> ParseFields(JsonNode? node)
    {
        var fields = new List<TemplateField>();
        if (node is null)
            return fields;
        if (node is not JsonArray array)
            throw new TemplateException("'fields' debe ser una lista.");
        foreach (var item in array)
        {
            var o = item as JsonObject ?? throw new TemplateException("Cada campo debe ser un objeto.");
            var name = Text(o, "name") ?? throw new TemplateException("Un campo no tiene 'name'.");
            if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$") || name.Equals("now", StringComparison.OrdinalIgnoreCase)
                || name.Equals("counter", StringComparison.OrdinalIgnoreCase))
                throw new TemplateException($"Nombre de campo no válido: '{name}'.");
            if (fields.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new TemplateException($"Campo duplicado: '{name}'.");
            var kind = ParseKind(Text(o, "kind") ?? "text", name);
            string[]? choices = o["choices"] is JsonArray c ? [.. c.Select(x => x?.GetValue<string>() ?? "")] : null;
            if (kind == TemplateFieldKind.Choice && (choices is null || choices.Length == 0))
                throw new TemplateException($"El campo '{name}' es de tipo 'choice' y necesita 'choices'.");
            fields.Add(new TemplateField(name, Text(o, "label") ?? name, kind,
                o["required"] is JsonValue r && r.TryGetValue<bool>(out var req) && req, choices, Text(o, "default")));
        }
        return fields;
    }

    private static TemplateFieldKind ParseKind(string kind, string field) => kind.ToLowerInvariant() switch
    {
        "text" => TemplateFieldKind.Text,
        "multiline" or "multilinetext" => TemplateFieldKind.MultilineText,
        "choice" => TemplateFieldKind.Choice,
        "image" => TemplateFieldKind.Image,
        "number" => TemplateFieldKind.Number,
        "boolean" or "bool" => TemplateFieldKind.Boolean,
        _ => throw new TemplateException($"Tipo de campo desconocido en '{field}': '{kind}' (text, multiline, choice, image, number, boolean)."),
    };

    private static List<BlockSpec> ParseBlocks(JsonNode? node, List<TemplateField> fields, Func<string, bool>? assetExists)
    {
        if (node is not JsonArray array || array.Count == 0)
            throw new TemplateException("La plantilla necesita al menos un bloque en 'blocks'.");
        if (array.Count > MaxBlocks)
            throw new TemplateException($"La plantilla tiene {array.Count} bloques; el máximo es {MaxBlocks}.");

        var blocks = new List<BlockSpec>();
        foreach (var item in array)
        {
            var o = item as JsonObject ?? throw new TemplateException($"El bloque {blocks.Count + 1} debe ser un objeto.");
            var type = Text(o, "type") ?? throw new TemplateException($"Al bloque {blocks.Count + 1} le falta 'type'.");
            var spec = new BlockSpec(blocks.Count + 1, type, o);
            var definition = LayoutBlocks.Find(type) ?? throw new TemplateException(
                $"{spec.Where}: tipo de bloque desconocido. Disponibles: {string.Join(", ", LayoutBlocks.Names)}.", spec.Index, "type");
            definition.Validate(spec, fields, assetExists);
            blocks.Add(spec);
        }
        return blocks;
    }

    /// <summary>Every placeholder in <paramref name="text"/> must be a declared field, now or counter.</summary>
    internal static void CheckPlaceholders(string text, IReadOnlyList<TemplateField> fields, string where, int? block = null, string? property = null)
    {
        foreach (var (name, _, filter) in Interpolator.Find(text))
        {
            if (!name.Equals("now", StringComparison.OrdinalIgnoreCase) && !name.Equals("counter", StringComparison.OrdinalIgnoreCase)
                && !fields.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new TemplateException($"{where}: el marcador '{{{{{name}}}}}' no corresponde a ningún campo declarado.", block, property);
            if (filter is not null && !Interpolator.Filters.Contains(filter))
                throw new TemplateException($"{where}: filtro desconocido '{filter}' (disponibles: {string.Join(", ", Interpolator.Filters)}).", block, property);
        }
    }
}
