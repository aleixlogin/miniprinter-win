using System.Windows;
using System.Windows.Interop;

namespace MiniPrinter.Tray;

/// <summary>
/// Base of every window of the tray: takes its background and text colour from the active palette and keeps the title
/// bar in step with it. (An implicit style cannot do this: it would not apply to classes derived from Window.)
/// </summary>
public class ThemedWindow : Window
{
    public ThemedWindow()
    {
        SetResourceReference(BackgroundProperty, "Brush.Surface");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        SetResourceReference(FontSizeProperty, "Ui.FontSize");
        SourceInitialized += (_, _) => ApplyTitleBar();
        if (Application.Current is App { Theme: { } theme })
        {
            theme.Changed += OnThemeChanged;
            Closed += (_, _) => theme.Changed -= OnThemeChanged;
        }
    }

    private void OnThemeChanged(MiniPrinter.Gui.EffectiveTheme _) => ApplyTitleBar();

    private void ApplyTitleBar()
    {
        if (Application.Current is App { Theme: { } theme } && new WindowInteropHelper(this).Handle != IntPtr.Zero)
            ThemeService.ApplyTitleBar(this, theme.Current);
    }
}
