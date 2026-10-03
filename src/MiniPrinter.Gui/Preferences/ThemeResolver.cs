namespace MiniPrinter.Gui;

/// <summary>The palette actually shown.</summary>
public enum EffectiveTheme
{
    Light,
    Dark,
    HighContrast,
}

/// <summary>Decides which palette to show from the choice of the user and what Windows is set to.</summary>
public static class ThemeResolver
{
    /// <summary>
    /// High contrast of Windows always wins (it is an accessibility setting). Otherwise a fixed choice of the user
    /// wins over Windows, and Automatic follows the "apps" colour mode of Windows.
    /// </summary>
    public static EffectiveTheme Resolve(ThemeChoice choice, bool windowsUsesLightApps, bool windowsHighContrast)
    {
        if (windowsHighContrast)
            return EffectiveTheme.HighContrast;
        return choice switch
        {
            ThemeChoice.Light => EffectiveTheme.Light,
            ThemeChoice.Dark => EffectiveTheme.Dark,
            _ => windowsUsesLightApps ? EffectiveTheme.Light : EffectiveTheme.Dark,
        };
    }
}
