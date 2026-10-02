using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MiniPrinter.Control;

namespace MiniPrinter.Tray;

/// <summary>The controls of the "Plantillas" tab that the panel drives.</summary>
public sealed record TemplatesUi(
    ComboBox TemplateCombo,
    StackPanel Form,
    Image Preview,
    TextBlock Status,
    ComboBox FavoriteCombo,
    TextBox Copies,
    Button CsvClear);

/// <summary>
/// "Plantillas" tab: builds a form from the service's template definitions, shows the exact preview
/// (live, while typing), prints with copies or a CSV batch, and keeps named favorites. Remembers the
/// last values in %LocalAppData%\MiniPrinter\templates.json and the favorites in favorites.json.
/// </summary>
public sealed class TemplatesPanel
{
    private const int ConfirmBatchOver = 20;
    private static readonly TimeSpan LiveDelay = TimeSpan.FromMilliseconds(400);

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniPrinter");
    private static readonly string StatePath = Path.Combine(Folder, "templates.json");
    private static readonly string FavoritesPath = Path.Combine(Folder, "favorites.json");

    private readonly ServiceConnection _service;
    private readonly TemplatesUi _ui;
    private readonly DispatcherTimer _debounce = new() { Interval = LiveDelay };
    private readonly Dictionary<string, Func<string?>> _readers = [];
    private readonly Dictionary<string, Action<string>> _writers = [];
    private Dictionary<string, Dictionary<string, string>> _saved = LoadJson<Dictionary<string, Dictionary<string, string>>>(StatePath);
    private Dictionary<string, Dictionary<string, Dictionary<string, string>>> _favorites =
        LoadJson<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(FavoritesPath);
    private IReadOnlyList<TemplateDto> _templates = [];
    private IReadOnlyList<IReadOnlyDictionary<string, string>>? _csvRows;
    private int _sequence;
    private bool _building;

