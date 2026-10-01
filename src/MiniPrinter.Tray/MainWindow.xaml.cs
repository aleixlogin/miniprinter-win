using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using MiniPrinter.Control;

namespace MiniPrinter.Tray;

public partial class MainWindow : Window
{
    private readonly ServiceConnection _service;
    private readonly BluetoothScanner _scanner = new();
    private readonly ObservableCollection<FoundDevice> _devices = [];
    private ServiceSettings? _settings;
    private bool _wizard;
    private DateTime _lastJobsRefresh = DateTime.MinValue;

    public MainWindow(ServiceConnection service)
    {
        _service = service;
        InitializeComponent();
        DevicesList.ItemsSource = _devices;
        _scanner.DeviceFound += d => Dispatcher.BeginInvoke(() => OnDeviceFound(d));
        _scanner.DeviceRemoved += id => Dispatcher.BeginInvoke(() =>
        {
            var existing = _devices.FirstOrDefault(x => x.Id == id);
            if (existing is not null) _devices.Remove(existing);
        });
        _scanner.Completed += () => Dispatcher.BeginInvoke(() => ScanStatus.Text = $"{_devices.Count} dispositivos. La búsqueda sigue activa.");
        Loaded += async (_, _) => await LoadSettingsAsync();
        Closed += (_, _) => _scanner.Dispose();
    }

    /// <summary>First-run guidance (task 8.6): search → pair → use → test print.</summary>
    public void StartWizard()
    {
        _wizard = true;
        Tabs.SelectedItem = SearchTab;
        ShowBanner("Bienvenido. Paso 1: enciende la impresora y pulsa «Buscar». Paso 2: selecciónala y pulsa «Emparejar» si no lo está. " +
                   "Paso 3: «Usar esta impresora». Después imprimiremos una página de prueba.");
        StartScan();
    }

    public void ShowStatus(StatusDto? status, bool available, string? error)
    {
        ServiceText.Text = available ? $"En ejecución (v{status?.Version})" : error ?? "No disponible";
        if (!available || status is null)
        {
            ShowBanner(error ?? "El servicio MiniPrinter no responde. Comprueba que está instalado e iniciado (services.msc).");
            return;
        }
        if (!_wizard)
            Banner.Visibility = Visibility.Collapsed;

        PrinterText.Text = status.Printer is null ? "Ninguna (ve a «Buscar impresoras»)" : $"{status.Printer.Name}  ·  {status.Printer.Address}  ·  {status.Printer.Transport}";
        LinkText.Text = status.Link switch
        {
            "Connected" => "Conectada",
            "Connecting" => "Conectando…",
            "Error" => $"Error: {status.LastError}",
            _ => "Desconectada (se conecta automáticamente al imprimir)",
        };
        StateText.Text = status.Link != "Connected" && status.LastSeen is null ? "—"
            : status.Alarms.Count > 0 ? $"Requiere atención: {string.Join(", ", status.Alarms.Select(App.Translate))}"
            : status.Printing ? "Imprimiendo" : "Lista";
        BatteryText.Text = status.BatteryLevel is { } level ? $"{level} (valor en bruto de la impresora)" : "—";
        FirmwareText.Text = status.Firmware ?? "—";
        UrlsText.Text = string.Join(Environment.NewLine, status.IppUrls);
        ConnectButton.Content = status.Link == "Connected" ? "Desconectar" : "Conectar";

        if (DateTime.UtcNow - _lastJobsRefresh > TimeSpan.FromMilliseconds(500))
        {
            _lastJobsRefresh = DateTime.UtcNow;
            _ = RefreshJobsAsync();
        }
    }

    private async Task RefreshJobsAsync()
    {
        try
        {
            var jobs = await _service.Client.GetJobsAsync();
            JobsList.ItemsSource = jobs.Select(j => j with { State = TranslateState(j.State) }).ToList();
        }
        catch (Exception)
        {
            // Status banner already reports service problems.
        }
    }

    private static string TranslateState(string state) => state switch
    {
        "Pending" => "En cola",
        "Processing" => "Imprimiendo",
        "ProcessingStopped" => "Esperando impresora",
        "Completed" => "Impreso",
        "Canceled" => "Cancelado",
        "Aborted" => "Error",
        _ => state,
    };

    private void ShowBanner(string text)
    {
        BannerText.Text = text;
        Banner.Visibility = Visibility.Visible;
    }

    // ---- Estado ----

    private async void OnConnect(object sender, RoutedEventArgs e) =>
        await Run(c => _service.Status?.Link == "Connected" ? c.DisconnectAsync() : c.ConnectAsync());

    private async void OnTestPrint(object sender, RoutedEventArgs e) => await Run(c => c.TestPrintAsync());

    private async void OnFeed(object sender, RoutedEventArgs e) => await Run(c => c.FeedAsync());

