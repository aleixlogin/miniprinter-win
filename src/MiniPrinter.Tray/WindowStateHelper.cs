using System.Windows;
using MiniPrinter.Gui;

namespace MiniPrinter.Tray;

/// <summary>Remembers where the panel was left and puts it back, checked against the monitors that exist now.</summary>
internal static class WindowStateHelper
{
    /// <summary>The working area of each monitor, in device-independent units (at the scale of the primary screen).</summary>
    public static IReadOnlyList<ScreenArea> Screens()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var primaryWidth = screens.FirstOrDefault(s => s.Primary)?.Bounds.Width ?? (int)SystemParameters.PrimaryScreenWidth;
        var scale = Math.Max(0.5, primaryWidth / SystemParameters.PrimaryScreenWidth);
        return [.. screens.Select(s => new ScreenArea(s.WorkingArea.Left / scale, s.WorkingArea.Top / scale, s.WorkingArea.Width / scale, s.WorkingArea.Height / scale))];
    }

    /// <summary>Applies the saved position and size before the window is shown; centres it when there is nothing valid to restore.</summary>
    public static void Restore(Window window, TrayPreferences preferences)
    {
        var bounds = WindowPlacement.Validate(preferences.Window, Screens(), window.MinWidth, window.MinHeight);
        if (bounds is null)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        if (bounds.Maximized)
            window.WindowState = WindowState.Maximized;
    }

    /// <summary>Where the window is now (when maximized or minimized, where it returns to).</summary>
    public static WindowBounds Capture(Window window)
    {
        var rect = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.ActualWidth > 0 ? window.ActualWidth : window.Width, window.ActualHeight > 0 ? window.ActualHeight : window.Height)
            : window.RestoreBounds;
        return new WindowBounds(rect.Left, rect.Top, rect.Width, rect.Height, window.WindowState == WindowState.Maximized);
    }
}
