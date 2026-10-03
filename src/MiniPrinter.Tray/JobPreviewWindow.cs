using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiniPrinter.Gui;

namespace MiniPrinter.Tray;

/// <summary>
/// The pages of a job exactly as they were sent to the printer, one below the other at their real size. When the service no
/// longer has them it says why instead of staying empty.
/// </summary>
public sealed class JobPreviewWindow : ThemedWindow
{
    private const int MaxPages = JobRowViewModel.MaxKeptPages;
    private readonly Func<int, Task<byte[]?>> _loadPage;
    private readonly string _unavailable;
    private readonly StackPanel _pages = new() { Margin = new Thickness(16) };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16), MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left };

    /// <param name="loadPage">Gives the PNG of a page (1-based) or null when there is none.</param>
    /// <param name="unavailable">What to say when not even the first page is there.</param>
    /// <param name="reprint">Prints the job again; null when it cannot be.</param>
    public JobPreviewWindow(Window owner, string title, Func<int, Task<byte[]?>> loadPage, string unavailable, Func<Task>? reprint)
    {
        Owner = owner;
        Title = title;
        _loadPage = loadPage;
        _unavailable = unavailable;
        Width = 520;
        Height = 640;
        MinWidth = 420;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var close = new Button { Content = Strings.Get("Preview.Close"), Padding = new Thickness(14, 4, 14, 4), IsCancel = true, IsDefault = true };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        if (reprint is not null)
        {
            var again = new Button { Content = Strings.Get("Status.Reprint"), Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
            again.Click += async (_, _) =>
            {
                again.IsEnabled = false;
                try
                {
                    await reprint();
                    Close();
                }
                catch (Exception ex)
                {
                    again.IsEnabled = true;
                    MessageBox.Show(this, ex.Message, "MiniPrinter", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            buttons.Children.Add(again);
        }
        buttons.Children.Add(close);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        var content = new StackPanel();
        content.Children.Add(_message);
        content.Children.Add(_pages);
        scroll.Content = content;
        root.Children.Add(scroll);
        Content = root;

        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var count = 0;
        try
        {
            for (var page = 1; page <= MaxPages; page++)
            {
                var png = await _loadPage(page);
                if (png is null)
                    break;
                count++;
                _pages.Children.Add(PageView(png));
            }
        }
        catch (Exception ex)
        {
            _message.Text = ex.Message;
            return;
        }
        if (count == 0)
        {
            _message.Text = _unavailable;
            return;
        }
        _message.Visibility = Visibility.Collapsed;
        for (var i = 0; i < _pages.Children.Count; i++)
            ((TextBlock)((StackPanel)_pages.Children[i]).Children[0]).Text = Strings.Get("Preview.Page", i + 1, count);
    }

    /// <summary>A page at its real size on white paper (the page is 1 bit: nothing is smoothed).</summary>
    private static StackPanel PageView(byte[] png)
    {
        var image = new BitmapImage();
        using (var stream = new MemoryStream(png))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
        }
        image.Freeze();
        var picture = new Image { Source = image, Stretch = Stretch.None, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
        var paper = new Border { BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Child = picture, Margin = new Thickness(0, 4, 0, 14) };
        paper.SetResourceReference(Border.BackgroundProperty, Themed.Preview);
        paper.SetResourceReference(Border.BorderBrushProperty, Themed.PreviewBorder);
        var label = new TextBlock();
        label.SetResourceReference(TextBlock.ForegroundProperty, Themed.Muted);
        var panel = new StackPanel();
        panel.Children.Add(label);
        panel.Children.Add(paper);
        return panel;
    }
}
