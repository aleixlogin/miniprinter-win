using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace MiniPrinter.Tray;

/// <summary>
/// Colours for the elements that the code builds: they refer to a brush of the active theme by name, so they change with it
/// (a fixed brush would stay as it was when the theme switches). The names are the keys of Theme.*.xaml.
/// </summary>
internal static class Themed
{
    public const string Normal = "Brush.Text";
    public const string Muted = "Brush.TextMuted";
    public const string Error = "Brush.Error";
    public const string Ok = "Brush.Ok";
    public const string Warn = "Brush.Warn";
    public const string Disabled = "Brush.Disabled";
    public const string Surface = "Brush.Surface";
    public const string SurfaceAlt = "Brush.SurfaceAlt";
    public const string Border = "Brush.Border";
    public const string Accent = "Brush.Accent";
    public const string Preview = "Brush.Preview";
    public const string PreviewBorder = "Brush.PreviewBorder";
    public const string WarnBackground = "Brush.WarnBackground";
    public const string WarnBorder = "Brush.WarnBorder";

    /// <summary>Sets the text colour of an element to a theme brush and returns the element.</summary>
    public static T Brush<T>(T element, string key) where T : FrameworkElement
    {
        element.SetResourceReference(TextElement.ForegroundProperty, key);
        return element;
    }

    /// <summary>Sets the text colour of a text block to a theme brush (for the colour that changes while the program runs).</summary>
    public static void Foreground(TextBlock text, string key) => text.SetResourceReference(TextElement.ForegroundProperty, key);

    /// <summary>Sets the background of a control to a theme brush and returns it.</summary>
    public static T Frame2<T>(T control, string background) where T : System.Windows.Controls.Control
    {
        control.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, background);
        return control;
    }

    /// <summary>Sets the background and the border of a border to theme brushes and returns it.</summary>
    public static Border Frame(Border border, string? background, string? borderBrush)
    {
        if (background is not null)
            border.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, background);
        if (borderBrush is not null)
            border.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, borderBrush);
        return border;
    }
}
