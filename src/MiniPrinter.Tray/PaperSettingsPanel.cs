using System.Globalization;
using MiniPrinter.Gui;
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
    private readonly Button _add = new() { Content = Strings.Get("Paper.Add") };
    private readonly Button _edit = new() { Content = Strings.Get("Paper.Edit") };
    private readonly Button _delete = new() { Content = Strings.Get("Paper.Delete") };
    private readonly Button _factory = new() { Content = Strings.Get("Paper.Factory") };
    private readonly TextBlock _message = Themed.Brush(new TextBlock { TextWrapping = TextWrapping.Wrap, MinHeight = 18 }, Themed.Error);
    private List<PaperSizeSetting> _sizes = [.. PaperCatalog.Presets];
    private string _defaultId = PaperCatalog.DefaultPresetId;
    private bool _rebuilding;

    public PaperSettingsPanel(Window owner, Panel host)
    {
        _owner = owner;
        host.Children.Add(Themed.Brush(new TextBlock
        {
            Text = Strings.Get("Paper.Intro"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        }, Themed.Muted));
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

    /// <summary>Raised when the user adds, edits, removes or switches sizes (not when sizes are loaded).</summary>
    public event Action<IReadOnlyList<PaperSizeSetting>, string>? Edited;

    private bool _loading;

    /// <summary>Shows these sizes.</summary>
    public void Load(IReadOnlyList<PaperSizeSetting> sizes, string defaultId)
    {
        if (sizes.SequenceEqual(_sizes) && defaultId == _defaultId)
            return;
        _sizes = [.. sizes];
        _defaultId = defaultId;
        Say("");
        _loading = true;
        Rebuild();
        _loading = false;
    }

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
        if (!_loading)
            Edited?.Invoke(_sizes, _defaultId);
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
            Content = Strings.Get("Paper.Default"),
            Margin = new Thickness(12, 0, 0, 0),
            ToolTip = size.Enabled ? Strings.Get("Paper.DefaultTip") : Strings.Get("Paper.DefaultDisabledTip"),
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

        var label = size.Preset ? size.Name : Strings.Get("Paper.OwnLabel", size.Name, size.WidthMm, size.LengthMm);
        row.Children.Add(Themed.Brush(new TextBlock
        {
            Text = (size.Id == _defaultId ? "● " : "") + label + (size.Preset ? "" : "  " + Strings.Get("Paper.Own")),
            VerticalAlignment = VerticalAlignment.Center,
        }, size.Enabled ? Themed.Normal : Themed.Disabled));
        return row;
    }

    private void SetEnabled(int index, bool enabled)
    {
        var size = _sizes[index];
        if (!enabled)
        {
            if (_sizes.Count(s => s.Enabled) <= 1)
            {
                Say(Strings.Get("Paper.NeedOne"));
                Rebuild();
                return;
            }
            if (size.Id == _defaultId)
            {
                Say(Strings.Get("Paper.IsDefault"));
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
            Say(Strings.Get("Paper.IsDefault"));
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
public sealed class PaperSizeDialog : ThemedWindow
{
    private readonly TextBox _name = new() { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _width = new() { Width = 70 };
    private readonly TextBox _length = new() { Width = 70 };
    private readonly TextBlock _info = Themed.Brush(new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MaxWidth = 360 }, Themed.Muted);
    private readonly TextBlock _error = Themed.Brush(new TextBlock { TextWrapping = TextWrapping.Wrap, MinHeight = 18, MaxWidth = 360 }, Themed.Error);
    private readonly Button _ok = new() { Content = Strings.Get("Dialog.Ok"), IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
    private readonly IReadOnlyList<PaperSizeSetting> _others;
    private readonly bool _enabled;

    /// <summary>The size entered, when the dialog was accepted.</summary>
    public PaperSizeSetting? Result { get; private set; }

    public PaperSizeDialog(Window owner, PaperSizeSetting? existing, IReadOnlyList<PaperSizeSetting> others)
    {
        Owner = owner;
        Title = existing is null ? Strings.Get("Paper.AddTitle") : Strings.Get("Paper.EditTitle");
        _others = others;
        _enabled = existing?.Enabled ?? true;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = Strings.Get("Paper.Name") });
        panel.Children.Add(_name);
        var sizes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        sizes.Children.Add(new StackPanel { Children = { new TextBlock { Text = Strings.Get("Paper.Width", PaperCatalog.MinWidthMm, PaperCatalog.MaxWidthMm) }, _width }, Margin = new Thickness(0, 0, 16, 0) });
        sizes.Children.Add(new StackPanel { Children = { new TextBlock { Text = Strings.Get("Paper.Length", PaperCatalog.MinLengthMm, PaperCatalog.MaxLengthMm) }, _length } });
        panel.Children.Add(sizes);
        panel.Children.Add(_info);
        panel.Children.Add(_error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(_ok);
        buttons.Children.Add(new Button { Content = Strings.Get("Dialog.Cancel"), IsCancel = true, MinWidth = 80 });
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
            problem = Strings.Get("Paper.NotWholeNumbers");
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
            ? Strings.Get("Paper.SideMargins", size.SideMarginMm.ToString("0.##", CultureInfo.CurrentCulture))
            : "";
    }
}
