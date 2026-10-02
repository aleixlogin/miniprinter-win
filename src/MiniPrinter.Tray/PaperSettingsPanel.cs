using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MiniPrinter.Control;

namespace MiniPrinter.Tray;

/// <summary>
/// The "Papel que se muestra a Windows" section of the settings (spec paper-sizes-settings): a list of sizes with
/// an active box and a default, own sizes (add, edit, delete) and the rules that keep at least one active size.
/// It is built in code inside a host panel so the XAML stays small.
/// </summary>
public sealed class PaperSettingsPanel
{
    private readonly Window _owner;
    private readonly ListBox _list = new() { Height = 190, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Button _add = new() { Content = "Añadir…" };
    private readonly Button _edit = new() { Content = "Editar…" };
    private readonly Button _delete = new() { Content = "Borrar" };
    private readonly Button _factory = new() { Content = "Restaurar valores de fábrica" };
    private readonly TextBlock _message = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, MinHeight = 18 };
    private List<PaperSizeSetting> _sizes = [.. PaperCatalog.Presets];
    private string _defaultId = PaperCatalog.DefaultPresetId;
    private bool _rebuilding;

    public PaperSettingsPanel(Window owner, Panel host)
    {
        _owner = owner;
        host.Children.Add(new TextBlock
        {
            Text = "Elige qué tamaños de papel ve Windows, y cuál es el tamaño por defecto. La marca ● indica el por defecto.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x57, 0x60, 0x6A)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        });
        host.Children.Add(_list);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var b in new[] { _add, _edit, _delete, _factory })
        {
            b.Margin = new Thickness(0, 0, 6, 0);
            b.Padding = new Thickness(10, 3, 10, 3);
            buttons.Children.Add(b);
        }
        host.Children.Add(buttons);
        host.Children.Add(_message);

        _add.Click += (_, _) => AddOrEdit(null);
        _edit.Click += (_, _) => AddOrEdit(SelectedIndex);
        _delete.Click += (_, _) => Delete();
        _factory.Click += (_, _) =>
        {
            _sizes = [.. PaperCatalog.Presets];
            _defaultId = PaperCatalog.DefaultPresetId;
            Say("");
            Rebuild();
        };
        _list.SelectionChanged += (_, _) =>
        {
            if (!_rebuilding)
                UpdateButtons();
        };
        Rebuild();
    }

    private int SelectedIndex => _list.SelectedIndex;

    /// <summary>Shows the sizes of the given settings.</summary>
    public void Load(ServiceSettings settings)
    {
        _sizes = [.. PaperCatalog.All(settings)];
        _defaultId = PaperCatalog.DefaultId(settings);
        Say("");
        Rebuild();
    }

    /// <summary>The settings with the sizes shown here (a null list when they are exactly the factory ones).</summary>
    public ServiceSettings Apply(ServiceSettings settings) => settings with
    {
        PaperSizes = _sizes.SequenceEqual(PaperCatalog.Presets) ? null : _sizes,
        DefaultPaperId = _defaultId,
    };

    // ---- list ------------------------------------------------------------------------------------------------

    private void Rebuild()
    {
        _rebuilding = true;
        var selected = SelectedIndex;
        _list.Items.Clear();
        for (var i = 0; i < _sizes.Count; i++)
            _list.Items.Add(Row(i));
        _list.SelectedIndex = selected >= 0 && selected < _sizes.Count ? selected : (_sizes.Count > 0 ? 0 : -1);
        _rebuilding = false;
        UpdateButtons();
    }

    private UIElement Row(int index)
    {
        var size = _sizes[index];
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 1, 0, 1) };

        var isDefault = new RadioButton
        {
            GroupName = "paper-default",
            IsChecked = size.Id == _defaultId,
            IsEnabled = size.Enabled,
            Content = "por defecto",
            Margin = new Thickness(12, 0, 0, 0),
            ToolTip = size.Enabled ? "Tamaño por defecto" : "Activa el tamaño para poder elegirlo por defecto",
        };
        isDefault.Click += (_, _) =>
        {
            _defaultId = size.Id;
            Say("");
            Rebuild();
        };
        DockPanel.SetDock(isDefault, Dock.Right);
        row.Children.Add(isDefault);

        var enabled = new CheckBox { IsChecked = size.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        enabled.Click += (_, _) => SetEnabled(index, enabled.IsChecked == true);
        DockPanel.SetDock(enabled, Dock.Left);
        row.Children.Add(enabled);

        var label = size.Preset ? size.Name : $"{size.Name} — {size.WidthMm} × {size.LengthMm} mm";
        row.Children.Add(new TextBlock
        {
            Text = (size.Id == _defaultId ? "● " : "") + label + (size.Preset ? "" : "  (propio)"),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = size.Enabled ? Brushes.Black : Brushes.Gray,
        });
        return row;
    }

    private void SetEnabled(int index, bool enabled)
    {
        var size = _sizes[index];
        if (!enabled)
        {
            if (_sizes.Count(s => s.Enabled) <= 1)
            {
                Say("Debe haber al menos un tamaño activo.");
                Rebuild();
                return;
            }
            if (size.Id == _defaultId)
            {
                Say("Es el tamaño por defecto: elige antes otro tamaño por defecto.");
                Rebuild();
                return;
            }
        }
        _sizes[index] = size with { Enabled = enabled };
        Say("");
        Rebuild();
    }

