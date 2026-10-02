using MiniPrinter.Protocol;

namespace MiniPrinter.Imaging;

public enum TemplateFieldKind
{
    Text,
    MultilineText,
    Choice,
    /// <summary>Image bytes, Base64-encoded when sent as text (API) or a file path (CLI).</summary>
    Image,
    Number,
    Boolean,
}

public sealed record TemplateField(string Name, string Label, TemplateFieldKind Kind, bool Required, IReadOnlyList<string>? Choices = null, string? Default = null);

/// <param name="Source">"builtin", "user" (the user's own) or "override" (a user template replacing a built-in).</param>
public sealed record TemplateDefinition(string Name, string Title, string Description, IReadOnlyList<TemplateField> Fields, bool IsImage = false, string Source = "builtin");

/// <summary>A template request could not be rendered (missing field, invalid code, too long…).</summary>
public sealed class TemplateException(string message) : Exception(message);

/// <summary>
/// Entry point to the template catalog of this machine (built-in JSON templates plus the user's).
/// The rendering lives in the layout engine (<c>Layout/</c>); this class keeps the original API.
/// </summary>
public static class TemplateRenderer
{
    public const int MinQrModule = 4;
    public const int MinBarWidth = 2;

    public static IReadOnlyList<TemplateDefinition> Definitions => TemplateCatalog.Default.Definitions;

    public static TemplateDefinition? Find(string name) => TemplateCatalog.Default.Find(name);

    /// <summary>
    /// Renders a template of the default catalog. <paramref name="consumeCounters"/> is true when the
    /// result is going to be printed (so <c>{{counter}}</c> advances), false for previews.
    /// </summary>
    public static MonoBitmap Render(string name, IReadOnlyDictionary<string, string> fields, int width = 384, bool consumeCounters = false) =>
        TemplateCatalog.Default.Render(name, fields, new RenderOptions { Width = width, ConsumeCounters = consumeCounters });

    public static int Ean13CheckDigit(string twelveDigits)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++)
            sum += (twelveDigits[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - sum % 10) % 10;
    }

    public static int UpcaCheckDigit(string elevenDigits)
    {
        var sum = 0;
        for (var i = 0; i < 11; i++)
            sum += (elevenDigits[i] - '0') * (i % 2 == 0 ? 3 : 1);
        return (10 - sum % 10) % 10;
    }

    internal static bool Digits(string text, int min, int max) =>
        text.Length >= min && text.Length <= max && text.All(char.IsAsciiDigit);

    /// <summary>Stacks blocks vertically with <paramref name="gap"/> blank rows between them.</summary>
    public static MonoBitmap Stack(IReadOnlyList<MonoBitmap> blocks, int gap = 8) => LayoutBlocks.Stack(blocks, gap);
}
