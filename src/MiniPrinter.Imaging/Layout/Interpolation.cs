using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MiniPrinter.Imaging;

/// <summary>
/// Substitutes <c>{{field}}</c>, <c>{{now[:format]}}</c>, <c>{{counter[:name]}}</c> and the
/// <c>| filter</c> suffixes (upper, lower, wifi, vcard, days, daysleft). Single pass: substituted
/// values are never interpolated again.
/// </summary>
internal sealed partial class Interpolator
{
    public static readonly string[] Filters = ["upper", "lower", "wifi", "vcard", "days", "daysleft"];

    public required IReadOnlyDictionary<string, string> Values { get; init; }
    public DateTime Now { get; init; } = DateTime.Now;
    public CultureInfo Culture { get; init; } = CultureInfo.CurrentCulture;
    public ICounterSource? Counters { get; init; }
    public bool ConsumeCounters { get; init; }

    private readonly Dictionary<string, long> _taken = [];

    [GeneratedRegex(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*(?::([^}|]*?))?\s*(?:\|\s*([a-z]+)\s*)?\}\}")]
    private static partial Regex Placeholder();

    public static IEnumerable<(string Name, string? Arg, string? Filter)> Find(string text) =>
        Placeholder().Matches(text).Select(m => (m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : null,
            m.Groups[3].Success ? m.Groups[3].Value : null));

    public string Apply(string text) =>
        text.Contains("{{") ? Placeholder().Replace(text, m => Resolve(m.Groups[1].Value,
            m.Groups[2].Success ? m.Groups[2].Value : null, m.Groups[3].Success ? m.Groups[3].Value : null)) : text;

    private string Resolve(string name, string? arg, string? filter)
    {
        string value;
        if (name.Equals("now", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                value = string.IsNullOrEmpty(arg) ? Now.ToString("g", Culture) : Now.ToString(arg, Culture);
            }
            catch (FormatException)
            {
                throw new TemplateException($"Formato de fecha no válido: '{arg}'.");
            }
        }
        else if (name.Equals("counter", StringComparison.OrdinalIgnoreCase))
        {
            var key = string.IsNullOrWhiteSpace(arg) ? "default" : arg.Trim();
            if (Counters is null)
                throw new TemplateException("Esta plantilla usa un contador, pero no hay ninguno disponible.");
            // One number per counter and label, however many times the placeholder appears.
            if (!_taken.TryGetValue(key, out var number))
                _taken[key] = number = ConsumeCounters ? Counters.Next(key) : Counters.Peek(key);
            value = number.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            value = Values.GetValueOrDefault(name) ?? "";
        }
        return filter is null ? value : ApplyFilter(value, filter);
    }

    private string ApplyFilter(string value, string filter) => filter switch
    {
        "upper" => value.ToUpper(Culture),
        "lower" => value.ToLower(Culture),
        "wifi" => Escape(value, "\\;,:\""),
        "vcard" => Escape(value, "\\;,").Replace("\r", "").Replace("\n", "\\n"),
        "days" => DaysLeft(value).ToString(CultureInfo.InvariantCulture),
        "daysleft" => DaysLeft(value) switch { 0 => "hoy", 1 => "1 día", var n => $"{n} días" },
        _ => throw new TemplateException($"Filtro desconocido: '{filter}' (disponibles: {string.Join(", ", Filters)})."),
    };

    private int DaysLeft(string value)
    {
        if (!DateTime.TryParse(value, Culture, DateTimeStyles.None, out var date)
            && !DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            throw new TemplateException($"Fecha no válida: '{value}' (por ejemplo 2026-12-31).");
        var days = (date.Date - Now.Date).Days;
        if (days < 0)
            throw new TemplateException("La fecha ya pasó.");
        return days;
    }

    private static string Escape(string value, string special)
    {
        var sb = new StringBuilder(value.Length + 4);
        foreach (var c in value)
        {
            if (special.Contains(c))
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
