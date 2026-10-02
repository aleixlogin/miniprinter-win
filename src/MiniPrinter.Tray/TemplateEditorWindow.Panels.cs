using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiniPrinter.Control;

namespace MiniPrinter.Tray;

public sealed partial class TemplateEditorWindow
{
    private static readonly Brush ErrorBrush = Brushes.Firebrick;
    private static readonly Brush NormalBorder = new SolidColorBrush(Color.FromRgb(0xAB, 0xAD, 0xB3));

    private readonly TextBox _raw = new()
    {
        AcceptsReturn = true,
        AcceptsTab = true,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    private readonly TextBlock _rawStatus = new() { Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    private bool _updatingRaw;

    // template tab
    private readonly TextBox _tplTitle = new() { IsUndoEnabled = false };
    private readonly TextBox _tplDescription = new() { IsUndoEnabled = false, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 52 };
    private readonly ComboBox _tplMode = new() { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _tplGap = new() { Width = 60, HorizontalAlignment = HorizontalAlignment.Left, IsUndoEnabled = false };
    private readonly TextBox _tplFrame = new() { IsUndoEnabled = false };
    private readonly ListBox _fieldList = new() { Height = 120 };
    private readonly StackPanel _fieldForm = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBox _fLabel = new() { IsUndoEnabled = false };
    private readonly ComboBox _fKind = new();
    private readonly CheckBox _fRequired = new() { Content = "Obligatorio" };
    private readonly TextBox _fDefault = new() { IsUndoEnabled = false };
    private readonly TextBox _fChoices = new() { IsUndoEnabled = false };
    private readonly StackPanel _fChoicesRow = new();
    private readonly TextBlock _fieldMessage = new() { Foreground = ErrorBrush, TextWrapping = TextWrapping.Wrap };
    private int _fieldSelected = -1;

    // test data
    private readonly StackPanel _testPanel = new() { Margin = new Thickness(6) };
    private readonly Dictionary<string, string> _testData = new(StringComparer.OrdinalIgnoreCase);
    private string _fieldSignature = "";
    private string _originalName = "";

    // ---- properties of the selected block ---------------------------------------------------------------

    private void BuildProps()
    {
        _props.Children.Clear();
        _propControls.Clear();
        _blockError.Visibility = Visibility.Collapsed;
        if (_selected < 0 || _selected >= _model.BlockCount)
        {
            _blockTitle.Text = "Ningún bloque seleccionado";
            return;
        }
        var type = _model.BlockType(_selected);
        var definition = _schema.Blocks.FirstOrDefault(b => b.Type == type);
        _blockTitle.Text = $"Bloque {_selected + 1} — {definition?.Title ?? type}";
        if (definition is null)
        {
            _props.Children.Add(new TextBlock { Text = $"Tipo de bloque desconocido: {type}. Edítalo en la pestaña JSON.", TextWrapping = TextWrapping.Wrap });
            return;
        }
        var index = _selected;
        foreach (var prop in definition.Props.Concat(_schema.Common))
            _props.Children.Add(BuildPropRow(index, type, prop));
        ApplyErrorToProps();
    }

    private UIElement BuildPropRow(int index, string blockType, SchemaPropDto prop)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var range = prop.Kind is "int" or "number" && prop.Min is not null ? $" ({prop.Min:0.##}–{prop.Max:0.##})" : "";
        if (prop.Kind != "bool")
            row.Children.Add(new TextBlock { Text = prop.Label + range, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 2) });
        var message = new TextBlock { Foreground = ErrorBrush, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var key = $"b{index}.{prop.Name}";
        System.Windows.Controls.Control control;

        switch (prop.Kind)
        {
            case "bool":
            {
                var check = new CheckBox { Content = prop.Label, IsChecked = _model.GetBlockProperty(index, prop.Name) == "true" };
                check.Click += (_, _) => Edit(() => _model.SetBlockProperty(index, prop, check.IsChecked == true ? "true" : ""));
                control = check;
                row.Children.Add(check);
                break;
            }
            case "enum":
            {
                var combo = new ComboBox { ItemsSource = prop.Values };
                var current = _model.GetBlockProperty(index, prop.Name);
                combo.SelectedItem = prop.Values!.FirstOrDefault(v => v.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? prop.Values![0];
                combo.SelectionChanged += (_, _) =>
                {
                    // The first option is the default: store nothing for it.
                    var chosen = combo.SelectedItem as string;
                    Edit(() => _model.SetBlockProperty(index, prop, chosen == prop.Values![0] ? "" : chosen));
                };
                control = combo;
                row.Children.Add(combo);
                break;
            }
            default:
            {
                var box = new TextBox
                {
                    Text = _model.GetBlockProperty(index, prop.Name),
                    IsUndoEnabled = false,
                    AcceptsReturn = prop.Multiline,
                    TextWrapping = prop.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    Height = prop.Multiline ? 80 : double.NaN,
                    VerticalScrollBarVisibility = prop.Multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                };
                box.TextChanged += (_, _) => Edit(() => _model.SetBlockProperty(index, prop, box.Text, key));
                control = box;
                if (blockType == "image" && prop.Name == "source")
                    row.Children.Add(ImageSourceRow(index, prop, box));
                else if (prop.Kind == "text")
                    row.Children.Add(WithInsertButton(box));
                else
                    row.Children.Add(box);
                break;
            }
        }
        row.Children.Add(message);
        _propControls[prop.Name] = (control, message);
        return row;
    }

    private UIElement WithInsertButton(TextBox box)
    {
        var dock = new DockPanel();
        var button = new Button
        {
            Content = "{ }",
            ToolTip = "Insertar un campo o un valor automático",
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Height = 24,
        };
        button.Click += (_, _) =>
        {
            var menu = BuildInsertMenu(token =>
            {
                var start = box.SelectionStart;
                box.Focus();
                box.SelectedText = token;
                box.Select(start + token.Length, 0);
            });
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        };
        DockPanel.SetDock(button, Dock.Right);
        dock.Children.Add(button);
        dock.Children.Add(box);
        return dock;
    }

    /// <summary>Menu with the template's fields, the automatic values and the fields with a filter.</summary>
    private ContextMenu BuildInsertMenu(Action<string> insert)
    {
        var menu = new ContextMenu();
        MenuItem Item(string token, string? header = null)
        {
            var item = new MenuItem { Header = header ?? token };
            item.Click += (_, _) => insert(token);
            return item;
        }
        var fields = _model.FieldNames.Where(n => n.Length > 0).ToList();
        if (fields.Count == 0)
            menu.Items.Add(new MenuItem { Header = "(la plantilla no tiene campos: añádelos en la pestaña Plantilla)", IsEnabled = false });
        foreach (var field in fields)
            menu.Items.Add(Item($"{{{{{field}}}}}"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("{{now}}", "{{now}}  (fecha y hora)"));
        menu.Items.Add(Item("{{now:dd/MM/yyyy}}", "{{now:dd/MM/yyyy}}  (fecha)"));
        menu.Items.Add(Item("{{now:HH:mm}}", "{{now:HH:mm}}  (hora)"));
        menu.Items.Add(Item("{{counter}}", "{{counter}}  (número consecutivo)"));
        if (fields.Count > 0)
        {
            var filters = new MenuItem { Header = "Campo con filtro" };
            foreach (var field in fields)
            {
                var sub = new MenuItem { Header = field };
                foreach (var filter in _schema.Filters)
                    sub.Items.Add(Item($"{{{{{field}|{filter}}}}}", filter));
                filters.Items.Add(sub);
            }
            menu.Items.Add(filters);
        }
        return menu;
    }

    private UIElement ImageSourceRow(int index, SchemaPropDto prop, TextBox box)
    {
        box.IsReadOnly = true;
        var panel = new StackPanel();
        panel.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var choose = new Button { Content = "Elegir imagen…", Padding = new Thickness(8, 2, 8, 2) };
        var remove = new Button { Content = "Quitar", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
        choose.Click += async (_, _) => await ChooseImageAsync(index, prop, box);
        remove.Click += (_, _) => Edit(() =>
        {
            _model.SetBlockProperty(index, prop, "");
            box.Text = "";
        });
        buttons.Children.Add(choose);
        buttons.Children.Add(remove);
        panel.Children.Add(buttons);
        return panel;
    }

    private async Task ChooseImageAsync(int index, SchemaPropDto prop, TextBox box)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Imágenes|*.png;*.jpg;*.jpeg" };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            var bytes = await File.ReadAllBytesAsync(dialog.FileName);
            if (bytes.Length > 1024 * 1024)
            {
                SetStatus("La imagen supera el máximo de 1 MB.", error: true);
                return;
            }
            await EnsureDraftAsync();
            if (_draft is null)
            {
                SetStatus("No se pudo preparar la subida de la imagen: el servicio no responde.", error: true);
                return;
            }
            var name = SafeFileName(Path.GetFileName(dialog.FileName));
            await _service.Client.SaveDraftAssetAsync(_draft, name, bytes);
            Edit(() =>
            {
                _model.SetBlockProperty(index, prop, name);
                box.Text = name;
            });
        }
        catch (Exception ex) when (ex is ControlApiException or IOException or HttpRequestException or InvalidOperationException)
        {
            SetStatus(ex.Message, error: true);
        }
    }

    /// <summary>A name the service accepts for an image: letters, digits, dot, dash and underscore, ending in png/jpg/jpeg.</summary>
    internal static string SafeFileName(string file)
    {
        var cleaned = Regex.Replace(file, @"[^A-Za-z0-9._-]", "-").TrimStart('.', '-', '_');
        if (cleaned.Length == 0 || !Regex.IsMatch(cleaned, @"\.(png|jpe?g)$", RegexOptions.IgnoreCase))
            cleaned = "imagen.png";
        if (cleaned.Length > 64)
        {
            var ext = Path.GetExtension(cleaned);
            cleaned = cleaned[..(64 - ext.Length)] + ext;
        }
        return cleaned;
    }

    /// <summary>Marks the control of the property the service complained about, or shows the message on the block.</summary>
    private void ApplyErrorToProps()
    {
        foreach (var (box, message) in _propControls.Values)
        {
            box.BorderBrush = NormalBorder;
            box.ToolTip = null;
            message.Visibility = Visibility.Collapsed;
        }
        _blockError.Visibility = Visibility.Collapsed;
        if (_error?.Block is not { } block || block != _selected + 1)
            return;
        if (_error.Property is { } property && _propControls.TryGetValue(property, out var control))
        {
            control.Box.BorderBrush = ErrorBrush;
            control.Message.Text = _error.Message;
            control.Message.Visibility = Visibility.Visible;
        }
        else
        {
            _blockError.Text = _error.Message;
            _blockError.Visibility = Visibility.Visible;
        }
    }

    // ---- template tab ----------------------------------------------------------------------------------

    private UIElement BuildTemplateTab()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        _originalName = _model.Name;
        panel.Children.Add(Label("Nombre"));
        panel.Children.Add(new TextBox
        {
            Text = _model.Name,
            IsReadOnly = true,
            Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
            ToolTip = "El nombre no se cambia: usa «Guardar como…» para crear otra plantilla.",
        });
        panel.Children.Add(Label("Título"));
        panel.Children.Add(_tplTitle);
        panel.Children.Add(Label("Descripción"));
        panel.Children.Add(_tplDescription);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var modeBox = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        modeBox.Children.Add(Label("Modo"));
        _tplMode.ItemsSource = _schema.Template.Modes;
        modeBox.Children.Add(_tplMode);
        var gapBox = new StackPanel();
        gapBox.Children.Add(Label($"Hueco ({_schema.Template.MinGap}–{_schema.Template.MaxGap})"));
        gapBox.Children.Add(_tplGap);
        row.Children.Add(modeBox);
        row.Children.Add(gapBox);
        panel.Children.Add(row);

        panel.Children.Add(Label("Marco (vacío = sin marco; p. ej. {{marco}})"));
        panel.Children.Add(WithInsertButton(_tplFrame));

        _tplTitle.TextChanged += (_, _) => TemplateEdit(() => _model.SetTemplateProperty("title", _tplTitle.Text, "tpl.title"));
        _tplDescription.TextChanged += (_, _) => TemplateEdit(() => _model.SetTemplateProperty("description", _tplDescription.Text, "tpl.description"));
        _tplGap.TextChanged += (_, _) => TemplateEdit(() => _model.SetTemplateProperty("gap", _tplGap.Text, "tpl.gap"));
        _tplFrame.TextChanged += (_, _) => TemplateEdit(() => _model.SetTemplateProperty("frame", _tplFrame.Text, "tpl.frame"));
        _tplMode.SelectionChanged += (_, _) =>
            TemplateEdit(() => _model.SetTemplateProperty("mode", _tplMode.SelectedItem as string == "text" ? "" : _tplMode.SelectedItem as string));

        // Fields
        panel.Children.Add(new TextBlock { Text = "Campos", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) });
        panel.Children.Add(_fieldList);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var add = new Button { Content = "Añadir campo", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
        var delete = new Button { Content = "Borrar campo", Padding = new Thickness(8, 2, 8, 2) };
        add.Click += (_, _) => AddField();
        delete.Click += (_, _) => DeleteField();
        buttons.Children.Add(add);
        buttons.Children.Add(delete);
        panel.Children.Add(buttons);

        _fieldForm.Children.Add(Label("Etiqueta"));
        _fieldForm.Children.Add(_fLabel);
        _fieldForm.Children.Add(Label("Tipo"));
        _fKind.ItemsSource = _schema.FieldKinds;
        _fKind.HorizontalAlignment = HorizontalAlignment.Left;
        _fKind.Width = 140;
        _fieldForm.Children.Add(_fKind);
        _fRequired.Margin = new Thickness(0, 6, 0, 0);
        _fieldForm.Children.Add(_fRequired);
        _fieldForm.Children.Add(Label("Valor por defecto"));
        _fieldForm.Children.Add(_fDefault);
        _fChoicesRow.Children.Add(Label("Opciones (separadas por comas)"));
        _fChoicesRow.Children.Add(_fChoices);
        _fieldForm.Children.Add(_fChoicesRow);
        _fieldForm.Children.Add(_fieldMessage);
        _fieldForm.Visibility = Visibility.Collapsed;
        panel.Children.Add(_fieldForm);

        _fieldList.SelectionChanged += (_, _) =>
        {
            if (_updating)
                return;
            _fieldSelected = _fieldList.SelectedIndex;
            LoadFieldForm();
        };
        _fLabel.TextChanged += (_, _) => FieldEdit("label", _fLabel.Text, "f.label");
        _fDefault.TextChanged += (_, _) => FieldEdit("default", _fDefault.Text, "f.default");
        _fChoices.TextChanged += (_, _) => FieldEdit("choices", _fChoices.Text, "f.choices");
        _fRequired.Click += (_, _) => FieldEdit("required", _fRequired.IsChecked == true ? "true" : "", null);
        _fKind.SelectionChanged += (_, _) =>
        {
            FieldEdit("kind", _fKind.SelectedItem as string, null);
            _fChoicesRow.Visibility = _fKind.SelectedItem as string == "choice" ? Visibility.Visible : Visibility.Collapsed;
        };
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
    }

    private static TextBlock Label(string text) => new() { Text = text, Foreground = Brushes.DimGray, Margin = new Thickness(0, 8, 0, 2) };

    private void TemplateEdit(Action change)
    {
        if (_updating)
            return;
        Edit(change);
    }

    private void RefreshTemplateTab()
    {
        _updating = true;
        try
        {
            _tplTitle.Text = _model.GetTemplateProperty("title");
            _tplDescription.Text = _model.GetTemplateProperty("description");
            _tplGap.Text = _model.GetTemplateProperty("gap");
            _tplFrame.Text = _model.GetTemplateProperty("frame");
            var mode = _model.GetTemplateProperty("mode");
            _tplMode.SelectedItem = mode == "image" ? "image" : "text";
            _fieldList.Items.Clear();
            foreach (var name in _model.FieldNames)
                _fieldList.Items.Add(name);
            _fieldSelected = _model.FieldCount == 0 ? -1 : Math.Clamp(_fieldSelected, 0, _model.FieldCount - 1);
            _fieldList.SelectedIndex = _fieldSelected;
            LoadFieldForm();
        }
        finally
        {
            _updating = false;
        }
    }

    private void LoadFieldForm()
    {
        _fieldMessage.Text = "";
        if (_fieldSelected < 0 || _fieldSelected >= _model.FieldCount)
        {
            _fieldForm.Visibility = Visibility.Collapsed;
            return;
        }
        var wasUpdating = _updating;
        _updating = true;
        try
        {
            _fieldForm.Visibility = Visibility.Visible;
            _fLabel.Text = _model.GetFieldProperty(_fieldSelected, "label");
            var kind = _model.GetFieldProperty(_fieldSelected, "kind");
            _fKind.SelectedItem = _schema.FieldKinds.FirstOrDefault(k => k.Equals(kind, StringComparison.OrdinalIgnoreCase)) ?? "text";
            _fRequired.IsChecked = _model.GetFieldProperty(_fieldSelected, "required") == "true";
            _fDefault.Text = _model.GetFieldProperty(_fieldSelected, "default");
            _fChoices.Text = _model.GetFieldProperty(_fieldSelected, "choices");
            _fChoicesRow.Visibility = _fKind.SelectedItem as string == "choice" ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _updating = wasUpdating;
        }
    }

    private void FieldEdit(string name, string? value, string? key)
    {
        if (_updating || _fieldSelected < 0)
            return;
        Edit(() => _model.SetFieldProperty(_fieldSelected, name, value, key));
    }

    private void AddField()
    {
        var dialog = new NameDialog(this, "Nuevo campo", "Nombre del campo (letras, dígitos y guion bajo):", n => _model.CheckFieldName(n));
        if (dialog.ShowDialog() != true)
            return;
        _fieldSelected = _model.AddField(dialog.ChosenName);
    }

    private void DeleteField()
    {
        if (_fieldSelected < 0 || _fieldSelected >= _model.FieldCount)
            return;
        var name = _model.FieldNames[_fieldSelected];
        var usage = _model.FieldUsage(name);
        if (usage.Count > 0)
        {
            var where = string.Join(", ", usage.Where(u => u > 0).Select(u => $"bloque {u}").Concat(usage.Contains(0) ? ["el marco"] : []));
            var answer = MessageBox.Show(this, $"El campo «{name}» se usa en: {where}.\n\nSi lo borras, esos marcadores quedarán sin valor y la plantilla puede dejar de ser válida. ¿Borrarlo?",
                "Borrar campo", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
                return;
        }
        _model.RemoveField(_fieldSelected);
        _testData.Remove(name);
    }

    // ---- test data -------------------------------------------------------------------------------------

    private UIElement BuildTestDataTab() => new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _testPanel };

    private void InitTestData() => SyncTestData(force: true);

    private static string Example(string kind, string? def, string? firstChoice) => !string.IsNullOrEmpty(def) ? def : kind switch
    {
        "multiline" => "Línea 1\nLínea 2",
        "number" => "1",
        "choice" => firstChoice ?? "",
        "boolean" or "image" => "",
        _ => "Ejemplo",
    };

    /// <summary>Rebuilds the test-data form when the template's fields changed; keeps the values already typed.</summary>
    private void SyncTestData(bool force = false)
    {
        var signature = string.Join("|", Enumerable.Range(0, _model.FieldCount).Select(i => _model.Field(i).ToJsonString()));
        if (!force && signature == _fieldSignature)
            return;
        _fieldSignature = signature;

        var names = new HashSet<string>(_model.FieldNames, StringComparer.OrdinalIgnoreCase);
        foreach (var gone in _testData.Keys.Where(k => !names.Contains(k)).ToList())
            _testData.Remove(gone);

        _testPanel.Children.Clear();
        _testPanel.Children.Add(new TextBlock
        {
            Text = "Valores solo para la vista previa: no se guardan en la plantilla.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
        });
        if (_model.FieldCount == 0)
            _testPanel.Children.Add(new TextBlock { Text = "La plantilla no tiene campos.", Margin = new Thickness(0, 8, 0, 0) });

        for (var i = 0; i < _model.FieldCount; i++)
        {
            var name = _model.FieldNames[i];
            if (name.Length == 0)
                continue;
            var kind = _model.GetFieldProperty(i, "kind");
            if (kind == "image")
                continue;   // images are tried from the block itself
            var choices = _model.GetFieldProperty(i, "choices").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!_testData.ContainsKey(name))
                _testData[name] = Example(kind, _model.GetFieldProperty(i, "default"), choices.FirstOrDefault());

            var label = _model.GetFieldProperty(i, "label");
            var captured = name;
            if (kind == "boolean")
            {
                var check = new CheckBox { Content = label.Length > 0 ? label : name, IsChecked = _testData[name] == "true", Margin = new Thickness(0, 10, 0, 0) };
                check.Click += (_, _) => SetTest(captured, check.IsChecked == true ? "true" : "");
                _testPanel.Children.Add(check);
                continue;
            }
            _testPanel.Children.Add(Label(label.Length > 0 ? label : name));
            if (kind == "choice" && choices.Length > 0)
            {
                var combo = new ComboBox { ItemsSource = choices, SelectedItem = choices.Contains(_testData[name]) ? _testData[name] : choices[0] };
                combo.SelectionChanged += (_, _) => SetTest(captured, combo.SelectedItem as string ?? "");
                _testPanel.Children.Add(combo);
                continue;
            }
            var multiline = kind == "multiline";
            var box = new TextBox
            {
                Text = _testData[name],
                AcceptsReturn = multiline,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                Height = multiline ? 70 : double.NaN,
            };
            box.TextChanged += (_, _) => SetTest(captured, box.Text);
            _testPanel.Children.Add(box);
        }
    }

    private void SetTest(string name, string value)
    {
        if (_updating)
            return;
        _testData[name] = value;
        SchedulePreview();
    }

    private Dictionary<string, string> TestValues() =>
        _testData.Where(t => !string.IsNullOrEmpty(t.Value)).ToDictionary(t => t.Key, t => t.Value);

    // ---- raw JSON ------------------------------------------------------------------------------------

    private UIElement BuildRawTab()
    {
        var panel = new DockPanel { Margin = new Thickness(6) };
        DockPanel.SetDock(_rawStatus, Dock.Bottom);
        panel.Children.Add(_rawStatus);
        panel.Children.Add(_raw);
        _raw.TextChanged += (_, _) =>
        {
            if (_updatingRaw)
                return;
            _rawTimer.Stop();
            _rawTimer.Start();
        };
        _raw.LostKeyboardFocus += (_, _) =>
        {
            _rawTimer.Stop();
            ApplyRaw();
        };
        return panel;
    }

    /// <summary>Shows the model's JSON, unless the user is typing in the raw tab.</summary>
    private void UpdateRaw()
    {
        if (_raw.IsKeyboardFocusWithin)
            return;
        _updatingRaw = true;
        try
        {
            _raw.Text = _model.Json;
            _rawStatus.Text = "";
        }
        finally
        {
            _updatingRaw = false;
        }
    }

    private void ApplyRaw()
    {
        if (_updatingRaw)
            return;
        var error = _model.TryReplace(_raw.Text);
        _rawStatus.Foreground = error is null ? Brushes.DarkGreen : ErrorBrush;
        _rawStatus.Text = error ?? "JSON válido: aplicado.";
    }

    // ---- preview ------------------------------------------------------------------------------------

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private async Task RefreshPreviewAsync()
    {
        var mine = ++_sequence;
        try
        {
            var result = await _service.Client.PreviewDraftAsync(_model.Json, TestValues(), _draft);
            if (mine != _sequence)
                return;   // an older answer: the newest one wins
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(result.Png);
            image.EndInit();
            _preview.Source = image;
            _preview.Width = image.PixelWidth;   // real size: 1 dot = 1 device-independent pixel
            _preview.Height = image.PixelHeight;
            _overlay.Width = image.PixelWidth;
            _overlay.Height = image.PixelHeight;
            _rows = result.Blocks;
            var hadError = _error is not null;
            _error = null;
            SetStatus($"{image.PixelHeight / 8.0:0} mm de papel", error: false);
            if (hadError)
            {
                RefreshBlockList();
                ApplyErrorToProps();
            }
            UpdateHighlight();
        }
        catch (ControlApiException ex)
        {
            if (mine != _sequence)
                return;
            _error = new PreviewError(ex.Block, ex.Property, ex.Message);
            SetStatus(ex.Message, error: true);
            RefreshBlockList();
            ApplyErrorToProps();
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            if (mine == _sequence)
                SetStatus($"No se pudo actualizar la vista previa: {ex.Message}", error: true);
        }
    }

    private void UpdateHighlight()
    {
        var row = _rows.FirstOrDefault(r => r.Index == _selected + 1);
        if (row is null || _selected < 0)
        {
            _highlight.Visibility = Visibility.Collapsed;
            return;
        }
        Canvas.SetLeft(_highlight, 0);
        Canvas.SetTop(_highlight, row.Top);
        _highlight.Width = Math.Max(1, _overlay.Width > 0 ? _overlay.Width : 384);
        _highlight.Height = Math.Max(2, row.Height);
        _highlight.Visibility = Visibility.Visible;
    }

    private void OnPreviewClick(object sender, MouseButtonEventArgs e)
    {
        if (_rows.Count == 0)
            return;
        var y = e.GetPosition(_preview).Y;
        // The block under the click, or the nearest one when the click lands in a gap.
        var row = _rows.FirstOrDefault(r => y >= r.Top && y < r.Top + r.Height)
                  ?? _rows.OrderBy(r => Math.Min(Math.Abs(y - r.Top), Math.Abs(y - (r.Top + r.Height)))).First();
        if (row.Index - 1 < _model.BlockCount)
            SelectBlock(row.Index - 1);
    }

    // ---- draft, save and close -----------------------------------------------------------------------------

    private async Task EnsureDraftAsync()
    {
        if (_draft is not null)
            return;
        try
        {
            _draft = await _service.Client.CreateDraftAsync();
        }
        catch (Exception ex) when (ex is ControlApiException or HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            _draft = null;
            SetStatus($"No se pudo preparar el editor de imágenes: {ex.Message}", error: true);
        }
    }

    private void DiscardDraft()
    {
        var id = _draft;
        _draft = null;
        if (id is null)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                await _service.Client.DiscardDraftAsync(id);
            }
            catch (Exception)
            {
                // The service removes stale drafts by itself after 24 hours.
            }
        });
    }

    private async Task SaveAsAsync()
    {
        var dialog = new NameDialog(this, "Guardar como…", "Nombre de la nueva plantilla:", n =>
            !TemplateEditorModel.IsValidName(n) ? "Solo letras, dígitos, guion y guion bajo (máximo 40) y no una palabra reservada."
            : _existingNames.Contains(n, StringComparer.OrdinalIgnoreCase) ? $"Ya existe una plantilla llamada '{n}'."
            : null, initial: $"{_model.Name}-2");
        if (dialog.ShowDialog() == true)
            await SaveAsync(dialog.ChosenName);
    }

    private async Task<bool> SaveAsync(string? asName)
    {
        if (asName is not null && asName != _model.Name)
            Edit(() => _model.Rename(asName));
        var name = _model.Name;
        if (!TemplateEditorModel.IsValidName(name))
        {
            SetStatus($"El nombre '{name}' no es válido (letras, dígitos, guion y guion bajo; máximo 40).", error: true);
            return false;
        }
        if (!name.Equals(_originalName, StringComparison.OrdinalIgnoreCase) && _existingNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            SetStatus($"Ya existe una plantilla llamada '{name}'. Usa «Guardar como…» con otro nombre.", error: true);
            return false;
        }
        try
        {
            await _service.Client.SaveTemplateAsync(name, _model.Json, _draft);
            _draft = null;   // the service removed it when it saved
            _model.MarkSaved();
            SavedName = name;
            DialogResult = true;
            return true;
        }
        catch (ControlApiException ex)
        {
            _error = new PreviewError(ex.Block, ex.Property, ex.Message);
            SetStatus($"No se pudo guardar: {ex.Message}", error: true);
            RefreshBlockList();
            ApplyErrorToProps();
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            SetStatus($"No se pudo guardar: {ex.Message}", error: true);
            return false;
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DialogResult == true || _closing)
            return;
        if (_model.IsDirty)
        {
            var answer = MessageBox.Show(this, "Hay cambios sin guardar. ¿Quieres guardarlos antes de cerrar?", Title,
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (answer == MessageBoxResult.Yes)
            {
                e.Cancel = true;
                _ = SaveAsync(asName: null);   // closes the window itself when it succeeds
                return;
            }
        }
        _closing = true;
        _previewTimer.Stop();
        _rawTimer.Stop();
    }
}
