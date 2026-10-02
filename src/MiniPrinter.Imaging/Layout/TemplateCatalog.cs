using System.Reflection;
using System.Text;
using MiniPrinter.Protocol;

namespace MiniPrinter.Imaging;

/// <summary>
/// Built-in templates (embedded JSON) plus the user's, stored in
/// <c>%ProgramData%\MiniPrinter\templates\*.json</c> so the service, the tray and the CLI all see
/// them (design.md D4). A user template named like a built-in replaces it; deleting it restores it.
/// Changes on disk are picked up on the next access.
/// </summary>
public sealed class TemplateCatalog
{
    public const string BuiltIn = "builtin";
    public const string User = "user";
    public const string Override = "override";

    /// <summary>Order of the built-ins in listings (the original five first).</summary>
    private static readonly string[] BuiltInOrder =
        ["qr", "barcode", "todo", "label", "sticker", "shopping", "wifi", "contact", "cable", "receipt", "bookmark", "countdown"];

    private static readonly Lazy<IReadOnlyDictionary<string, (TemplateLayout Layout, string Json)>> BuiltIns = new(LoadBuiltIns);

    /// <summary>
    /// %ProgramData%\MiniPrinter (overridable with MINIPRINTER_DATA, like the service's data folder):
    /// the templates live in its <c>templates</c> folder and the counters in <c>counters.json</c>.
    /// </summary>
    public static string DataDirectory { get; } = Environment.GetEnvironmentVariable("MINIPRINTER_DATA")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MiniPrinter");

    public static string DefaultDirectory { get; } = Path.Combine(DataDirectory, "templates");

    /// <summary>The catalog of this machine (default folder, default counters file).</summary>
    public static TemplateCatalog Default { get; } = ForDataDirectory(DataDirectory);

    public static TemplateCatalog ForDataDirectory(string dataDirectory) =>
        new(Path.Combine(dataDirectory, "templates"), new FileCounterStore(Path.Combine(dataDirectory, "counters.json")));

    private readonly object _gate = new();
    private string _signature = "\0";
    private Dictionary<string, (TemplateLayout Layout, string Source, string Json)> _templates = [];
    private List<string> _errors = [];

    public TemplateCatalog(string userDirectory, ICounterSource? counters = null)
    {
        UserDirectory = userDirectory;
        Counters = counters;
    }

    public string UserDirectory { get; }
    public ICounterSource? Counters { get; }

    /// <summary>Why each skipped user template was rejected ("file: reason").</summary>
    public IReadOnlyList<string> Errors
    {
        get
        {
            Refresh();
            return _errors;
        }
    }

    public IReadOnlyList<TemplateDefinition> Definitions
    {
        get
        {
            Refresh();
            return [.. _templates.Values.Select(t => t.Layout.ToDefinition(t.Source))];
        }
    }

    public TemplateLayout? FindLayout(string name)
    {
        Refresh();
        return _templates.TryGetValue(name, out var t) ? t.Layout : null;
    }

    public TemplateDefinition? Find(string name)
    {
        Refresh();
        return _templates.TryGetValue(name, out var t) ? t.Layout.ToDefinition(t.Source) : null;
    }

    /// <summary>The JSON of a template (the user's file, or the embedded built-in).</summary>
    public string? GetJson(string name)
    {
        Refresh();
        return _templates.TryGetValue(name, out var t) ? t.Json : null;
    }

    public string AssetsDirectory(string name) => Path.Combine(UserDirectory, "assets", name);

    // ---- rendering -----------------------------------------------------------------------------

    private TemplateLayout Require(string name) => FindLayout(name) ?? throw new TemplateException(
        $"Plantilla desconocida: '{name}'. Disponibles: {string.Join(", ", Definitions.Select(d => d.Name))}.");

