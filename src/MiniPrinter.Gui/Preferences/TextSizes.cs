namespace MiniPrinter.Gui;

/// <summary>The sizes of text the user can choose, and how much room the windows need at each.</summary>
public static class TextSizes
{
    /// <summary>Base font size of the panel, in device-independent units.</summary>
    public static double FontSize(TextSizeChoice size) => size switch
    {
        TextSizeChoice.Small => 12,
        TextSizeChoice.Large => 15,
        _ => 13,
    };

    /// <summary>The factor by which the minimum size of a window grows with the text, so that nothing is cut.</summary>
    public static double MinimumScale(TextSizeChoice size) => FontSize(size) / FontSize(TextSizeChoice.Normal);
}
