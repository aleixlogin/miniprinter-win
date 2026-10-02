using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MiniPrinter.Control;

namespace MiniPrinter.Tray;

/// <summary>Quick note: type, preview at real size, Ctrl+Enter prints, Esc closes.</summary>
public sealed class QuickNoteWindow : Window
{
    private readonly ServiceConnection _service;
    private readonly TextBox _text = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontSize = 14,
    };
    private readonly ComboBox _size = new() { Width = 70, ItemsSource = new[] { 8, 10, 12, 14, 18, 24, 32 } };
    private readonly Image _preview = new() { Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _status = new() { Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public QuickNoteWindow(ServiceConnection service, float sizePt)
    {
        _service = service;
        Title = "Nota rápida — MiniPrinter";
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/miniprinter.ico"));
        Width = 760;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        _size.SelectedItem = (int)sizePt is var s && ((int[])_size.ItemsSource).Contains(s) ? s : 12;

        var print = new Button { Content = "Imprimir (Ctrl+Enter)", Padding = new Thickness(12, 4, 12, 4), FontWeight = FontWeights.SemiBold };
        print.Click += async (_, _) => await PrintAsync();
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        toolbar.Children.Add(new TextBlock { Text = "Tamaño:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        toolbar.Children.Add(_size);
        toolbar.Children.Add(new TextBlock { Text = "pt", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 16, 0) });
        toolbar.Children.Add(print);
        toolbar.Children.Add(_status);

        var previewBox = new Border
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Width = 410,
            Margin = new Thickness(12, 0, 0, 0),
            Child = new ScrollViewer { Content = _preview, Padding = new Thickness(12), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        };
        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(_text);
        Grid.SetColumn(previewBox, 1);
        grid.Children.Add(previewBox);
        Grid.SetRow(toolbar, 1);
        Grid.SetColumnSpan(toolbar, 2);
        grid.Children.Add(toolbar);
        Content = grid;

        _debounce.Tick += async (_, _) => { _debounce.Stop(); await PreviewAsync(); };
        _text.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _size.SelectionChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;
                await PrintAsync();
            }
            else if (e.Key == Key.Escape)
            {
                Close();
            }
        };
        Loaded += (_, _) => _text.Focus();
    }

    public float SizePt => _size.SelectedItem is int s ? s : 12;

    private async Task PreviewAsync()
    {
        if (string.IsNullOrWhiteSpace(_text.Text))
        {
            _preview.Source = null;
            return;
        }
        try
        {
            var png = await _service.Client.PreviewTextAsync(_text.Text, SizePt);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(png);
            image.EndInit();
            _preview.Source = image;
            _preview.Width = image.PixelWidth;
            _preview.Height = image.PixelHeight;
            _status.Text = $"{image.PixelHeight / 8.0:0} mm de papel";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private async Task PrintAsync()
    {
        if (string.IsNullOrWhiteSpace(_text.Text))
            return;
        try
        {
            await _service.Client.PrintTextAsync(_text.Text, SizePt);
            Close();
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }
}
