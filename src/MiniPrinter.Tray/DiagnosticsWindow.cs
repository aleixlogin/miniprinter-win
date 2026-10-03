using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using MiniPrinter.Gui;

namespace MiniPrinter.Tray;

/// <summary>
/// The diagnostics: a traffic light per check (service, Bluetooth, pairing, connection, paper, Windows printer, listeners, fonts),
/// what each result means and a button that leads to the fix. The report can be copied to share it, without secrets.
/// </summary>
public sealed class DiagnosticsWindow : ThemedWindow
{
    private readonly DiagnosticsViewModel _model;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _summary = new() { FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
    private readonly Button _again = new() { Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), Content = Strings.Get("Diag.Run") };
    private readonly Button _copy = new() { Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), Content = Strings.Get("Diag.Copy") };
    private readonly Func<DiagnosticsReportContext> _reportContext;
    private readonly Action<DiagnosticAction> _perform;

    /// <param name="reportContext">Gathers what the report says about this machine when it is copied.</param>
    /// <param name="perform">Leads to the fix of a row (opens the tab, the settings or the settings of Windows).</param>
    public DiagnosticsWindow(Window owner, DiagnosticsViewModel model, Func<DiagnosticsReportContext> reportContext, Action<DiagnosticAction> perform)
    {
        Owner = owner;
        _model = model;
        _reportContext = reportContext;
        _perform = perform;
        Title = Strings.Get("Diag.Title");
        Width = 640;
        Height = 620;
        MinWidth = 460;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var close = new Button { Content = Strings.Get("Diag.Close"), Padding = new Thickness(14, 4, 14, 4), IsCancel = true };
        close.Click += (_, _) => Close();
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        Themed.Brush(status, Themed.Muted);

        var buttons = new DockPanel { Margin = new Thickness(14) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(_again);
        right.Children.Add(_copy);
        right.Children.Add(close);
        DockPanel.SetDock(right, Dock.Left);
        buttons.Children.Add(right);
        buttons.Children.Add(status);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var body = new StackPanel { Margin = new Thickness(18, 16, 18, 8) };
        body.Children.Add(_summary);
        body.Children.Add(_rows);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body });
        Content = root;

        _again.Click += async (_, _) => await RunAsync();
        _copy.Click += (_, _) =>
        {
            Clipboard.SetText(DiagnosticsReport.Build(_reportContext(), [.. _model.Rows]));
            status.Text = Strings.Get("Diag.Copied");
        };
        _model.Rows.CollectionChanged += (_, _) => Rebuild();
        Loaded += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        _again.IsEnabled = false;
        _copy.IsEnabled = false;
        _summary.Text = Strings.Get("Diag.Checking");
        Themed.Foreground(_summary, Themed.Muted);
        await _model.RunAsync();
        _again.IsEnabled = true;
        _copy.IsEnabled = true;
        _summary.Text = _model.Summary;
        Themed.Foreground(_summary, BrushOf(_model.OverallStatus));
    }

    private static string BrushOf(string status) => status switch
    {
        MiniPrinter.Control.DiagnosticStatus.Ok => Themed.Ok,
        MiniPrinter.Control.DiagnosticStatus.Warn => Themed.Warn,
        MiniPrinter.Control.DiagnosticStatus.Fail => Themed.Error,
        _ => Themed.Muted,
    };

    private void Rebuild()
    {
        _rows.Children.Clear();
        foreach (var row in _model.Rows)
            _rows.Children.Add(RowView(row));
    }

    private UIElement RowView(DiagnosticRow row)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new TextBlock { Text = row.Glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe UI Symbol"), FontSize = 22, Width = 34, VerticalAlignment = VerticalAlignment.Top };
        Themed.Foreground(glyph, BrushOf(row.Status));
        grid.Children.Add(glyph);

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = row.Title, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = row.Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        if (row.Hint.Length > 0)
            text.Children.Add(Themed.Brush(new TextBlock { Text = row.Hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) }, Themed.Muted));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (row.HasAction)
        {
            var button = new Button { Content = row.ActionText, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            button.Click += (_, _) => _perform(row.Action);
            Grid.SetColumn(button, 2);
            grid.Children.Add(button);
        }
        return grid;
    }
}
