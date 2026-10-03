using MiniPrinter.Protocol;

namespace MiniPrinter.Protocol.Tests;

/// <summary>The example of a template field passes the validation of the property it feeds.</summary>
public class TemplateExamplesTests
{
    private static readonly ExampleRule Height = new(4, 50, 12);
    private static readonly ExampleRule TextSize = new(6, 48, 10);
    private static readonly ExampleRule QrModule = new(1, 40, 0); // 0 means automatic and is outside the range
    private static readonly ExampleRule Marker = new(0, 0, 0, ["box", "bullet", "number", "none"]);

    [Fact]
    public void A_number_field_gets_the_value_the_property_uses_by_itself()
    {
        Assert.Equal("12", TemplateExamples.Choose("number", null, null, "x", [Height]));
        Assert.Equal("10", TemplateExamples.Choose("number", "", null, "x", [TextSize]));
    }

    [Fact]
    public void A_default_outside_the_range_is_pulled_into_it()
    {
        Assert.Equal("1", TemplateExamples.Choose("number", null, null, "x", [QrModule]));
    }

    [Fact]
    public void Several_properties_share_the_example_when_their_ranges_overlap()
    {
        // 4–50 and 6–48 overlap in 6–48: the wanted 12 is inside.
        Assert.Equal("12", TemplateExamples.Choose("number", null, null, "x", [Height, TextSize]));
        // 4–50 and 1–8: only 4–8 is allowed, so 12 is pulled to 8.
        Assert.Equal("8", TemplateExamples.Choose("number", null, null, "x", [Height, new ExampleRule(1, 8, 2)]));
    }

    [Fact]
    public void Every_example_passes_its_own_rules()
    {
        foreach (var rule in new[] { Height, TextSize, QrModule, new ExampleRule(0.5, 100, 2), new ExampleRule(0, 50, 6) })
            Assert.True(rule.Accepts(TemplateExamples.Choose("number", null, null, "x", [rule])), $"{rule}");
    }

    [Fact]
    public void A_field_that_feeds_a_choice_gets_one_of_its_words()
    {
        Assert.Equal("box", TemplateExamples.Choose("text", null, null, "Texto", [Marker]));
        Assert.Equal("bullet", TemplateExamples.Choose("choice", null, "bullet", "x", [Marker]));
        // the first choice of the field is not accepted by the property: another word that is
        Assert.Equal("box", TemplateExamples.Choose("choice", null, "rojo", "x", [Marker]));
    }

    [Fact]
    public void A_default_written_by_the_user_is_kept_so_that_its_problem_shows()
    {
        Assert.Equal("100", TemplateExamples.Choose("number", "100", null, "x", [Height]));
    }

    [Theory]
    [InlineData("number", null, "1")]
    [InlineData("choice", "rojo", "rojo")]
    [InlineData("boolean", null, "")]
    [InlineData("image", null, "")]
    [InlineData("text", null, "Texto de ejemplo")]
    public void Without_rules_the_example_follows_the_kind(string kind, string? choice, string expected) =>
        Assert.Equal(expected, TemplateExamples.Choose(kind, null, choice, "Texto de ejemplo", []));

    [Fact]
    public void A_number_with_a_comma_is_read_as_a_number()
    {
        Assert.True(Height.Accepts("12,5"));
        Assert.False(Height.Accepts("3"));
        Assert.False(Height.Accepts("alto"));
    }
}

public class TemplateExamplesDateTests
{
    [Fact]
    public void A_field_through_a_date_filter_gets_a_date()
    {
        var example = TemplateExamples.Choose("text", null, null, "Fecha (aaaa-mm-dd)", [ExampleRule.Date]);
        Assert.True(ExampleRule.Date.Accepts(example), example);
        Assert.False(ExampleRule.Date.Accepts("Fecha (aaaa-mm-dd)"));
    }
}
