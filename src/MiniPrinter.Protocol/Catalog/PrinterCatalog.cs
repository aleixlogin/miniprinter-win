using System.Reflection;
using System.Text.Json;

namespace MiniPrinter.Protocol.Catalog;

/// <summary>A printer model: which advertised Bluetooth names map to which profile.</summary>
public sealed record PrinterModel(string Key, string Profile, IReadOnlyList<string> Prefixes, IReadOnlyList<string> Origin);

/// <summary>Result of resolving an advertised Bluetooth name against the catalog.</summary>
public sealed record CatalogMatch(PrinterModel Model, PrinterProfile Profile, string MatchedPrefix);

/// <summary>
/// Model and profile catalog for the "tiny" protocol family, ported from TiMini-Print
/// (Apache-2.0). Detection is by advertised-name prefix: the longest prefix wins, and an
/// exact-case match beats a case-insensitive one, so <c>X5h-…</c> and <c>X5H-…</c> resolve to
/// different profiles.
/// </summary>
public sealed class PrinterCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly Lazy<PrinterCatalog> DefaultCatalog = new(LoadEmbedded);

    private readonly Dictionary<string, PrinterProfile> _profiles;

    public PrinterCatalog(IEnumerable<PrinterProfile> profiles, IEnumerable<PrinterModel> models)
    {
        _profiles = profiles.ToDictionary(p => p.Key, StringComparer.Ordinal);
        Models = models.ToList();
        foreach (var model in Models)
        {
            if (!_profiles.ContainsKey(model.Profile))
                throw new InvalidDataException($"Model '{model.Key}' references unknown profile '{model.Profile}'.");
        }
    }

    /// <summary>The catalog shipped with this assembly.</summary>
    public static PrinterCatalog Default => DefaultCatalog.Value;

    public IReadOnlyList<PrinterModel> Models { get; }
    public IReadOnlyCollection<PrinterProfile> Profiles => _profiles.Values;

    public PrinterProfile? GetProfile(string key) => _profiles.GetValueOrDefault(key);

    public PrinterProfile RequireProfile(string key) =>
        GetProfile(key) ?? throw new KeyNotFoundException($"Unknown printer profile '{key}'.");

    /// <summary>
    /// Resolves an advertised name. Returns <c>null</c> when nothing matches or when the best
    /// match is ambiguous (two models with an equally specific prefix).
    /// </summary>
    public CatalogMatch? Detect(string? advertisedName)
    {
        var name = advertisedName?.Trim();
        if (string.IsNullOrEmpty(name))
            return null;

        return Best(name, StringComparison.Ordinal) ?? Best(name, StringComparison.OrdinalIgnoreCase);
    }

    private CatalogMatch? Best(string name, StringComparison comparison)
    {
        var candidates = Models
            .SelectMany(m => m.Prefixes.Where(p => name.StartsWith(p, comparison)).Select(p => (Model: m, Prefix: p)))
            .GroupBy(c => c.Prefix.Length)
            .OrderByDescending(g => g.Key)
            .FirstOrDefault();
        if (candidates is null)
            return null;

        var models = candidates.Select(c => c.Model).Distinct().ToList();
        if (models.Count != 1)
            return null;

        var winner = candidates.First();
        return new CatalogMatch(winner.Model, _profiles[winner.Model.Profile], winner.Prefix);
    }

    private static PrinterCatalog LoadEmbedded()
    {
        var assembly = typeof(PrinterCatalog).Assembly;
        var profiles = Read<ProfilesFile>(assembly, "profiles.json").Profiles;
        var models = Read<ModelsFile>(assembly, "models.json").Models;
        return new PrinterCatalog(profiles, models);
    }

    private static T Read<T>(Assembly assembly, string fileName)
    {
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Catalog." + fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidDataException($"Empty catalog resource '{fileName}'.");
    }

    private sealed record ProfilesFile(List<PrinterProfile> Profiles);

    private sealed record ModelsFile(List<PrinterModel> Models);
}
