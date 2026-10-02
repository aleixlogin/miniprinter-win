using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MiniPrinter.Control;

/// <summary>
/// The template being edited, as the editor window sees it (design.md D1, D7): one <see cref="JsonObject"/>
/// with the same structure the service validates, plus undo/redo and the helpers the UI needs (blocks,
/// fields, placeholders in use). It lives here, away from WPF, so it can be tested without a UI.
/// </summary>
public sealed partial class TemplateEditorModel
{
    public const int MaxHistory = 100;
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(600);
    public static readonly string[] ReservedNames = ["list", "show", "add", "remove", "validate", "schema", "preview", "drafts"];

    private static readonly JsonSerializerOptions Canonical = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly TemplateSchemaDto _schema;
    private readonly Func<DateTime> _clock;
    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];
    private JsonObject _root;
    private string _saved;
    private string? _lastKey;
    private DateTime _lastEdit;

    /// <summary>Raised after every change to the template (including undo and redo).</summary>
    public event Action? Changed;

    /// <param name="json">The template JSON; comments and trailing commas are accepted.</param>
    /// <exception cref="InvalidDataException">The text is not a JSON object.</exception>
    public TemplateEditorModel(string json, TemplateSchemaDto schema, Func<DateTime>? clock = null)
    {
        _schema = schema;
        _clock = clock ?? (() => DateTime.UtcNow);
        HadComments = HasComments(json);
        _root = Parse(json);
        _saved = ToJson(_root);
    }

    /// <summary>True when the original text had comments, which are lost when the editor saves.</summary>
    public bool HadComments { get; }

    public JsonObject Root => _root;

    /// <summary>The canonical JSON (what is saved and previewed).</summary>
    public string Json => ToJson(_root);

    public string Name => Text(_root, "name") ?? "";

    public bool IsDirty => Json != _saved;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void MarkSaved() => _saved = Json;

    // ---- creation ------------------------------------------------------------------------------

    /// <summary>A blank template with a first text block.</summary>
    public static string Blank(string name) =>
        ToJson(new JsonObject
        {
            ["name"] = name,
            ["title"] = name,
            ["description"] = "",
            ["fields"] = new JsonArray(),
            ["blocks"] = new JsonArray(new JsonObject { ["type"] = "text", ["value"] = "Texto", ["size"] = 14, ["align"] = "center" }),
        });

    /// <summary>A copy of an existing template under a new name.</summary>
    public static string Duplicate(string json, string newName)
    {
        var root = Parse(json);
        root["name"] = newName;
        root["title"] = $"{Text(root, "title") ?? Text(root, "name") ?? newName} (copia)";
        return ToJson(root);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,39}$")]
    private static partial Regex NamePattern();

    /// <summary>The same rule the service applies to template names.</summary>
    public static bool IsValidName(string? name) =>
        name is not null && NamePattern().IsMatch(name) && !ReservedNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    // ---- blocks --------------------------------------------------------------------------------

    private JsonArray BlocksArray => _root["blocks"] as JsonArray ?? (JsonArray)(_root["blocks"] = new JsonArray());

    public int BlockCount => BlocksArray.Count;

    public JsonObject Block(int index) => (JsonObject)BlocksArray[index]!;

    public string BlockType(int index) => Text(Block(index), "type") ?? "?";

    public bool CanAddBlock => BlockCount < _schema.Template.MaxBlocks;

    /// <summary>Appends (or inserts) a block of the given type with example values and returns its index.</summary>
    public int AddBlock(string type, int? at = null)
    {
        if (!CanAddBlock)
            throw new InvalidOperationException($"Una plantilla admite como máximo {_schema.Template.MaxBlocks} bloques.");
        if (_schema.Blocks.All(b => b.Type != type))
            throw new ArgumentException($"Tipo de bloque desconocido: {type}.", nameof(type));
        Checkpoint();
        var block = new JsonObject { ["type"] = type };
        foreach (var (name, value) in Starter(type))
            block[name] = value;
        var index = Math.Clamp(at ?? BlockCount, 0, BlockCount);
        BlocksArray.Insert(index, block);
        Raise();
        return index;
    }

    private static IEnumerable<(string, JsonNode)> Starter(string type) => type switch
    {
        "text" => [("value", "Texto")],
        "qr" => [("data", "https://example.com")],
        "barcode" => [("data", "123456789012"), ("format", "code128")],
        "list" => [("items", "Uno\nDos\nTres"), ("marker", "box")],
        "columns" => [("left", "Izquierda"), ("right", "Derecha"), ("leader", "dots")],
        "spacer" => [("mm", 3)],
        _ => [],
    };

    public void RemoveBlock(int index)
    {
        Checkpoint();
        BlocksArray.RemoveAt(index);
        Raise();
    }

    /// <summary>Duplicates a block right below itself and returns the index of the copy.</summary>
    public int DuplicateBlock(int index)
    {
        if (!CanAddBlock)
            throw new InvalidOperationException($"Una plantilla admite como máximo {_schema.Template.MaxBlocks} bloques.");
        Checkpoint();
        BlocksArray.Insert(index + 1, Block(index).DeepClone());
        Raise();
        return index + 1;
    }

    /// <summary>Moves a block so that it ends up at <paramref name="to"/> (0-based, in the final list).</summary>
    public void MoveBlock(int from, int to)
    {
        to = Math.Clamp(to, 0, BlockCount - 1);
        if (from == to)
            return;
        Checkpoint();
        var node = BlocksArray[from]!;
        BlocksArray.RemoveAt(from);
        BlocksArray.Insert(to, node);
        Raise();
    }

    /// <summary>
    /// Sets a block property from the text a control holds: blank removes it. Values are stored with their
    /// natural JSON type (numbers, booleans) unless they contain a <c>{{placeholder}}</c>.
    /// </summary>
    /// <param name="coalesceKey">Edits with the same key within 600 ms form a single undo step (typing).</param>
    public void SetBlockProperty(int index, SchemaPropDto prop, string? raw, string? coalesceKey = null)
    {
        var block = Block(index);
        var node = ToNode(prop, raw);
        if (JsonEquals(block[prop.Name], node))
            return;
        Checkpoint(coalesceKey);
        if (node is null)
            block.Remove(prop.Name);
        else
            block[prop.Name] = node;
        Raise();
    }

    /// <summary>The text a control should show for a block property ("" when it is not set).</summary>
    public string GetBlockProperty(int index, string name) => Display(Block(index)[name]);

    private static JsonNode? ToNode(SchemaPropDto prop, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return null;
        if (raw.Contains("{{"))
            return raw;
        switch (prop.Kind)
        {
            case "bool":
                return raw.Trim().ToLowerInvariant() is "true" or "1" or "sí" or "si" ? JsonValue.Create(true) : null;
            case "int" when int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i):
                return JsonValue.Create(i);
            case "number" when double.TryParse(raw.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d):
                return JsonValue.Create(d);
            default:
                return raw;   // text, enum, or a number the service will reject with a message
        }
    }

    // ---- template properties and fields ----------------------------------------------------------

    /// <summary>Sets title, description, mode, gap or frame; blank removes it (name is only set by <see cref="Rename"/>).</summary>
    public void SetTemplateProperty(string name, string? raw, string? coalesceKey = null)
    {
        JsonNode? node = string.IsNullOrEmpty(raw) ? null
            : name == "gap" && int.TryParse(raw.Trim(), out var gap) ? JsonValue.Create(gap)
            : raw;
        if (JsonEquals(_root[name], node))
            return;
        Checkpoint(coalesceKey);
        if (node is null)
            _root.Remove(name);
        else
            _root[name] = node;
        Raise();
    }

    public string GetTemplateProperty(string name) => Display(_root[name]);

    /// <summary>Changes the template's name (only for a template that is not saved yet, or Save as…).</summary>
    public void Rename(string name)
    {
        if (name == Name)
            return;
        Checkpoint();
        _root["name"] = name;
        Raise();
    }

    private JsonArray FieldsArray => _root["fields"] as JsonArray ?? (JsonArray)(_root["fields"] = new JsonArray());

    public int FieldCount => FieldsArray.Count;

    public JsonObject Field(int index) => (JsonObject)FieldsArray[index]!;

    public IReadOnlyList<string> FieldNames => [.. FieldsArray.Select(f => Text((JsonObject)f!, "name") ?? "")];

    private static readonly Regex FieldNamePattern = new("^[A-Za-z_][A-Za-z0-9_]*$");

    /// <summary>Why a field name cannot be used, or null if it can (<paramref name="except"/> is the field being renamed).</summary>
    public string? CheckFieldName(string? name, int? except = null)
    {
        if (string.IsNullOrWhiteSpace(name) || !FieldNamePattern.IsMatch(name))
            return "El nombre del campo solo admite letras, dígitos y guion bajo, y no puede empezar por un dígito.";
        if (name.Equals("now", StringComparison.OrdinalIgnoreCase) || name.Equals("counter", StringComparison.OrdinalIgnoreCase))
            return $"'{name}' está reservado.";
        for (var i = 0; i < FieldCount; i++)
            if (i != except && string.Equals(Text(Field(i), "name"), name, StringComparison.OrdinalIgnoreCase))
                return $"El campo '{name}' está duplicado.";
        return null;
    }

    /// <summary>Adds a field with the given name and returns its index; throws if the name is not valid.</summary>
    public int AddField(string name, string kind = "text")
    {
        if (CheckFieldName(name) is { } problem)
            throw new ArgumentException(problem, nameof(name));
        Checkpoint();
        FieldsArray.Add(new JsonObject { ["name"] = name, ["label"] = name, ["kind"] = kind });
        Raise();
        return FieldCount - 1;
    }

    public void RemoveField(int index)
    {
        Checkpoint();
        FieldsArray.RemoveAt(index);
        Raise();
    }

    /// <summary>Sets a property of a field (label, kind, required, default, choices as a list separated by commas).</summary>
    public void SetFieldProperty(int index, string name, string? raw, string? coalesceKey = null)
    {
        if (name == "name" && CheckFieldName(raw, index) is { } problem)
            throw new ArgumentException(problem, nameof(raw));
        JsonNode? node = string.IsNullOrEmpty(raw) ? null
            : name == "required" ? (raw.Trim().ToLowerInvariant() is "true" ? JsonValue.Create(true) : null)
            : name == "choices" ? new JsonArray([.. raw.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).Select(c => (JsonNode)c)])
            : raw;
        if (JsonEquals(Field(index)[name], node))
            return;
        Checkpoint(coalesceKey);
        if (node is null)
            Field(index).Remove(name);
        else
            Field(index)[name] = node;
        Raise();
    }

    public string GetFieldProperty(int index, string name) =>
        name == "choices" && Field(index)["choices"] is JsonArray choices
            ? string.Join(", ", choices.Select(c => c?.GetValue<string>()))
            : Display(Field(index)[name]);

    // ---- placeholders in use ---------------------------------------------------------------------

    /// <summary>The 1-based blocks that use <c>{{name}}</c> (in any property, or in <c>when</c>), plus 0 if the frame uses it.</summary>
    public IReadOnlyList<int> FieldUsage(string name)
    {
        var pattern = new Regex(@"\{\{\s*" + Regex.Escape(name) + @"\s*(?::[^}|]*)?(?:\|\s*[a-z]+\s*)?\}\}", RegexOptions.IgnoreCase);
        var used = new List<int>();
        if (_root["frame"] is JsonValue frame && frame.TryGetValue<string>(out var frameText) && pattern.IsMatch(frameText))
            used.Add(0);
        for (var i = 0; i < BlockCount; i++)
            if (Block(i).Any(p => p.Value is JsonValue v && v.TryGetValue<string>(out var s) && pattern.IsMatch(s)))
                used.Add(i + 1);
        return used;
    }

    // ---- raw JSON -------------------------------------------------------------------------------

    /// <summary>Replaces the whole template with edited JSON; returns the error message, or null on success.</summary>
    public string? TryReplace(string json)
    {
        JsonObject parsed;
        try
        {
            parsed = Parse(json);
        }
        catch (InvalidDataException ex)
        {
            return ex.Message;
        }
        if (ToJson(parsed) == Json)
            return null;
        Checkpoint();
        _root = parsed;
        Raise();
        return null;
    }

    // ---- undo and redo ------------------------------------------------------------------------------

    public void Undo()
    {
        if (_undo.Count == 0)
            return;
        _redo.Add(Json);
        _root = Parse(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        _lastKey = null;
        Raise();
    }

    public void Redo()
    {
        if (_redo.Count == 0)
            return;
        _undo.Add(Json);
        _root = Parse(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        _lastKey = null;
        Raise();
    }

    /// <summary>Saves the state before a change; continuous edits with the same key share one step.</summary>
    private void Checkpoint(string? key = null)
    {
        var now = _clock();
        var coalesce = key is not null && key == _lastKey && now - _lastEdit < CoalesceWindow && _undo.Count > 0;
        _lastKey = key;
        _lastEdit = now;
        if (coalesce)
            return;
        _undo.Add(Json);
        if (_undo.Count > MaxHistory)
            _undo.RemoveAt(0);
        _redo.Clear();
    }

    private void Raise() => Changed?.Invoke();

    // ---- helpers ------------------------------------------------------------------------------------

    private static string? Text(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    private static string Display(JsonNode? node) => node switch
    {
        null => "",
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
        JsonValue v when v.TryGetValue<double>(out var d) => d.ToString(CultureInfo.InvariantCulture),
        _ => node.ToJsonString(),
    };

    private static bool JsonEquals(JsonNode? a, JsonNode? b) =>
        (a is null && b is null) || (a is not null && b is not null && a.ToJsonString() == b.ToJsonString());

    private static string ToJson(JsonObject root) => root.ToJsonString(Canonical);

    private static JsonObject Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject ?? throw new InvalidDataException("La plantilla debe ser un objeto JSON.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"JSON no válido: {ex.Message}");
        }
    }

    private static bool HasComments(string json)
    {
        try
        {
            using var _ = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
            return false;   // strict parsing worked: no comments
        }
        catch (JsonException)
        {
            try
            {
                using var _ = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                return true;   // only parses when comments are skipped
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
