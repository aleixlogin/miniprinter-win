using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using MiniPrinter.Control;
using MiniPrinter.Gui;

namespace MiniPrinter.Tray;

public partial class MainWindow : ThemedWindow
{
    private readonly ServiceConnection _service;
    private readonly BluetoothScanner _scanner = new();
    private readonly ObservableCollection<FoundDevice> _devices = [];
    private readonly TemplatesPanel _templates;
    private bool _wizard;

    public MainWindow(ServiceConnection service)
    {
        _service = service;
        InitializeComponent();
        var app = (App)Application.Current;
        ApplyMinimumScale(TextSizes.MinimumScale(app.Preferences.TextSize));
        WindowStateHelper.Restore(this, app.Preferences);
        Tabs.SelectedIndex = Math.Clamp(app.Preferences.Tab, 0, Tabs.Items.Count - 1);
        _saveWindow.Tick += (_, _) =>
        {
            _saveWindow.Stop();
            SaveWindowState();
        };
        SizeChanged += (_, _) => RestartSaveTimer();
        LocationChanged += (_, _) => RestartSaveTimer();
        Tabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource == Tabs)
                RestartSaveTimer();
        };
        Closing += (_, _) => SaveWindowState();
        InitSettings();
        InitStatus();
        DevicesList.ItemsSource = _devices;
        _templates = new TemplatesPanel(service, new TemplatesUi(TemplateCombo, TemplateForm, TemplatePreview, TemplateStatus,
            FavoriteCombo, TemplateCopies, TemplateCsvClear, TemplateCreate, TemplateEdit, TemplateDelete, this));
        Tabs.SelectionChanged += async (_, e) =>
        {
            if (e.OriginalSource == Tabs && Tabs.SelectedItem == TemplatesTab)
                await _templates.LoadAsync();
        };
        InitTemplates();
        _scanner.DeviceFound += d => Dispatcher.BeginInvoke(() => OnDeviceFound(d));
        _scanner.DeviceRemoved += id => Dispatcher.BeginInvoke(() =>
        {
            var existing = _devices.FirstOrDefault(x => x.Id == id);
            if (existing is not null) _devices.Remove(existing);
        });
        _scanner.Completed += () => Dispatcher.BeginInvoke(() => ScanStatus.Text = Strings.Get("Main.ScanActive", _devices.Count));
        Closed += (_, _) => _scanner.Dispose();
    }

    private const double BaseMinWidth = 700, BaseMinHeight = 440;
    private readonly System.Windows.Threading.DispatcherTimer _saveWindow = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>With larger text the window needs more room: its minimum size grows with it so that nothing is cut.</summary>
    public void ApplyMinimumScale(double scale)
    {
        MinWidth = BaseMinWidth * scale;
        MinHeight = BaseMinHeight * scale;
    }

    private void RestartSaveTimer()
    {
        _saveWindow.Stop();
        _saveWindow.Start();
    }

    /// <summary>Remembers the size, the position, whether it is maximized and the tab, for the next time the panel opens.</summary>
    private void SaveWindowState() =>
        ((App)Application.Current).UpdatePreferences(p => p with { Window = WindowStateHelper.Capture(this), Tab = Tabs.SelectedIndex });

    /// <summary>First-run guidance (task 8.6): search → pair → use → test print.</summary>
    public void StartWizard()
    {
        _wizard = true;
        Tabs.SelectedItem = SearchTab;
        ShowBanner(Strings.Get("Main.WizardBanner"));
        StartScan();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Files dropped anywhere on the panel are printed (one job per file).</summary>
    private async void OnDropFiles(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;
        try
        {
            var message = await QuickPrint.PrintFilesAsync(_service.Client, files);
            ShowBanner(message.Message);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "MiniPrinter", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnTemplateReload(object sender, RoutedEventArgs e) => await _templates.LoadAsync();

    private async void OnTemplatePrint(object sender, RoutedEventArgs e) => await _templates.PrintAsync();

    private async void OnTemplateCsv(object sender, RoutedEventArgs e) => await _templates.LoadCsvAsync();

    private async void OnTemplateCsvClear(object sender, RoutedEventArgs e) => await _templates.ClearCsvAsync();

    private void OnFavoriteSave(object sender, RoutedEventArgs e) => _templates.SaveFavorite();

    private void OnFavoriteDelete(object sender, RoutedEventArgs e) => _templates.DeleteFavorite();

    // ---- Buscar ----

    private void OnScan(object sender, RoutedEventArgs e) => StartScan();

    private void StartScan()
    {
        _devices.Clear();
        ScanStatus.Text = Strings.Get("Main.Scanning");
        try
        {
            _scanner.Start();
        }
        catch (Exception ex)
        {
            ScanStatus.Text = Strings.Get("Main.ScanFailed", ex.Message);
        }
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _devices.Clear();
        foreach (var device in _scanner.Devices)
            OnDeviceFound(device);
    }

    private void OnDeviceFound(FoundDevice device)
    {
        var existing = _devices.FirstOrDefault(d => d.Id == device.Id);
        if (existing is not null)
            _devices.Remove(existing);
        if (OnlyPrinters.IsChecked == true && !device.IsKnownPrinter)
            return;
        // Known printers first, then paired devices.
        var index = 0;
        while (index < _devices.Count && Rank(_devices[index]) <= Rank(device))
            index++;
        _devices.Insert(index, device);
    }

    private static int Rank(FoundDevice d) => (d.IsKnownPrinter ? 0 : 2) + (d.Paired ? 0 : 1);

    private async void OnPair(object sender, RoutedEventArgs e)
    {
        if (DevicesList.SelectedItem is not FoundDevice device)
        {
            ScanStatus.Text = Strings.Get("Main.SelectDevice");
            return;
        }
        ScanStatus.Text = Strings.Get("Main.Pairing", device.Name);
        try
        {
            ScanStatus.Text = await BluetoothScanner.PairAsync(device.Id);
        }
        catch (Exception ex)
        {
            ScanStatus.Text = Strings.Get("Main.PairFailed", ex.Message);
        }
    }

    private async void OnUsePrinter(object sender, RoutedEventArgs e)
    {
        if (DevicesList.SelectedItem is not FoundDevice device)
        {
            ScanStatus.Text = Strings.Get("Main.SelectPrinter");
            return;
        }
        var transport = Enum.Parse<TransportChoice>((string)((ComboBoxItem)TransportCombo.SelectedItem).Tag);
        if (!device.Paired && transport != TransportChoice.Simulated)
        {
            ScanStatus.Text = Strings.Get("Main.PairFirst");
            return;
        }
        var selection = new PrinterSelection
        {
            Name = device.Name,
            Address = device.Address,
            ProfileKey = device.Match?.Profile.Key ?? "d1",
            Transport = transport,
        };
        if (device.Match is null && MessageBox.Show(this,
                Strings.Get("Main.UnknownPrinterConfirm", device.Name),
                "MiniPrinter", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var ok = await Run(async c =>
        {
            await c.SelectPrinterAsync(selection);
            return await c.ConnectAsync();
        });
        if (!ok)
            return;

        _scanner.Stop();
        await _settings.LoadAsync(discardChanges: false);
        Tabs.SelectedIndex = 0;
        if (_wizard || MessageBox.Show(this, Strings.Get("Main.PrinterConfiguredPrompt"), "MiniPrinter",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            await Run(c => c.TestPrintAsync());
        }
        if (_wizard)
        {
            _wizard = false;
            ShowBanner(Strings.Get("Main.WizardDone", _settings.Paper.PrinterName));
        }
    }
}
