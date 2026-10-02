using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MiniPrinter.Imaging;

namespace MiniPrinter.Service;

/// <summary>
/// Editor drafts (design.md D6): a folder per draft under <c>%ProgramData%\MiniPrinter\drafts\</c> holding the
/// images of a template that is not saved yet, so cancelling never touches a saved template. Identifiers are
/// generated here and never used as paths; old drafts are removed after 24 hours.
/// </summary>
public sealed partial class DraftStore
{
    public const int MaxDrafts = 20;
    public const long MaxDraftBytes = 10 * 1024 * 1024;
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private readonly object _gate = new();
    private readonly Func<DateTime> _clock;

    public DraftStore(ServicePaths paths, Func<DateTime>? clock = null)
        : this(Path.Combine(paths.DataDirectory, "drafts"), clock)
    {
    }

    public DraftStore(string root, Func<DateTime>? clock = null)
    {
        Root = root;
        _clock = clock ?? (() => DateTime.UtcNow);
        Directory.CreateDirectory(Root);
        Prune();
    }

    public string Root { get; }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex IdPattern();

    /// <summary>Creates a draft and returns its identifier.</summary>
    public string Create()
    {
        lock (_gate)
        {
            Prune();
            if (Directory.Exists(Root) && Directory.GetDirectories(Root).Length >= MaxDrafts)
                throw new PrintRequestException($"Hay demasiados borradores abiertos (máximo {MaxDrafts}); cierra algún editor.");
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var dir = Path.Combine(Root, id);
            Directory.CreateDirectory(dir);
            Directory.SetLastWriteTimeUtc(dir, _clock());
            return id;
        }
    }

    /// <summary>The folder of a draft, or null when no identifier was given. Throws for an unknown or malformed one.</summary>
    public string? Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        if (!IdPattern().IsMatch(id))
            throw new PrintRequestException("Identificador de borrador no válido.");
        var dir = Path.Combine(Root, id);
        if (!Directory.Exists(dir))
            throw new PrintRequestException("El borrador no existe o ha caducado.");
        Directory.SetLastWriteTimeUtc(dir, _clock());   // activity keeps it alive
        return dir;
    }

    public void SaveAsset(string id, string file, byte[] content)
    {
        var dir = Resolve(id) ?? throw new PrintRequestException("Falta el identificador del borrador.");
        if (!TemplateAssets.IsValidFileName(file))
            throw new PrintRequestException($"Nombre de imagen no válido: '{file}' (PNG o JPEG, sin rutas).");
        if (content.Length > TemplateAssets.MaxBytes)
            throw new PrintRequestException($"La imagen supera el máximo de {TemplateAssets.MaxBytes / (1024 * 1024)} MB.");
        if (!TemplateAssets.LooksLikeImage(content))
            throw new PrintRequestException("Formato de imagen no admitido (PNG o JPEG).");
        var others = Directory.GetFiles(dir).Where(f => !Path.GetFileName(f).Equals(file, StringComparison.OrdinalIgnoreCase))
            .Sum(f => new FileInfo(f).Length);
        if (others + content.Length > MaxDraftBytes)
            throw new PrintRequestException($"El borrador supera el máximo de {MaxDraftBytes / (1024 * 1024)} MB de imágenes.");
        File.WriteAllBytes(Path.Combine(dir, file), content);
    }

    public void DeleteAsset(string id, string file)
    {
        var dir = Resolve(id) ?? throw new PrintRequestException("Falta el identificador del borrador.");
        if (!TemplateAssets.IsValidFileName(file))
            throw new PrintRequestException("Nombre de imagen no válido.");
        var path = Path.Combine(dir, file);
        if (!File.Exists(path))
            throw new PrintRequestException($"No existe la imagen '{file}' en el borrador.");
        File.Delete(path);
    }

    /// <summary>Removes a draft (cancelling the editor, or after saving). Unknown identifiers are ignored.</summary>
    public void Discard(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || !IdPattern().IsMatch(id))
            return;
        var dir = Path.Combine(Root, id);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    /// <summary>Deletes the drafts untouched for more than <see cref="MaxAge"/>.</summary>
    public void Prune()
    {
        if (!Directory.Exists(Root))
            return;
        var limit = _clock() - MaxAge;
        foreach (var dir in Directory.GetDirectories(Root))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(dir) < limit)
                    Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: it is tried again next time.
            }
        }
    }
}