    /// <summary>Opens the pages of the last job exactly as they were sent to the printer.</summary>
    private async void OnPreview(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MiniPrinter-preview");
            System.IO.Directory.CreateDirectory(folder);
            foreach (var old in System.IO.Directory.GetFiles(folder, "*.png"))
                System.IO.File.Delete(old);
            var pages = 0;
            for (var page = 1; page <= 20; page++)
            {
                var png = await _service.Client.GetLastJobPageAsync(page);
                if (png is null)
                    break;
                await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(folder, $"pagina-{page}.png"), png);
                pages++;
            }
            if (pages == 0)
            {
                MessageBox.Show(this, "Todavía no hay ningún trabajo con vista previa.", "MiniPrinter");
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(folder, "pagina-1.png")) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "MiniPrinter", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnCancelJob(object sender, RoutedEventArgs e)
    {
        if (JobsList.SelectedItem is JobDto job)
            await Run(async c => { await c.CancelJobAsync(job.Id); return true; });
    }

    // ---- Buscar ----

    private void OnScan(object sender, RoutedEventArgs e) => StartScan();

    private void StartScan()
    {
        _devices.Clear();
        ScanStatus.Text = "Buscando…";
        try
        {
            _scanner.Start();
        }
        catch (Exception ex)
        {
            ScanStatus.Text = $"No se pudo buscar: {ex.Message}";
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
            ScanStatus.Text = "Selecciona un dispositivo.";
            return;
        }
        ScanStatus.Text = $"Emparejando {device.Name}…";
        try
        {
            ScanStatus.Text = await BluetoothScanner.PairAsync(device.Id);
        }
        catch (Exception ex)
        {
            ScanStatus.Text = $"Error al emparejar: {ex.Message}";
        }
    }

    private async void OnUsePrinter(object sender, RoutedEventArgs e)
    {
        if (DevicesList.SelectedItem is not FoundDevice device)
        {
            ScanStatus.Text = "Selecciona la impresora en la lista.";
            return;
        }
        var transport = Enum.Parse<TransportChoice>((string)((ComboBoxItem)TransportCombo.SelectedItem).Tag);
        if (!device.Paired && transport != TransportChoice.Simulated)
        {
            ScanStatus.Text = "Primero empareja la impresora.";
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
                $"«{device.Name}» no está en el catálogo de impresoras conocidas. ¿Usarla con el perfil d1 (X5h)?",
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
        await LoadSettingsAsync();
        Tabs.SelectedIndex = 0;
        if (_wizard || MessageBox.Show(this, "Impresora configurada. ¿Imprimir una página de prueba?", "MiniPrinter",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            await Run(c => c.TestPrintAsync());
        }
        if (_wizard)
        {
            _wizard = false;
            ShowBanner("¡Listo! Se ha enviado una página de prueba. La impresora ya aparece como «" + (_settings?.PrinterName ?? "X5h Thermal Printer") +
                       "» en el diálogo de impresión de Windows.");
        }
    }

    // ---- Ajustes ----

    private async void OnLoadSettings(object sender, RoutedEventArgs e) => await LoadSettingsAsync();

    private async Task LoadSettingsAsync()
    {
        try
        {
            _settings = await _service.Client.GetSettingsAsync();
        }
        catch (Exception ex)
        {
            SettingsStatus.Text = ex.Message;
            return;
        }
        DarknessSlider.Value = _settings.Darkness;
        DitherCombo.SelectedItem = DitherCombo.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == _settings.Dither.ToString());
        FeedSlider.Value = _settings.ExtraFeedSteps;
        IdleBox.Text = _settings.IdleTimeoutSeconds.ToString();
        NameBox.Text = _settings.PrinterName;
        LocalRadio.IsChecked = _settings.NetworkMode == NetworkMode.Local;
        LanRadio.IsChecked = _settings.NetworkMode == NetworkMode.Lan;
        PortBox.Text = _settings.IppPort.ToString();
        SettingsStatus.Text = "";
    }

    private async void OnSaveSettings(object sender, RoutedEventArgs e)
    {
        if (_settings is null)
            return;
        if (!int.TryParse(IdleBox.Text, out var idle) || !int.TryParse(PortBox.Text, out var port))
        {
            SettingsStatus.Text = "Revisa los campos numéricos.";
            return;
        }
        var updated = _settings with
        {
            Darkness = (int)DarknessSlider.Value,
            Dither = Enum.Parse<DitherChoice>((string)((ComboBoxItem)DitherCombo.SelectedItem).Tag),
            ExtraFeedSteps = (int)FeedSlider.Value,
            IdleTimeoutSeconds = idle,
            PrinterName = NameBox.Text,
            NetworkMode = LanRadio.IsChecked == true ? NetworkMode.Lan : NetworkMode.Local,
            IppPort = port,
        };
        try
        {
            _settings = await _service.Client.SaveSettingsAsync(updated);
            SettingsStatus.Text = updated.NetworkMode != NetworkMode.Local
                ? "Guardado. El modo red se aplica cuando no hay trabajos en curso."
                : "Guardado.";
        }
        catch (Exception ex)
        {
            SettingsStatus.Text = ex.Message;
        }
    }

    private async Task<bool> Run<T>(Func<ControlClient, Task<T>> action)
    {
        try
        {
            await action(_service.Client);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "MiniPrinter", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
}
