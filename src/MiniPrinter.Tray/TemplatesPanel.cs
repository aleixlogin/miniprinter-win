using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MiniPrinter.Control;

namespace MiniPrinter.Tray;

/// <summary>
/// "Plantillas" tab: builds a form from the service's template definitions, shows the exact
/// preview and prints. Remembers the last values in %LocalAppData%\MiniPrinter\templates.json.
/// </summary>
public sealed class TemplatesPanel
{
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniPrinter", "templates.json");

    private readonly ServiceConnection _service;
    private readonly ComboBox _templateCombo;
    private readonly StackPanel _form;
    private readonly Image _preview;
    private readonly TextBlock _status;
    private readonly Dictionary<string, Func<string?>> _readers = [];
    private Dictionary<string, Dictionary<string, string>> _saved = Load();
    private IReadOnlyList<TemplateDto> _templates = [];

    public TemplatesPanel(ServiceConnection service, ComboBox templateCombo, StackPanel form, Image preview, TextBlock status)
    {
        _service = service;
        _templateCombo = templateCombo;
        _form = form;
        _preview = preview;
        _status = status;
        _templateCombo.SelectionChanged += (_, _) => BuildForm();
    }

    public async Task LoadAsync()
    {
        if (_templates.Count > 0)
            return;
        try
        {
            _templates = await _service.Client.GetTemplatesAsync();
            _templateCombo.ItemsSource = _templates;
            _templateCombo.DisplayMemberPath = nameof(TemplateDto.Title);
            _templateCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    public async Task PreviewAsync()
    {
        if (Current() is not { } template)
            return;
        try
        {
            var png = await _service.Client.PreviewTemplateAsync(template.Name, Values());
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(png);
            image.EndInit();
            _preview.Source = image;
            _preview.Width = image.PixelWidth;   // real size: 1 dot = 1 device-independent pixel
            _preview.Height = image.PixelHeight;
            _status.Text = $"{image.PixelHeight / 8.0:0} mm de papel";
            Save(template.Name);
        }
        catch (Exception ex)
        {
            _preview.Source = null;
            _status.Text = ex.Message;
        }
    }

    public async Task PrintAsync()
    {
        if (Current() is not { } template)
            return;
        try
        {
            await _service.Client.PrintTemplateAsync(template.Name, Values());
            _status.Text = "Enviado a la impresora.";
            Save(template.Name);
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private TemplateDto? Current() => _templateCombo.SelectedItem as TemplateDto;

    private Dictionary<string, string> Values() =>
        _readers.Select(r => (r.Key, Value: r.Value())).Where(r => !string.IsNullOrEmpty(r.Value))
            .ToDictionary(r => r.Key, r => r.Value!);

    private void BuildForm()
    {
        _form.Children.Clear();
        _readers.Clear();
        _preview.Source = null;
        if (Current() is not { } template)
            return;
        _form.Children.Add(new TextBlock { Text = template.Description, Foreground = System.Windows.Media.Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var saved = _saved.GetValueOrDefault(template.Name) ?? [];

        foreach (var field in template.Fields)
        {
            _form.Children.Add(new TextBlock { Text = field.Label + (field.Required ? " *" : ""), Margin = new Thickness(0, 6, 0, 2) });
            var initial = saved.GetValueOrDefault(field.Name) ?? field.Default ?? "";
            switch (field.Kind)
            {
                case "Choice":
                {
                    var combo = new ComboBox { ItemsSource = field.Choices, SelectedItem = initial, Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
                    _form.Children.Add(combo);
                    _readers[field.Name] = () => combo.SelectedItem as string;
                    break;
                }
                case "Image":
                {
                    var path = new TextBlock { Text = "(ninguna)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                    string? base64 = null;
                    var button = new Button { Content = "Elegir imagen…" };
                    button.Click += (_, _) =>
                    {
                        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Imágenes|*.png;*.jpg;*.jpeg" };
                        if (dialog.ShowDialog() != true) return;
                        base64 = Convert.ToBase64String(File.ReadAllBytes(dialog.FileName));
                        path.Text = Path.GetFileName(dialog.FileName);
                    };
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    row.Children.Add(button);
                    row.Children.Add(path);
                    _form.Children.Add(row);
                    _readers[field.Name] = () => base64;
                    break;
                }
                default:
                {
                    var multiline = field.Kind == "MultilineText";
                    var box = new TextBox
                    {
                        Text = initial,
                        AcceptsReturn = multiline,
                        TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                        Height = multiline ? 90 : double.NaN,
                        VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                    };
                    _form.Children.Add(box);
                    _readers[field.Name] = () => box.Text;
                    break;
                }
            }
        }
    }

    private void Save(string template)
    {
        // Images are not remembered (they can be large).
        var imageFields = Current()?.Fields.Where(f => f.Kind == "Image").Select(f => f.Name).ToHashSet() ?? [];
        _saved[template] = Values().Where(v => !imageFields.Contains(v.Key)).ToDictionary();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(_saved));
        }
        catch (IOException)
        {
            // Remembering values is a convenience.
        }
    }

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(StatePath)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return [];
        }
    }
}
