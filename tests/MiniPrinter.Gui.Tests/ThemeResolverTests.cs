namespace MiniPrinter.Gui.Tests;

public class ThemeResolverTests
{
    [Theory]
    [InlineData(ThemeChoice.Auto, true, EffectiveTheme.Light)]
    [InlineData(ThemeChoice.Auto, false, EffectiveTheme.Dark)]
    [InlineData(ThemeChoice.Light, false, EffectiveTheme.Light)]
    [InlineData(ThemeChoice.Dark, true, EffectiveTheme.Dark)]
    public void Automatic_follows_windows_and_a_fixed_choice_wins(ThemeChoice choice, bool windowsLight, EffectiveTheme expected) =>
        Assert.Equal(expected, ThemeResolver.Resolve(choice, windowsLight, windowsHighContrast: false));

    [Theory]
    [InlineData(ThemeChoice.Auto, true)]
    [InlineData(ThemeChoice.Auto, false)]
    [InlineData(ThemeChoice.Light, true)]
    [InlineData(ThemeChoice.Dark, false)]
    public void High_contrast_of_windows_always_wins(ThemeChoice choice, bool windowsLight) =>
        Assert.Equal(EffectiveTheme.HighContrast, ThemeResolver.Resolve(choice, windowsLight, windowsHighContrast: true));
}
