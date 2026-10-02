using System.Text;

namespace MiniPrinter.Control;

/// <summary>
/// A paper size offered to Windows through IPP (design.md D1). Presets are the factory sizes: they can be
/// switched on and off but not edited or deleted.
/// </summary>
/// <param name="Id">Stable identifier: <c>48x210</c> for presets, <c>c57x150</c> for the user's own.</param>
/// <param name="Name">Label shown in the settings (the IPP key is derived from the measures).</param>
public sealed record PaperSizeSetting(string Id, string Name, int WidthMm, int LengthMm, bool Enabled = true, bool Preset = false)
{
    /// <summary>The IPP media keyword, derived from the measures so what Windows shows is the size itself.</summary>
    public string IppName => PaperCatalog.IppName(WidthMm, LengthMm);

    /// <summary>
    /// Left/right margin announced for this size, in millimetres: half of what exceeds the head width for a
    /// user size wider than 48 mm, so Windows lays out at 48 mm inside the page; 0 otherwise.
    /// </summary>
    public double SideMarginMm => !Preset && WidthMm > PaperCatalog.HeadWidthMm ? (WidthMm - PaperCatalog.HeadWidthMm) / 2.0 : 0;
}

/// <summary>Factory sizes, rules and normalization of the paper sizes setting (spec paper-sizes).</summary>
public static class PaperCatalog
{
    public const int HeadWidthMm = 48;
    public const int MinWidthMm = 30;
    public const int MaxWidthMm = 57;
    public const int MinLengthMm = 10;
    public const int MaxLengthMm = 1000;
    public const int MaxSizes = 30;
    public const int MaxNameLength = 40;
    public const string DefaultPresetId = "48x210";

    /// <summary>The seven sizes the service always offered, in their original order.</summary>
    public static IReadOnlyList<PaperSizeSetting> Presets { get; } =
    [
        new("48x210", "48 × 210 mm", 48, 210, true, true),
        new("48x100", "48 × 100 mm", 48, 100, true, true),
        new("48x50", "48 × 50 mm", 48, 50, true, true),
        new("48x297", "48 × 297 mm", 48, 297, true, true),
        new("48x1000", "Rollo 48 × 1000 mm", 48, 1000, true, true),
        new("80x297", "80 × 297 mm (virtual)", 80, 297, true, true),
        new("80x100", "80 × 100 mm (virtual)", 80, 100, true, true),
    ];

    public static string IppName(int widthMm, int lengthMm) => $"om_x5h-{widthMm}x{lengthMm}mm_{widthMm}x{lengthMm}mm";

    public static string CustomId(int widthMm, int lengthMm) => $"c{widthMm}x{lengthMm}";

    /// <summary>The sizes in force: the saved list, or the seven presets when nothing was saved.</summary>
    public static IReadOnlyList<PaperSizeSetting> All(ServiceSettings settings) =>
        settings.PaperSizes is { Count: > 0 } list ? list : Presets;

    /// <summary>The enabled sizes, with the default one first (this is what is announced over IPP).</summary>
    public static IReadOnlyList<PaperSizeSetting> Effective(ServiceSettings settings)
    {
        var enabled = All(settings).Where(p => p.Enabled).ToList();
        if (enabled.Count == 0)
            enabled = [Presets[0]];
        var defaultId = DefaultId(settings);
        var first = enabled.FindIndex(p => p.Id == defaultId);
        if (first > 0)
        {
            var chosen = enabled[first];
            enabled.RemoveAt(first);
            enabled.Insert(0, chosen);
        }
        return enabled;
    }

    /// <summary>The default size: the chosen one if it is enabled, otherwise the first enabled.</summary>
    public static string DefaultId(ServiceSettings settings)
    {
        var enabled = All(settings).Where(p => p.Enabled).ToList();
        if (enabled.Count == 0)
            return DefaultPresetId;
        return enabled.Any(p => p.Id == settings.DefaultPaperId) ? settings.DefaultPaperId! : enabled[0].Id;
    }

    /// <summary>True when two settings announce the same sizes in the same order with the same default.</summary>
    public static bool SameEffective(ServiceSettings a, ServiceSettings b) =>
        Effective(a).SequenceEqual(Effective(b));

