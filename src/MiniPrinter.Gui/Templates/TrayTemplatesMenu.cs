using System.Text.Json;
using MiniPrinter.Control;

namespace MiniPrinter.Gui;

/// <summary>The saved values with a name ("favorites") of each template: <c>template → name → field → value</c>, per user.</summary>
public static class TemplateFavorites
{
    /// <summary>Reads the favorites file; a missing or damaged one gives none.</summary>
    public static Dictionary<string, Dictionary<string, Dictionary<string, string>>> Read(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(File.ReadAllText(path)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static void Write(string path, Dictionary<string, Dictionary<string, Dictionary<string, string>>> favorites)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(favorites));
    }
}

public enum TrayTemplateKind
{
    /// <summary>A favorite: choosing it prints at once.</summary>
    Favorite,

    /// <summary>A recent template: choosing it opens the panel on it, without printing.</summary>
    Recent,

    /// <summary>A group (the favorites of one template when there are many), with its entries as children.</summary>
    Group,

    /// <summary>A line that says there is nothing here (disabled).</summary>
    Empty,
}

/// <param name="Template">The name of the template.</param>
/// <param name="Favorite">The name of the favorite, for <see cref="TrayTemplateKind.Favorite"/>.</param>
public sealed record TrayTemplateItem(TrayTemplateKind Kind, string Text, string? Template = null, string? Favorite = null, IReadOnlyList<TrayTemplateItem>? Children = null);

/// <summary>What the "Plantillas" submenu of the tray icon contains. Pure, so that what is offered can be tested without a menu.</summary>
public static class TrayTemplatesMenu
{
    /// <summary>With more favorites than this they are grouped by template.</summary>
    public const int GroupAbove = 15;

    /// <summary>
    /// The favorites as "Template — name". A favorite of a template that no longer exists is left out. With many of them, one group
    /// per template.
    /// </summary>
    public static IReadOnlyList<TrayTemplateItem> Favorites(Dictionary<string, Dictionary<string, Dictionary<string, string>>> favorites, IReadOnlyList<TemplateDto> templates)
    {
        var titles = templates.ToDictionary(t => t.Name, t => t.Title, StringComparer.OrdinalIgnoreCase);
        var entries = favorites
            .Where(t => titles.ContainsKey(t.Key))
            .SelectMany(t => t.Value.Keys.Select(name => (Template: t.Key, Title: titles[t.Key], Name: name)))
            .OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (entries.Count == 0)
            return [new TrayTemplateItem(TrayTemplateKind.Empty, Strings.Get("Tray.NoFavorites"))];

        if (entries.Count <= GroupAbove)
            return [.. entries.Select(e => new TrayTemplateItem(TrayTemplateKind.Favorite, $"{e.Title} — {e.Name}", e.Template, e.Name))];

        return [.. entries.GroupBy(e => e.Template).Select(group => new TrayTemplateItem(TrayTemplateKind.Group, group.First().Title, group.Key, null,
            [.. group.Select(e => new TrayTemplateItem(TrayTemplateKind.Favorite, e.Name, e.Template, e.Name))]))];
    }

    /// <summary>The templates used last (a deleted one is left out), the most recent first.</summary>
    public static IReadOnlyList<TrayTemplateItem> Recent(IReadOnlyList<string> recent, IReadOnlyList<TemplateDto> templates)
    {
        var titles = templates.ToDictionary(t => t.Name, t => t.Title, StringComparer.OrdinalIgnoreCase);
        var items = recent.Where(titles.ContainsKey)
            .Take(TrayPreferences.MaxRecentTemplates)
            .Select(name => new TrayTemplateItem(TrayTemplateKind.Recent, titles[name], name))
            .ToList();
        return items.Count > 0 ? items : [new TrayTemplateItem(TrayTemplateKind.Empty, Strings.Get("Tray.NoRecent"))];
    }
}
