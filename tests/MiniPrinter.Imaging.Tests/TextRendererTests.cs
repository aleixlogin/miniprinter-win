using MiniPrinter.Protocol;

namespace MiniPrinter.Imaging.Tests;

public class TextRendererTests
{
    private static int InkRows(MonoBitmap b) => Enumerable.Range(0, b.Height).Count(y => !b.IsRowBlank(y));

    private static (int First, int Last) InkColumns(MonoBitmap b)
    {
        var columns = Enumerable.Range(0, b.Width).Where(x => Enumerable.Range(0, b.Height).Any(y => b[x, y])).ToList();
        return (columns.First(), columns.Last());
    }

    [Fact]
    public void Renders_at_head_width_with_ink()
    {
        var bitmap = TextRenderer.Render("Hola");
        Assert.Equal(384, bitmap.Width);
        Assert.True(InkRows(bitmap) > 5);
    }

    [Fact]
    public void Long_line_wraps_inside_the_margins()
    {
        var shortText = TextRenderer.Render("Los animales");
        var longText = TextRenderer.Render(string.Join(' ', Enumerable.Repeat("Los animales de dos en dos", 4)));

        Assert.True(longText.Height > shortText.Height * 2.5, $"{longText.Height} vs {shortText.Height}");
        var (first, last) = InkColumns(longText);
        Assert.True(first >= TextRenderer.MarginPx - 2, $"first ink column {first}");
        Assert.True(last <= 384 - TextRenderer.MarginPx + 2, $"last ink column {last}");
    }

    [Fact]
    public void Very_long_word_is_broken_instead_of_overflowing()
    {
        var bitmap = TextRenderer.Render(new string('W', 80));
        var (_, last) = InkColumns(bitmap);
        Assert.True(last <= 384 - TextRenderer.MarginPx + 2);
    }

    [Fact]
    public void Spanish_characters_render()
    {
        // Same text with and without diacritics must differ (accents and tildes are drawn).
        var accented = TextRenderer.Render("Página ñandú ¿qué?");
        var plain = TextRenderer.Render("Pagina nandu  que ");
        var differs = Enumerable.Range(0, Math.Min(accented.Height, plain.Height))
            .Any(y => !accented.Row(y).SequenceEqual(plain.Row(y)));
        Assert.True(differs);
    }

    [Fact]
    public void Bigger_font_is_taller_and_blank_lines_are_kept()
    {
        Assert.True(TextRenderer.Render("A", new TextStyle { SizePt = 24 }).Height > TextRenderer.Render("A", new TextStyle { SizePt = 8 }).Height);
        Assert.True(TextRenderer.Render("A\n\n\nB").Height > TextRenderer.Render("A\nB").Height);
    }

    [Fact]
    public void Right_alignment_moves_text_right()
    {
        var left = InkColumns(TextRenderer.Render("abc"));
        var right = InkColumns(TextRenderer.Render("abc", new TextStyle { Align = TextAlign.Right }));
        Assert.True(right.First > left.First + 100);
    }

    [Fact]
    public void Unknown_font_falls_back()
    {
        Assert.True(InkRows(TextRenderer.Render("Hola", new TextStyle { FontFamily = "No Such Font 123" })) > 5);
    }
}