    /// <summary>Renders a template; <paramref name="options"/>.Counters/AssetsDir default to this catalog's.</summary>
    public MonoBitmap Render(string name, IReadOnlyDictionary<string, string> fields, RenderOptions? options = null)
    {
        var layout = Require(name);
        options ??= new RenderOptions();
        options = options with
        {
            Counters = options.Counters ?? Counters,
            AssetsDir = options.AssetsDir ?? AssetsDirectory(layout.Name),
        };
        return TemplateEngine.Render(layout, fields, options);
    }

    public const int MaxCopies = 50;
    public const int MaxRows = 200;

    /// <summary>
    /// Renders what a print request produces: one label (or one per row, each row overriding the base
    /// fields), every label repeated <paramref name="copies"/> times. Counters advance once per distinct
    /// label, and only if every row renders: a bad row throws "Fila N: …" and consumes nothing.
    /// </summary>
    public IReadOnlyList<MonoBitmap> RenderBatch(string name, IReadOnlyDictionary<string, string> fields,
        IReadOnlyList<IReadOnlyDictionary<string, string>>? rows, int copies, RenderOptions? options = null)
    {
        if (copies is < 1 or > MaxCopies)
            throw new TemplateException($"copies debe estar entre 1 y {MaxCopies}.");
        if (rows is { Count: 0 })
            throw new TemplateException("La lista de filas está vacía.");
        if (rows is { Count: > MaxRows })
            throw new TemplateException($"Hay {rows.Count} filas; el máximo es {MaxRows}.");

        options ??= new RenderOptions();
        var sets = rows is null
            ? [fields]
            : rows.Select(row => (IReadOnlyDictionary<string, string>)MergeRow(fields, row)).ToList();

        // Validate everything first (previews do not consume numbers), then print-render.
        var preview = options with { ConsumeCounters = false };
        for (var i = 0; i < sets.Count; i++)
        {
            try
            {
                Render(name, sets[i], preview);
            }
            catch (TemplateException ex) when (rows is not null)
            {
                throw new TemplateException($"Fila {i + 1}: {ex.Message}");
            }
        }

        var print = options with { ConsumeCounters = true };
        var pages = new List<MonoBitmap>();
        foreach (var set in sets)
        {
            var label = Render(name, set, print);
            for (var c = 0; c < copies; c++)
                pages.Add(label);
        }
        return pages;
    }

    private static Dictionary<string, string> MergeRow(IReadOnlyDictionary<string, string> baseFields, IReadOnlyDictionary<string, string> row)
    {
        var merged = new Dictionary<string, string>(baseFields, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in row)
            merged[key] = value;
        return merged;
    }

    // ---- management ----------------------------------------------------------------------------

