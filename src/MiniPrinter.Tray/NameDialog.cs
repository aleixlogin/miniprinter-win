using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MiniPrinter.Gui;

namespace MiniPrinter.Tray;

/// <summary>
/// Small dialog that asks for a name (new template, Save as…, new field) and validates it as the user types.
/// With <c>duplicateLabel</c> it also offers "blank" or "duplicate the selected template".
/// </summary>
public sealed class NameDialog : ThemedWindow
{
    private readonly TextBox _name = new() { MinWidth = 280, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock _error = Themed.Brush(new TextBlock { TextWrapping = TextWrapping.Wrap, MinHeight = 18 }, Themed.Error);
    private readonly Button _ok = new() { Content = Strings.Get("Dialog.Ok"), IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
    private readonly RadioButton? _blank;
    private readonly RadioButton? _copy;
    private readonly Func<string, string?> _validate;

    /// <param name="validate">Returns why a name is not acceptable, or null when it is.</param>
    /// <param name="duplicateLabel">When given, shows "En blanco" / "Duplicar «label»" choices.</param>
    public NameDialog(Window owner, string title, string prompt, Func<string, string?> validate, string? initial = null, string? duplicateLabel = null)
    {
        Owner = owner;
        Title = title;
        _validate = validate;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
        panel.Children.Add(_name);
        if (duplicateLabel is not null)
        {
            _blank = new RadioButton { Content = Strings.Get("Name.Blank"), IsChecked = true, Margin = new Thickness(0, 6, 0, 2) };
            _copy = new RadioButton { Content = Strings.Get("Name.Duplicate", duplicateLabel), Margin = new Thickness(0, 2, 0, 6) };
            panel.Children.Add(_blank);
            panel.Children.Add(_copy);
        }
        panel.Children.Add(_error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(_ok);
        buttons.Children.Add(new Button { Content = Strings.Get("Dialog.Cancel"), IsCancel = true, MinWidth = 80 });
        panel.Children.Add(buttons);
        Content = panel;

        _name.TextChanged += (_, _) => Validate();
        _ok.Click += (_, _) => DialogResult = true;
        _name.Text = initial ?? "";
        Validate();
        Loaded += (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
        };
    }

    public string ChosenName => _name.Text.Trim();

    /// <summary>True when "Duplicar" was chosen.</summary>
    public bool Duplicate => _copy?.IsChecked == true;

    private void Validate()
    {
        var problem = _name.Text.Trim().Length == 0 ? " " : _validate(_name.Text.Trim());
        _error.Text = problem?.Trim() ?? "";
        _ok.IsEnabled = problem is null;
    }
}
