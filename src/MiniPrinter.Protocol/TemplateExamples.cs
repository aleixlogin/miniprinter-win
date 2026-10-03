using System.Globalization;

namespace MiniPrinter.Protocol;

/// <summary>
/// What a block property accepts when it takes its value from a template field (<c>"height": "{{alto}}"</c>): a number in a
/// range or one of some words. The example value of the field has to pass this, or the preview fails for a reason that is not
/// the user's.
/// </summary>
/// <param name="Default">What the property uses when it is empty (clamped into the range when it is not in it, like 0 = automatic).</param>
public sealed record ExampleRule(double Min, double Max, double Default, IReadOnlyList<string>? Values = null, bool IsDate = false)
{
    /// <summary>A field that goes through the <c>days</c> or <c>daysleft</c> filter: it has to be a date.</summary>
    public static readonly ExampleRule Date = new(0, 0, 0, null, IsDate: true);

    public bool Accepts(string value)
    {
        value = value.Trim();
        if (IsDate)
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        if (Values is not null)
            return Values.Contains(value, StringComparer.OrdinalIgnoreCase);
        return double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= Min && number <= Max;
    }
}

/// <summary>Example values for the fields of a template (previews of the editor and thumbnails of the gallery).</summary>
public static class TemplateExamples
{
    /// <summary>
    /// The value to use for a field when the user has typed none. A default written by the user is kept as it is, even if it does not
    /// pass the rules, so that the preview shows the problem it will cause when printing. Without one, the value is made to pass
    /// every property the field feeds: a number inside all their ranges (the nearest to what the property uses by itself) or a word
    /// all of them accept.
    /// </summary>
    /// <param name="fallback">The text of a plain text field with no rule (a label or a sample sentence).</param>
    public static string Choose(string kind, string? fieldDefault, string? firstChoice, string fallback, IReadOnlyList<ExampleRule> rules)
    {
        if (!string.IsNullOrEmpty(fieldDefault))
            return fieldDefault;

        if (rules.Any(r => r.IsDate))
            return DateTime.Today.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var words = rules.Where(r => r.Values is not null).Select(r => r.Values!).ToList();
        if (words.Count > 0)
        {
            var common = words[0].Where(w => rules.All(r => r.Values is null || r.Accepts(w))).ToList();
            if (common.Count > 0)
                return firstChoice is not null && common.Contains(firstChoice, StringComparer.OrdinalIgnoreCase) ? firstChoice : common[0];
            return words[0][0];
        }

        var ranges = rules.Where(r => r.Values is null && !r.IsDate).ToList();
        if (ranges.Count > 0)
        {
            var low = ranges.Max(r => r.Min);
            var high = ranges.Min(r => r.Max);
            var wanted = ranges[0].Default;
            var value = low <= high ? Math.Clamp(wanted, low, high) : wanted;
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        return kind switch
        {
            "number" => "1",
            "choice" => firstChoice ?? "",
            "boolean" or "image" => "",
            _ => fallback,
        };
    }
}