    /// <summary>Validates a template without saving it (images are looked up in the template's assets).</summary>
    public TemplateLayout Validate(string json, string? expectedName = null)
    {
        var layout = TemplateLayout.Parse(json, file => AssetExists(expectedName ?? ExtractName(json), file));
        if (expectedName is not null && !layout.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            throw new TemplateException($"El nombre de la plantilla ('{layout.Name}') no coincide con '{expectedName}'.");
        return layout;
    }

    /// <summary>Validates and saves a user template (atomically).</summary>
    public TemplateLayout Save(string name, string json)
    {
        if (!TemplateLayout.IsValidName(name))
            throw new TemplateException($"Nombre de plantilla no válido: '{name}' (letras, dígitos, guion y guion bajo; máximo 40).");
        var layout = Validate(json, name);
        Directory.CreateDirectory(UserDirectory);
        var path = FilePath(layout.Name);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
        Refresh();
        return layout;
    }

    /// <summary>Deletes a user template and its images. Built-ins without a user version cannot be deleted.</summary>
    public void Delete(string name)
    {
        if (!TemplateLayout.IsValidName(name))
            throw new TemplateException($"Nombre de plantilla no válido: '{name}'.");
        var path = FilePath(name);
        if (!File.Exists(path))
            throw new TemplateException(BuiltIns.Value.ContainsKey(name)
                ? $"'{name}' es una plantilla integrada y no se puede borrar."
                : $"No existe la plantilla '{name}'.");
        File.Delete(path);
        var assets = AssetsDirectory(name);
        if (Directory.Exists(assets))
            Directory.Delete(assets, recursive: true);
        Refresh();
    }

    public void SaveAsset(string template, string file, byte[] content)
    {
        if (!TemplateLayout.IsValidName(template))
            throw new TemplateException($"Nombre de plantilla no válido: '{template}'.");
        if (!TemplateAssets.IsValidFileName(file))
            throw new TemplateException($"Nombre de imagen no válido: '{file}' (PNG o JPEG, sin rutas).");
        if (content.Length > TemplateAssets.MaxBytes)
            throw new TemplateException($"La imagen supera el máximo de {TemplateAssets.MaxBytes / (1024 * 1024)} MB.");
        if (!IsPngOrJpeg(content))
            throw new TemplateException("Formato de imagen no admitido (PNG o JPEG).");
        var dir = AssetsDirectory(template);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, file), content);
    }

    public void DeleteAsset(string template, string file)
    {
        if (!TemplateLayout.IsValidName(template) || !TemplateAssets.IsValidFileName(file))
            throw new TemplateException("Nombre de imagen no válido.");
        var path = Path.Combine(AssetsDirectory(template), file);
        if (!File.Exists(path))
            throw new TemplateException($"No existe la imagen '{file}'.");
        File.Delete(path);
    }

    private bool AssetExists(string template, string file) =>
        TemplateLayout.IsValidName(template) && TemplateAssets.IsValidFileName(file) && File.Exists(Path.Combine(AssetsDirectory(template), file));

    private static bool IsPngOrJpeg(byte[] c) =>
        c.Length > 8 && ((c[0] == 0x89 && c[1] == 0x50 && c[2] == 0x4E && c[3] == 0x47) || (c[0] == 0xFF && c[1] == 0xD8));

    private string FilePath(string name) => Path.Combine(UserDirectory, name + ".json");

    private static string ExtractName(string json)
    {
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(json)?["name"]?.GetValue<string>() ?? "";
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return "";
        }
    }

    // ---- loading -------------------------------------------------------------------------------

    private void Refresh()
    {
        var files = Directory.Exists(UserDirectory)
            ? Directory.GetFiles(UserDirectory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        var signature = string.Join('|', files.Select(f =>
        {
            var info = new FileInfo(f);
            return $"{info.Name}:{info.LastWriteTimeUtc.Ticks}:{info.Length}";
        }));
        lock (_gate)
        {
            if (signature == _signature)
                return;
            var templates = new Dictionary<string, (TemplateLayout, string, string)>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in BuiltInOrder)
                if (BuiltIns.Value.TryGetValue(name, out var b))
                    templates[name] = (b.Layout, BuiltIn, b.Json);
            var errors = new List<string>();
            foreach (var file in files)
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                try
                {
                    var json = File.ReadAllText(file);
                    var layout = TemplateLayout.Parse(json, asset => AssetExists(stem, asset));
                    if (!layout.Name.Equals(stem, StringComparison.OrdinalIgnoreCase))
                        throw new TemplateException($"el nombre '{layout.Name}' no coincide con el del archivo.");
                    templates[layout.Name] = (layout, BuiltIns.Value.ContainsKey(layout.Name) ? Override : User, json);
                }
                catch (Exception ex) when (ex is TemplateException or IOException)
                {
                    errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }
            _templates = templates;
            _errors = errors;
            _signature = signature;
        }
    }

    private static IReadOnlyDictionary<string, (TemplateLayout, string)> LoadBuiltIns()
    {
        var assembly = typeof(TemplateCatalog).Assembly;
        var result = new Dictionary<string, (TemplateLayout, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in assembly.GetManifestResourceNames().Where(r => r.Contains(".Templates.") && r.EndsWith(".json")))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var json = reader.ReadToEnd();
            var layout = TemplateLayout.Parse(json);
            result[layout.Name] = (layout, json);
        }
        return result;
    }
}
