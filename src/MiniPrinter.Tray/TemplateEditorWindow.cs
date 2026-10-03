using System.IO;
using System.Windows;
using MiniPrinter.Gui;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MiniPrinter.Control;

namespace MiniPrinter.Tray;

/// <summary>
/// The template editor (design.md D7): block list, exact live preview with click-to-select, properties
/// generated from the service's schema, template properties and fields, test data and the raw JSON.
/// Everything edits one <see cref="TemplateEditorModel"/>; the window is built in code so the controls can
/// follow the schema.
/// </summary>
public sealed partial class TemplateEditorWindow : ThemedWindow
{
    private static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RawDelay = TimeSpan.FromMilliseconds(800);

    private readonly ServiceConnection _service;
    private readonly TemplateEditorModel _model;
    private readonly TemplateSchemaDto _schema;
    private readonly IReadOnlyCollection<string> _existingNames;
    private readonly bool _existing;
    private string _source;

    private readonly DispatcherTimer _previewTimer = new() { Interval = PreviewDelay };
    private readonly DispatcherTimer _rawTimer = new() { Interval = RawDelay };

    // controls
    private readonly ListBox _blockList = new() { MinHeight = 160 };
    private readonly StackPanel _props = new() { Margin = new Thickness(8) };
    private readonly TextBlock _blockTitle = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _blockError = Themed.Brush(new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) }, Themed.Error);
    private readonly Image _preview = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Rectangle _highlight = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0x09, 0x69, 0xDA)),   // theme-ok: highlight over the white paper of the preview
        StrokeThickness = 2,
        StrokeDashArray = [4, 3],
        Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x09, 0x69, 0xDA)),   // theme-ok: highlight over the white paper of the preview
        Visibility = Visibility.Collapsed,
    };
    private readonly TextBlock _status = Themed.Brush(new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) }, Themed.Muted);
    private readonly Button _undoButton = new() { Content = Strings.Get("Editor.Undo"), ToolTip = "Ctrl+Z" };   // i18n-ok: keyboard shortcut
    private readonly Button _redoButton = new() { Content = Strings.Get("Editor.Redo"), ToolTip = "Ctrl+Y" };   // i18n-ok: keyboard shortcut
    private readonly Button _addButton = new() { Content = Strings.Get("Editor.AddBlock") };
    private readonly Button _duplicateButton = new() { Content = Strings.Get("Editor.Duplicate") };
    private readonly Button _deleteButton = new() { Content = Strings.Get("Editor.Delete") };
    private readonly Button _upButton = new() { Content = "↑", ToolTip = Strings.Get("Editor.Up") };
    private readonly Button _downButton = new() { Content = "↓", ToolTip = Strings.Get("Editor.Down") };

    // state
    private string? _draft;
    private int _selected = -1;
    private int _sequence;
    private bool _internalEdit;
    private bool _updating;
    private IReadOnlyList<BlockRowsDto> _rows = [];
    private PreviewError? _error;
    private readonly Dictionary<string, (System.Windows.Controls.Control Box, TextBlock Message)> _propControls = [];
    private bool _closing;

    /// <summary>Name of the template saved by this window, or null if nothing was saved.</summary>
    public string? SavedName { get; private set; }

    private sealed record PreviewError(int? Block, string? Property, string Message);

    public TemplateEditorWindow(Window owner, ServiceConnection service, TemplateSchemaDto schema, string json,
        bool existing, string source, IReadOnlyCollection<string> existingNames)
    {
        Owner = owner;
        _service = service;
        _schema = schema;
        _model = new TemplateEditorModel(json, schema);
        _existing = existing;
        _source = source;
        _existingNames = existingNames;

        Title = Strings.Get(existing ? "Editor.TitleEdit" : "Editor.TitleNew", _model.Name);
        Width = 1280;
        Height = 820;
        MinWidth = 1000;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildLayout();

        InitTestData();
        _previewTimer.Tick += async (_, _) =>
        {
            _previewTimer.Stop();
            await RefreshPreviewAsync();
        };
        _rawTimer.Tick += (_, _) =>
        {
            _rawTimer.Stop();
            ApplyRaw();
        };
        _model.Changed += OnModelChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += async (_, _) =>
        {
            await EnsureDraftAsync();
            RefreshAll();
        };
        Closing += OnClosing;
        Closed += (_, _) => DiscardDraft();
        RefreshAll();
    }

    // ---- layout -----------------------------------------------------------------------------------

    private UIElement BuildLayout()
    {
        var root = new DockPanel();

        // Toolbar and banners
        var top = new StackPanel();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        var save = new Button { Content = Strings.Get("Editor.Save"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 4, 14, 4) };
        save.Click += async (_, _) => await SaveAsync(asName: null);
        var saveAs = new Button { Content = Strings.Get("Editor.SaveAs"), Margin = new Thickness(0, 0, 16, 0), Padding = new Thickness(10, 4, 10, 4) };
        saveAs.Click += async (_, _) => await SaveAsAsync();
        _undoButton.Click += (_, _) => _model.Undo();
        _redoButton.Click += (_, _) => _model.Redo();
        _undoButton.Margin = new Thickness(0, 0, 4, 0);
        foreach (var b in new Button[] { save, saveAs, _undoButton, _redoButton })
            bar.Children.Add(b);
        top.Children.Add(bar);
        if (_source == TemplateEditorSources.BuiltIn)
            top.Children.Add(Banner(Strings.Get("Editor.BuiltInBanner")));
        if (_model.HadComments)
            top.Children.Add(Banner(Strings.Get("Editor.CommentsBanner")));
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        // Three columns: left tabs, preview, properties
        var grid = new Grid { Margin = new Thickness(8, 0, 8, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 440 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });

        var left = BuildLeftTabs();
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var center = BuildPreviewPane();
        Grid.SetColumn(center, 1);
        grid.Children.Add(center);

        var right = Themed.Frame(new Border { BorderThickness = new Thickness(1), Margin = new Thickness(8, 0, 0, 0) }, null, Themed.Border);
        var rightPanel = new DockPanel();
        var header = new StackPanel { Margin = new Thickness(8, 8, 8, 0) };
        header.Children.Add(_blockTitle);
        header.Children.Add(_blockError);
        DockPanel.SetDock(header, Dock.Top);
        rightPanel.Children.Add(header);
        rightPanel.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _props });
        right.Child = rightPanel;
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        root.Children.Add(grid);
        return root;
    }

    private static Border Banner(string text) => Themed.Frame(new Border
    {
        BorderThickness = new Thickness(1),
        Margin = new Thickness(8, 0, 8, 6),
        Padding = new Thickness(8, 4, 8, 4),
        Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
    }, Themed.WarnBackground, Themed.WarnBorder);

    private UIElement BuildPreviewPane()
    {
        var host = new Grid { Width = 384, HorizontalAlignment = HorizontalAlignment.Left };
        host.SetResourceReference(Panel.BackgroundProperty, Themed.Preview);   // the paper: white in every theme
        host.Children.Add(_preview);
        _overlay.Children.Add(_highlight);
        host.Children.Add(_overlay);
        _preview.MouseLeftButtonDown += OnPreviewClick;
        _preview.Cursor = Cursors.Hand;
        RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.NearestNeighbor);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new Border { Margin = new Thickness(12), Child = host },
        };
        var border = Themed.Frame(new Border { BorderThickness = new Thickness(1), Child = scroll }, Themed.SurfaceAlt, Themed.Border);

        var pane = new DockPanel { Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(_status, Dock.Bottom);
        pane.Children.Add(_status);
        pane.Children.Add(border);
        return pane;
    }

    private TabControl BuildLeftTabs()
    {
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = Strings.Get("Editor.TabBlocks"), Content = BuildBlocksTab() });
        tabs.Items.Add(new TabItem { Header = Strings.Get("Editor.TabTemplate"), Content = BuildTemplateTab() });
        tabs.Items.Add(new TabItem { Header = Strings.Get("Editor.TabTestData"), Content = BuildTestDataTab() });
        tabs.Items.Add(new TabItem { Header = Strings.Get("Editor.TabJson"), Content = BuildRawTab() });
        return tabs;
    }

    // ---- blocks tab --------------------------------------------------------------------------------

    private UIElement BuildBlocksTab()
    {
        var panel = new DockPanel { Margin = new Thickness(6) };
        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var b in new Button[] { _addButton, _duplicateButton, _deleteButton, _upButton, _downButton })
        {
            b.Margin = new Thickness(0, 0, 4, 4);
            b.Padding = new Thickness(8, 3, 8, 3);
            buttons.Children.Add(b);
        }
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);

        _blockList.AllowDrop = true;
        _blockList.SelectionChanged += (_, _) =>
        {
            if (_updating)
                return;
            _selected = _blockList.SelectedIndex;
            BuildProps();
            UpdateHighlight();
            UpdateButtons();
        };
        _blockList.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(null);
        _blockList.PreviewMouseMove += OnBlockDragMove;
        _blockList.Drop += OnBlockDrop;
        panel.Children.Add(_blockList);

        _addButton.Click += (_, _) => ShowAddMenu();
        _duplicateButton.Click += (_, _) =>
        {
            if (_selected >= 0 && _model.CanAddBlock)
                SelectBlock(_model.DuplicateBlock(_selected));
        };
        _deleteButton.Click += (_, _) =>
        {
            if (_selected < 0)
                return;
            var index = _selected;
            _model.RemoveBlock(index);
            SelectBlock(Math.Min(index, _model.BlockCount - 1));
        };
        _upButton.Click += (_, _) => MoveSelected(-1);
        _downButton.Click += (_, _) => MoveSelected(1);
        return panel;
    }

    private Point _dragStart;

    private void ShowAddMenu()
    {
        var menu = new ContextMenu { PlacementTarget = _addButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var block in _schema.Blocks)
        {
            var item = new MenuItem { Header = block.Title };
            var type = block.Type;
            item.Click += (_, _) => SelectBlock(_model.AddBlock(type, _selected >= 0 ? _selected + 1 : null));
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void MoveSelected(int delta)
    {
        if (_selected < 0)
            return;
        var to = _selected + delta;
        if (to < 0 || to >= _model.BlockCount)
            return;
        _model.MoveBlock(_selected, to);
        SelectBlock(to);
    }

    private void OnBlockDragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _selected < 0)
            return;
        var now = e.GetPosition(null);
        if (Math.Abs(now.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(now.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        if (ItemAt(e.OriginalSource as DependencyObject) is null)
            return;
        DragDrop.DoDragDrop(_blockList, new DataObject("miniprinter-block", _selected), DragDropEffects.Move);
    }

    private void OnBlockDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("miniprinter-block"))
            return;
        var from = (int)e.Data.GetData("miniprinter-block")!;
        var target = ItemAt(e.OriginalSource as DependencyObject);
        var to = target is null ? _model.BlockCount - 1 : _blockList.ItemContainerGenerator.IndexFromContainer(target);
        if (to < 0)
            return;
        _model.MoveBlock(from, to);
        SelectBlock(to);
    }

    private ListBoxItem? ItemAt(DependencyObject? source)
    {
        while (source is not null and not ListBoxItem)
            source = VisualTreeHelper.GetParent(source);
        return source as ListBoxItem;
    }

    private void SelectBlock(int index)
    {
        _selected = _model.BlockCount == 0 ? -1 : Math.Clamp(index, 0, _model.BlockCount - 1);
        _updating = true;
        _blockList.SelectedIndex = _selected;
        _updating = false;
        BuildProps();
        UpdateHighlight();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var has = _selected >= 0;
        _duplicateButton.IsEnabled = has && _model.CanAddBlock;
        _deleteButton.IsEnabled = has;
        _upButton.IsEnabled = has && _selected > 0;
        _downButton.IsEnabled = has && _selected < _model.BlockCount - 1;
        _addButton.IsEnabled = _model.CanAddBlock;
        _undoButton.IsEnabled = _model.CanUndo;
        _redoButton.IsEnabled = _model.CanRedo;
    }

    private void RefreshBlockList()
    {
        _updating = true;
        _blockList.Items.Clear();
        for (var i = 0; i < _model.BlockCount; i++)
        {
            var type = _model.BlockType(i);
            var title = _schema.Blocks.FirstOrDefault(b => b.Type == type)?.Title ?? type;
            var broken = _error?.Block == i + 1;
            var text = Themed.Brush(new TextBlock
            {
                Text = $"{i + 1}. {(broken ? "⚠ " : "")}{title}{Summary(i)}",   // i18n-ok: list item built from the title and the summary
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = broken ? _error!.Message : null,
            }, broken ? Themed.Error : Themed.Normal);
            _blockList.Items.Add(text);
        }
        _selected = _model.BlockCount == 0 ? -1 : Math.Clamp(_selected, 0, _model.BlockCount - 1);
        _blockList.SelectedIndex = _selected;
        _updating = false;
        UpdateButtons();
    }

    private string Summary(int index)
    {
        foreach (var name in new[] { "value", "data", "items", "left", "source", "style", "mm" })
        {
            var value = _model.GetBlockProperty(index, name).Replace("\r", "").Split('\n')[0];
            if (value.Length > 0)
                return " — " + (value.Length > 32 ? value[..32] + "…" : value);
        }
        return "";
    }

    // ---- model changes -----------------------------------------------------------------------------

    private void OnModelChanged()
    {
        if (_closing)
            return;
        if (_internalEdit)
        {
            // Typing in a property control: keep that control (and its focus); refresh the rest.
            RefreshBlockList();
            UpdateRaw();
            SchedulePreview();
            return;
        }
        RefreshAll();
    }

    private void RefreshAll()
    {
        RefreshBlockList();
        BuildProps();
        UpdateRaw();
        RefreshTemplateTab();
        SyncTestData();
        UpdateHighlight();
        SchedulePreview();
    }

    /// <summary>Applies a change made by a property control without rebuilding that control.</summary>
    private void Edit(Action change)
    {
        _internalEdit = true;
        try
        {
            change();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            SetStatus(ex.Message, error: true);
        }
        finally
        {
            _internalEdit = false;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_raw.IsKeyboardFocusWithin || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;
        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            _model.Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y || (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) != 0))
        {
            _model.Redo();
            e.Handled = true;
        }
    }

    private void SetStatus(string text, bool error)
    {
        _status.Text = text;
        Themed.Foreground(_status, error ? Themed.Error : Themed.Muted);
    }
}

/// <summary>The origins a template can have, as the service reports them.</summary>
internal static class TemplateEditorSources
{
    public const string BuiltIn = "builtin";
    public const string User = "user";
    public const string Override = "override";
}
