namespace MiniPrinter.Gui.Tests;

public class TextSizesTests
{
    [Theory]
    [InlineData(TextSizeChoice.Small, 12)]
    [InlineData(TextSizeChoice.Normal, 13)]
    [InlineData(TextSizeChoice.Large, 15)]
    public void Each_size_has_its_font_size(TextSizeChoice size, double expected) =>
        Assert.Equal(expected, TextSizes.FontSize(size));

    [Fact]
    public void The_minimum_size_of_the_windows_grows_only_with_larger_text()
    {
        Assert.Equal(1.0, TextSizes.MinimumScale(TextSizeChoice.Normal));
        Assert.True(TextSizes.MinimumScale(TextSizeChoice.Large) > 1.1);
        Assert.True(TextSizes.MinimumScale(TextSizeChoice.Small) < 1.0);
    }
}
