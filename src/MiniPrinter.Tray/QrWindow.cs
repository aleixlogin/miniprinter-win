using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiniPrinter.Gui;

namespace MiniPrinter.Tray;

/// <summary>A small window with the QR code of an address and the address written under it.</summary>
public sealed class QrWindow : ThemedWindow
{
    private const int Scale = 8, QuietZone = 4;

    public QrWindow(Window owner, string address)
    {
        Owner = owner;
        Title = Strings.Get("Raw.QrTitle");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var picture = new Image { Source = Draw(address), Stretch = Stretch.None, SnapsToDevicePixels = true, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
        var close = new Button { Content = Strings.Get("Diag.Close"), Padding = new Thickness(14, 4, 14, 4), IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        close.Click += (_, _) => Close();

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(picture);
        panel.Children.Add(new TextBlock { Text = address, FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(Themed.Brush(new TextBlock { Text = Strings.Get("Raw.QrHint"), TextWrapping = TextWrapping.Wrap, MaxWidth = 320, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) }, Themed.Muted));
        panel.Children.Add(close);
        Content = panel;
    }

    /// <summary>The code on white with its quiet zone, as pixels (a QR code must be dark on light whatever the theme is).</summary>
    private static BitmapSource Draw(string text)
    {
        var modules = QrCode.Modules(text);
        var size = (modules.GetLength(0) + 2 * QuietZone) * Scale;
        var pixels = new byte[size * size * 4];
        Array.Fill(pixels, (byte)255);
        for (var y = 0; y < modules.GetLength(0); y++)
        for (var x = 0; x < modules.GetLength(1); x++)
        {
            if (!modules[y, x])
                continue;
            for (var dy = 0; dy < Scale; dy++)
            for (var dx = 0; dx < Scale; dx++)
            {
                var at = (((y + QuietZone) * Scale + dy) * size + (x + QuietZone) * Scale + dx) * 4;
                pixels[at] = pixels[at + 1] = pixels[at + 2] = 0;
            }
        }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
