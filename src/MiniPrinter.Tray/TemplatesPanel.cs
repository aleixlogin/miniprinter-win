using System.IO;
using MiniPrinter.Gui;
using System.Net.Http;
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
    Button CsvClear,
    Button Create,
    Button Edit,
    Button Delete,
    Window Owner);

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
    internal static readonly string FavoritesPath = Path.Combine(Folder, "favorites.json");

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

    /// <summary>Raised with the templates of the service when the list changes (the gallery shows them).</summary>
    public event Action<IReadOnlyList<TemplateDto>>? ListChanged;

    /// <summary>Raised with the name of a template after it was printed from the tab (it becomes a recent one).</summary>
    public event Action<string>? Printed;

    /// <summary>Raised with the name of a template that was edited or deleted (its picture is out of date).</summary>
    public event Action<string>? Edited;

    /// <summary>Raised with the name of the template chosen (null when there is none).</summary>
    public event Action<string?>? SelectionChanged;

    public string? CurrentName => Current()?.Name;

    public TemplatesPanel(ServiceConnection service, TemplatesUi ui)
    {
        _service = service;
        _ui = ui;
        _ui.TemplateCombo.SelectionChanged += (_, _) =>
        {
            BuildForm();
            UpdateButtons();
            SelectionChanged?.Invoke(CurrentName);
        };
        _ui.Create.Click += async (_, _) => await CreateAsync();
        _ui.Edit.Click += async (_, _) => await EditAsync();
        _ui.Delete.Click += async (_, _) => await DeleteAsync();
        UpdateButtons();
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
    public Task LoadAsync() => RefreshListAsync(null, -1, force: false);

    /// <summary>
    /// Reloads the list from the service. <paramref name="select"/> names the template to select afterwards;
    /// otherwise the current one stays selected, falling back to <paramref name="fallbackIndex"/> (or the first).
    /// </summary>
    private async Task RefreshListAsync(string? select, int fallbackIndex, bool force)
    {
        try
        {
            var fresh = await _service.Client.GetTemplatesAsync();
            var changed = _templates.Count == 0 || JsonSerializer.Serialize(fresh) != JsonSerializer.Serialize(_templates);
            var wanted = select ?? Current()?.Name;
            if (!changed)
            {
                if (select is not null)
                    SelectByName(select);
                if (force)
                    Schedule();   // same list, but a template's content may have changed
                return;
            }
            _templates = fresh;
            ListChanged?.Invoke(fresh);
            _ui.TemplateCombo.ItemsSource = _templates;
            _ui.TemplateCombo.DisplayMemberPath = nameof(TemplateDto.Title);
            var index = wanted is null ? -1 : _templates.ToList().FindIndex(t => t.Name == wanted);
            if (index < 0)
                index = fallbackIndex >= 0 ? Math.Min(fallbackIndex, _templates.Count - 1) : 0;
            _ui.TemplateCombo.SelectedIndex = Math.Max(0, index);
            UpdateButtons();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, error: true);
        }
    }

    /// <summary>Chooses the template with that name (once the list is loaded).</summary>
    public void SelectTemplate(string name) => SelectByName(name);

    private void SelectByName(string name)
    {
        var index = _templates.ToList().FindIndex(t => t.Name == name);
        if (index >= 0 && _ui.TemplateCombo.SelectedIndex != index)
            _ui.TemplateCombo.SelectedIndex = index;
    }

    // ---- create, edit and delete templates ------------------------------------------------------

    private void UpdateButtons()
    {
        var current = Current();
        _ui.Create.IsEnabled = _templates.Count > 0;
        _ui.Edit.IsEnabled = current is not null;
        // Built-in templates without a user version cannot be deleted; a user version restores the built-in.
        _ui.Delete.IsEnabled = current is { Source: not "builtin" };
        _ui.Delete.Content = current?.Source == "override" ? Strings.Get("Templates.RestoreBuiltIn") : Strings.Get("Templates.Delete");
        _ui.Delete.ToolTip = current?.Source == "builtin" ? Strings.Get("Templates.BuiltInCannotDelete") : null;
    }

    private async Task CreateAsync()
    {
        var names = _templates.Select(t => t.Name).ToList();
        var dialog = new NameDialog(_ui.Owner, Strings.Get("Templates.CreateTitle"), Strings.Get("Templates.CreatePrompt"),
            n => !TemplateEditorModel.IsValidName(n) ? Strings.Get("Templates.NameRules")
                : names.Contains(n, StringComparer.OrdinalIgnoreCase) ? Strings.Get("Templates.NameExists", n)
                : null,
            duplicateLabel: Current()?.Title);
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            var json = dialog.Duplicate && Current() is { } source
                ? TemplateEditorModel.Duplicate(await _service.Client.GetTemplateJsonAsync(source.Name), dialog.ChosenName)
                : TemplateEditorModel.Blank(dialog.ChosenName);
            await OpenEditorAsync(json, existing: false, source: "user");
        }
        catch (Exception ex) when (ex is ControlApiException or InvalidDataException or HttpRequestException or InvalidOperationException)
        {
            SetStatus(ex.Message, error: true);
        }
    }

    private async Task EditAsync()
    {
        if (Current() is not { } template)
            return;
        try
        {
            await OpenEditorAsync(await _service.Client.GetTemplateJsonAsync(template.Name), existing: true, source: template.Source);
        }
        catch (Exception ex) when (ex is ControlApiException or InvalidDataException or HttpRequestException or InvalidOperationException)
        {
            SetStatus(ex.Message, error: true);
        }
    }

    private async Task OpenEditorAsync(string json, bool existing, string source)
    {
        var schema = await _service.Client.GetTemplateSchemaAsync();
        var index = _ui.TemplateCombo.SelectedIndex;
        var window = new TemplateEditorWindow(_ui.Owner, _service, schema, json, existing, source, _templates.Select(t => t.Name).ToList());
        window.ShowDialog();
        if (window.SavedName is { } saved)
            Edited?.Invoke(saved);
        await RefreshListAsync(window.SavedName, index, force: true);
    }

    private async Task DeleteAsync()
    {
        if (Current() is not { } template || template.Source == "builtin")
            return;
        var restore = template.Source == "override";
        var question = restore
            ? Strings.Get("Templates.RestoreQuestion", template.Title)
            : Strings.Get("Templates.DeleteQuestion", template.Title);
        if (MessageBox.Show(_ui.Owner, question, restore ? Strings.Get("Templates.RestoreTitle") : Strings.Get("Templates.DeleteTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            var index = _ui.TemplateCombo.SelectedIndex;
            await _service.Client.DeleteTemplateAsync(template.Name);
            Edited?.Invoke(template.Name);
            SetStatus(restore ? Strings.Get("Templates.Restored") : Strings.Get("Templates.Deleted"), error: false);
            await RefreshListAsync(restore ? template.Name : null, Math.Max(0, index - 1), force: true);
        }
        catch (Exception ex) when (ex is ControlApiException or HttpRequestException or InvalidOperationException)
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
        var labels = _csvRows is null ? "" : Strings.Get("Templates.CsvLabels", _csvRows.Count) + " · ";
        return labels + Strings.Get("Templates.PaperLength", mm);
    }

    public async Task PrintAsync()
    {
        if (Current() is not { } template)
            return;
        if (!int.TryParse(_ui.Copies.Text.Trim(), out var copies) || copies is < 1 or > 50)
        {
            SetStatus(Strings.Get("Templates.CopiesRange"), error: true);
            return;
        }
        if (_csvRows is { Count: > ConfirmBatchOver } rows
            && MessageBox.Show(Strings.Get("Templates.BatchConfirm", rows.Count * copies), Strings.Get("Templates.Title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await _service.Client.PrintTemplateAsync(template.Name, Values(), copies, _csvRows);
            SetStatus(Strings.Get("Templates.Sent"), error: false);
            Save(template.Name);
            Printed?.Invoke(template.Name);
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
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Strings.Get("Templates.CsvFilter") };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            var csv = TemplateCsv.Parse(File.ReadAllText(dialog.FileName), template.Fields.Select(f => f.Name));
            _csvRows = csv.Rows;
            _ui.CsvClear.Visibility = Visibility.Visible;
            await PreviewAsync();
            if (csv.IgnoredColumns.Count > 0)
                SetStatus($"{Summary(_ui.Preview.Height / 8.0)} · " + Strings.Get("Templates.IgnoredColumns", string.Join(", ", csv.IgnoredColumns)), error: false);   // i18n-ok: composed of the summary and a translated part
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
            SetStatus(Strings.Get("Templates.FavoriteNeedsName"), error: true);
            return;
        }
        if (!_favorites.TryGetValue(template.Name, out var set))
            _favorites[template.Name] = set = [];
        set[name] = ValuesWithoutImages(template);
        SaveJson(FavoritesPath, _favorites);
        RefreshFavorites(template, name);
        SetStatus(Strings.Get("Templates.FavoriteSaved", name), error: false);
    }

    public void DeleteFavorite()
    {
        if (Current() is not { } template || _ui.FavoriteCombo.Text.Trim() is not { Length: > 0 } name)
            return;
        if (_favorites.TryGetValue(template.Name, out var set) && set.Remove(name))
        {
            SaveJson(FavoritesPath, _favorites);
            RefreshFavorites(template, null);
            SetStatus(Strings.Get("Templates.FavoriteDeleted", name), error: false);
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
        Themed.Foreground(_ui.Status, error ? Themed.Error : Themed.Muted);
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

        form.Children.Add(Themed.Brush(new TextBlock { Text = template.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }, Themed.Muted));
        if (template.Source == "override")
            form.Children.Add(Themed.Brush(new TextBlock { Text = Strings.Get("Templates.OverrideNote"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }, Themed.Warn));
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
                    var path = new TextBlock { Text = Strings.Get("Templates.NoImage"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                    string? base64 = null;
                    var button = new Button { Content = Strings.Get("Templates.ChooseImage") };
                    button.Click += (_, _) =>
                    {
                        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Strings.Get("Templates.ImageFilter") };
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