    public TemplatesPanel(ServiceConnection service, TemplatesUi ui)
    {
        _service = service;
        _ui = ui;
        _ui.TemplateCombo.SelectionChanged += (_, _) => BuildForm();
        _ui.FavoriteCombo.SelectionChanged += (_, _) => LoadFavorite();
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await PreviewAsync();
        };
        _ui.Copies.TextChanged += (_, _) => Schedule();
    }

    /// <summary>
    /// Fetches the template list (on opening the tab and from the refresh button, so templates created
    /// through the API or the CLI show up). The form is only rebuilt if the list actually changed.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            var fresh = await _service.Client.GetTemplatesAsync();
            if (_templates.Count > 0 && JsonSerializer.Serialize(fresh) == JsonSerializer.Serialize(_templates))
                return;
            var selected = Current()?.Name;
            _templates = fresh;
            _ui.TemplateCombo.ItemsSource = _templates;
            _ui.TemplateCombo.DisplayMemberPath = nameof(TemplateDto.Title);
            var index = selected is null ? 0 : _templates.ToList().FindIndex(t => t.Name == selected);
            _ui.TemplateCombo.SelectedIndex = Math.Max(0, index);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, error: true);
        }
    }

    // ---- preview and print -------------------------------------------------------------------

    /// <summary>Restarts the live-preview timer: the preview refreshes 400 ms after the last change.</summary>
    private void Schedule()
    {
        if (_building)
            return;
        _debounce.Stop();
        _debounce.Start();
    }

    public async Task PreviewAsync()
    {
        if (Current() is not { } template)
            return;
        // Only the answer to the most recent request may update the preview.
        var mine = ++_sequence;
        try
        {
            var png = await _service.Client.PreviewTemplateAsync(template.Name, Values(), _csvRows);
            if (mine != _sequence)
                return;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(png);
            image.EndInit();
            _ui.Preview.Source = image;
            _ui.Preview.Width = image.PixelWidth;   // real size: 1 dot = 1 device-independent pixel
            _ui.Preview.Height = image.PixelHeight;
            SetStatus(Summary(image.PixelHeight / 8.0), error: false);
            Save(template.Name);
        }
        catch (Exception ex)
        {
            if (mine != _sequence)
                return;
            SetStatus(ex.Message, error: true);
        }
    }

    private string Summary(double mm)
    {
        var labels = _csvRows is null ? "" : $"{_csvRows.Count} etiqueta(s) del CSV · ";
        return $"{labels}{mm:0} mm de papel";
    }

    public async Task PrintAsync()
    {
        if (Current() is not { } template)
            return;
        if (!int.TryParse(_ui.Copies.Text.Trim(), out var copies) || copies is < 1 or > 50)
        {
            SetStatus("Las copias deben estar entre 1 y 50.", error: true);
            return;
        }
        if (_csvRows is { Count: > ConfirmBatchOver } rows
            && MessageBox.Show($"Se van a imprimir {rows.Count * copies} etiquetas. ¿Continuar?", "Plantillas",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await _service.Client.PrintTemplateAsync(template.Name, Values(), copies, _csvRows);
            SetStatus("Enviado a la impresora.", error: false);
            Save(template.Name);
            await PreviewAsync();   // a numbered template now shows the next number
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, error: true);
        }
    }

    // ---- CSV -----------------------------------------------------------------------------------

    public async Task LoadCsvAsync()
    {
        if (Current() is not { } template)
            return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "CSV|*.csv;*.txt|Todos|*.*" };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            var csv = TemplateCsv.Parse(File.ReadAllText(dialog.FileName), template.Fields.Select(f => f.Name));
            _csvRows = csv.Rows;
            _ui.CsvClear.Visibility = Visibility.Visible;
            await PreviewAsync();
            if (csv.IgnoredColumns.Count > 0)
                SetStatus($"{Summary(_ui.Preview.Height / 8.0)} · se ignoran las columnas: {string.Join(", ", csv.IgnoredColumns)}", error: false);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            SetStatus(ex.Message, error: true);
        }
    }

    public async Task ClearCsvAsync()
    {
        _csvRows = null;
        _ui.CsvClear.Visibility = Visibility.Collapsed;
        await PreviewAsync();
    }

    // ---- favorites -----------------------------------------------------------------------------

    public void SaveFavorite()
    {
        if (Current() is not { } template)
            return;
        var name = _ui.FavoriteCombo.Text.Trim();
        if (name.Length == 0)
        {
            SetStatus("Escribe un nombre para el favorito.", error: true);
            return;
        }
        if (!_favorites.TryGetValue(template.Name, out var set))
            _favorites[template.Name] = set = [];
        set[name] = ValuesWithoutImages(template);
        SaveJson(FavoritesPath, _favorites);
        RefreshFavorites(template, name);
        SetStatus($"Favorito '{name}' guardado.", error: false);
    }

    public void DeleteFavorite()
    {
        if (Current() is not { } template || _ui.FavoriteCombo.Text.Trim() is not { Length: > 0 } name)
            return;
        if (_favorites.TryGetValue(template.Name, out var set) && set.Remove(name))
        {
            SaveJson(FavoritesPath, _favorites);
            RefreshFavorites(template, null);
            SetStatus($"Favorito '{name}' borrado.", error: false);
        }
    }

    private void LoadFavorite()
    {
        if (_building || Current() is not { } template || _ui.FavoriteCombo.SelectedItem is not string name)
            return;
        if (_favorites.GetValueOrDefault(template.Name)?.GetValueOrDefault(name) is not { } values)
            return;
        _building = true;
        try
        {
            foreach (var (field, write) in _writers)
                write(values.GetValueOrDefault(field) ?? "");
        }
        finally
        {
            _building = false;
        }
        Schedule();
    }

    private void RefreshFavorites(TemplateDto template, string? select)
    {
        _building = true;
        try
        {
            _ui.FavoriteCombo.ItemsSource = _favorites.GetValueOrDefault(template.Name)?.Keys.Order().ToList() ?? [];
            _ui.FavoriteCombo.Text = select ?? "";
        }
        finally
        {
            _building = false;
        }
    }

    // ---- form ----------------------------------------------------------------------------------

    private TemplateDto? Current() => _ui.TemplateCombo.SelectedItem as TemplateDto;

    private void SetStatus(string text, bool error)
    {
        _ui.Status.Text = text;
        _ui.Status.Foreground = error ? Brushes.Firebrick : new SolidColorBrush(Color.FromRgb(0x57, 0x60, 0x6A));
    }

    private Dictionary<string, string> Values() =>
        _readers.Select(r => (r.Key, Value: r.Value())).Where(r => !string.IsNullOrEmpty(r.Value))
            .ToDictionary(r => r.Key, r => r.Value!);

    private Dictionary<string, string> ValuesWithoutImages(TemplateDto template)
    {
        // Images are not remembered (they can be large).
        var imageFields = template.Fields.Where(f => f.Kind == "Image").Select(f => f.Name).ToHashSet();
        return Values().Where(v => !imageFields.Contains(v.Key)).ToDictionary();
    }

    private void BuildForm()
    {
        _building = true;
        try
        {
            BuildFormCore();
        }
        finally
        {
            _building = false;
        }
        Schedule();
    }

    private void BuildFormCore()
    {
        var form = _ui.Form;
        form.Children.Clear();
        _readers.Clear();
        _writers.Clear();
        _ui.Preview.Source = null;
        _csvRows = null;
        _ui.CsvClear.Visibility = Visibility.Collapsed;
        if (Current() is not { } template)
            return;
        RefreshFavorites(template, null);

        form.Children.Add(new TextBlock { Text = template.Description, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        if (template.Source == "override")
            form.Children.Add(new TextBlock { Text = "Una plantilla de usuario sustituye a esta plantilla integrada.", Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var saved = _saved.GetValueOrDefault(template.Name) ?? [];

        foreach (var field in template.Fields)
        {
            var initial = saved.GetValueOrDefault(field.Name) ?? field.Default ?? "";
            if (field.Kind == "Boolean")
            {
                var check = new CheckBox { Content = field.Label, IsChecked = ToBool(initial), Margin = new Thickness(0, 8, 0, 2) };
                check.Click += (_, _) => Schedule();
                form.Children.Add(check);
                _readers[field.Name] = () => check.IsChecked == true ? "true" : null;
                _writers[field.Name] = v => check.IsChecked = ToBool(v);
                continue;
            }

            form.Children.Add(new TextBlock { Text = field.Label + (field.Required ? " *" : ""), Margin = new Thickness(0, 6, 0, 2) });
            switch (field.Kind)
            {
                case "Choice":
                {
                    var combo = new ComboBox { ItemsSource = field.Choices, SelectedItem = initial, Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
                    combo.SelectionChanged += (_, _) => Schedule();
                    form.Children.Add(combo);
                    _readers[field.Name] = () => combo.SelectedItem as string;
                    _writers[field.Name] = v => combo.SelectedItem = field.Choices?.Contains(v) == true ? v : field.Default;
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
                        Schedule();
                    };
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    row.Children.Add(button);
                    row.Children.Add(path);
                    form.Children.Add(row);
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
                    box.TextChanged += (_, _) => Schedule();
                    form.Children.Add(box);
                    _readers[field.Name] = () => box.Text;
                    _writers[field.Name] = v => box.Text = v;
                    break;
                }
            }
        }
    }

    private static bool ToBool(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().ToLowerInvariant() is not ("false" or "0" or "no");

    // ---- persistence ---------------------------------------------------------------------------

    private void Save(string template)
    {
        if (Current() is not { } current)
            return;
        _saved[template] = ValuesWithoutImages(current);
        SaveJson(StatePath, _saved);
    }

    private static void SaveJson<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(value));
        }
        catch (IOException)
        {
            // Remembering values is a convenience.
        }
    }

    private static T LoadJson<T>(string path) where T : new()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? new T() : new T();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new T();
        }
    }
}