    public static bool SameList(IReadOnlyList<PaperSizeSetting>? a, IReadOnlyList<PaperSizeSetting>? b) =>
        (a is null && b is null) || (a is not null && b is not null && a.SequenceEqual(b));

    /// <summary>
    /// Corrects a list read from a file or the API: presets keep their factory measures, own sizes are clamped
    /// to the allowed ranges, names are cleaned, repeated measures are dropped, and every preset is present.
    /// A null or empty list stays null (= the factory sizes).
    /// </summary>
    public static IReadOnlyList<PaperSizeSetting>? Normalize(IReadOnlyList<PaperSizeSetting>? list)
    {
        if (list is not { Count: > 0 })
            return null;

        var result = new List<PaperSizeSetting>();
        var measures = new HashSet<(int, int)>();
        foreach (var item in list)
        {
            if (result.Count >= MaxSizes)
                break;
            var preset = Presets.FirstOrDefault(p => p.Id == item.Id);
            PaperSizeSetting fixedItem;
            if (preset is not null)
                fixedItem = preset with { Enabled = item.Enabled };
            else
            {
                var w = Math.Clamp(item.WidthMm, MinWidthMm, MaxWidthMm);
                var l = Math.Clamp(item.LengthMm, MinLengthMm, MaxLengthMm);
                fixedItem = new PaperSizeSetting(CustomId(w, l), CleanName(item.Name, w, l), w, l, item.Enabled, Preset: false);
            }
            if (!measures.Add((fixedItem.WidthMm, fixedItem.LengthMm)) || result.Any(r => r.Id == fixedItem.Id))
                continue;
            result.Add(fixedItem);
        }
        // Every preset is always in the list (a missing one is simply not offered).
        foreach (var preset in Presets)
            if (result.All(r => r.Id != preset.Id) && measures.Add((preset.WidthMm, preset.LengthMm)) && result.Count < MaxSizes)
                result.Add(preset with { Enabled = false });
        if (result.All(r => !r.Enabled))
            result[result.FindIndex(r => r.Id == DefaultPresetId) is var i and >= 0 ? i : 0] = Presets[0];
        return result;
    }

    /// <summary>The first problem of a list a user is about to save (the API rejects instead of correcting), or null.</summary>
    public static string? Problem(IReadOnlyList<PaperSizeSetting>? list)
    {
        if (list is not { Count: > 0 })
            return null;
        if (list.Count > MaxSizes)
            return $"Como máximo hay {MaxSizes} tamaños de papel.";
        var measures = new HashSet<(int, int)>();
        foreach (var item in list)
        {
            if (Presets.Any(p => p.Id == item.Id))
            {
                if (!measures.Add((Presets.First(p => p.Id == item.Id).WidthMm, Presets.First(p => p.Id == item.Id).LengthMm)))
                    return "Hay tamaños con las mismas medidas.";
                continue;
            }
            if (item.WidthMm is < MinWidthMm or > MaxWidthMm)
                return $"El ancho de «{item.Name}» debe estar entre {MinWidthMm} y {MaxWidthMm} mm (la impresora admite como máximo {MaxWidthMm} mm).";
            if (item.LengthMm is < MinLengthMm or > MaxLengthMm)
                return $"El largo de «{item.Name}» debe estar entre {MinLengthMm} y {MaxLengthMm} mm.";
            if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Trim().Length > MaxNameLength || item.Name.Any(char.IsControl))
                return $"El nombre del tamaño debe tener entre 1 y {MaxNameLength} caracteres, sin caracteres de control.";
            if (!measures.Add((item.WidthMm, item.LengthMm)))
                return $"Ya existe un tamaño de {item.WidthMm} × {item.LengthMm} mm.";
        }
        if (list.All(p => !p.Enabled))
            return "Debe haber al menos un tamaño de papel activo.";
        return null;
    }

    private static string CleanName(string? name, int width, int length)
    {
        var clean = new StringBuilder();
        foreach (var c in name ?? "")
            if (!char.IsControl(c))
                clean.Append(c);
        var text = clean.ToString().Trim();
        if (text.Length == 0)
            return $"{width} × {length} mm";
        return text.Length > MaxNameLength ? text[..MaxNameLength].TrimEnd() : text;
    }
}