    private void UpdateButtons()
    {
        var index = SelectedIndex;
        var own = index >= 0 && index < _sizes.Count && !_sizes[index].Preset;
        _edit.IsEnabled = own;
        _delete.IsEnabled = own;
        _add.IsEnabled = _sizes.Count < PaperCatalog.MaxSizes;
    }

    private void Delete()
    {
        var index = SelectedIndex;
        if (index < 0 || _sizes[index].Preset)
            return;
        if (_sizes[index].Id == _defaultId)
        {
            Say("Es el tamaño por defecto: elige antes otro tamaño por defecto.");
            return;
        }
        _sizes.RemoveAt(index);
        Say("");
        Rebuild();
    }

    private void AddOrEdit(int? index)
    {
        var current = index is { } i && i >= 0 && i < _sizes.Count ? _sizes[i] : null;
        if (index is not null && (current is null || current.Preset))
            return;
        var others = _sizes.Where((_, n) => n != index).ToList();
        var dialog = new PaperSizeDialog(_owner, current, others);
        if (dialog.ShowDialog() != true || dialog.Result is not { } result)
            return;
        if (index is { } at)
        {
            if (_sizes[at].Id == _defaultId)
                _defaultId = result.Id;
            _sizes[at] = result;
        }
        else
        {
            _sizes.Add(result);
        }
        Say("");
        Rebuild();
        _list.SelectedIndex = _sizes.FindIndex(s => s.Id == result.Id);
    }

    private void Say(string text) => _message.Text = text;

    // ---- Windows default printer (per user, so the tray does it) ---------------------------------------------------

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDefaultPrinter(string name);

    /// <summary>True when the queue exists and is this user's default printer.</summary>
    public static bool IsDefaultPrinter(string name)
    {
        try
        {
            var settings = new System.Drawing.Printing.PrinterSettings { PrinterName = name };
            return settings.IsValid && settings.IsDefaultPrinter;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static bool MakeDefaultPrinter(string name) => SetDefaultPrinter(name);
}

/// <summary>Dialog to add or edit one of the user's own sizes, with live validation and the margins it will announce.</summary>
public sealed class PaperSizeDialog : Window
{
    private readonly TextBox _name = new() { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _width = new() { Width = 70 };
    private readonly TextBox _length = new() { Width = 70 };
    private readonly TextBlock _info = new() { Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MaxWidth = 360 };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, MinHeight = 18, MaxWidth = 360 };
    private readonly Button _ok = new() { Content = "Aceptar", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
    private readonly IReadOnlyList<PaperSizeSetting> _others;
    private readonly bool _enabled;

    /// <summary>The size entered, when the dialog was accepted.</summary>
    public PaperSizeSetting? Result { get; private set; }

    public PaperSizeDialog(Window owner, PaperSizeSetting? existing, IReadOnlyList<PaperSizeSetting> others)
    {
        Owner = owner;
        Title = existing is null ? "Añadir tamaño de papel" : "Editar tamaño de papel";
        _others = others;
        _enabled = existing?.Enabled ?? true;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Nombre" });
        panel.Children.Add(_name);
        var sizes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        sizes.Children.Add(new StackPanel { Children = { new TextBlock { Text = $"Ancho ({PaperCatalog.MinWidthMm}–{PaperCatalog.MaxWidthMm} mm)" }, _width }, Margin = new Thickness(0, 0, 16, 0) });
        sizes.Children.Add(new StackPanel { Children = { new TextBlock { Text = $"Largo ({PaperCatalog.MinLengthMm}–{PaperCatalog.MaxLengthMm} mm)" }, _length } });
        panel.Children.Add(sizes);
        panel.Children.Add(_info);
        panel.Children.Add(_error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(_ok);
        buttons.Children.Add(new Button { Content = "Cancelar", IsCancel = true, MinWidth = 80 });
        panel.Children.Add(buttons);
        Content = panel;

        _name.Text = existing?.Name ?? "";
        _width.Text = (existing?.WidthMm ?? PaperCatalog.HeadWidthMm).ToString(CultureInfo.InvariantCulture);
        _length.Text = (existing?.LengthMm ?? 100).ToString(CultureInfo.InvariantCulture);
        _name.TextChanged += (_, _) => Validate();
        _width.TextChanged += (_, _) => Validate();
        _length.TextChanged += (_, _) => Validate();
        _ok.Click += (_, _) =>
        {
            if (Parse(out var size, out _))
            {
                Result = size;
                DialogResult = true;
            }
        };
        Validate();
        Loaded += (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
        };
    }

    private bool Parse(out PaperSizeSetting size, out string? problem)
    {
        size = null!;
        if (!int.TryParse(_width.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(_length.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var length))
        {
            problem = "El ancho y el largo deben ser números enteros de milímetros.";
            return false;
        }
        var name = _name.Text.Trim();
        size = new PaperSizeSetting(PaperCatalog.CustomId(width, length), name, width, length, _enabled);
        problem = PaperCatalog.Problem([.. _others, size]);
        if (problem is not null && problem.StartsWith("Debe haber", StringComparison.Ordinal))
            problem = null;   // other sizes decide that rule, not this dialog
        return problem is null;
    }

    private void Validate()
    {
        var ok = Parse(out var size, out var problem);
        _error.Text = problem ?? "";
        _ok.IsEnabled = ok;
        _info.Text = ok && size.SideMarginMm > 0
            ? $"Se anunciarán márgenes laterales de {size.SideMarginMm.ToString("0.##", CultureInfo.CurrentCulture)} mm, de modo que el área imprimible sea de 48 mm."
            : "";
    }
}
