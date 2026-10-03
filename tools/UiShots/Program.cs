using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Renders the controls of the tray in each theme to PNG files, to look at the result of the styles without opening the
// application:  dotnet run --project tools/UiShots -- <output directory>
namespace UiShots;

public static class Program
{
    private static readonly string[] ThemeNames = ["Light", "Dark", "HighContrast"];

    [STAThread]
    public static int Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "miniprinter-uishots");
        // Never touch the real preferences of the user from here.
        Environment.SetEnvironmentVariable("MINIPRINTER_UISHOTS", "1");
        Environment.SetEnvironmentVariable("MINIPRINTER_TRAY_PREFS", Path.Combine(Path.GetTempPath(), "miniprinter-uishots-tray.json"));
        Directory.CreateDirectory(output);
        if (args.Contains("--app"))
        {
            Directory.CreateDirectory(output);
            AppShots.Run(output);
            Console.WriteLine($"Saved to {output}");
            Environment.Exit(0);
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var theme in ThemeNames)
        {
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/MiniPrinter.Tray;component/Themes/Theme.{theme}.xaml") });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MiniPrinter.Tray;component/Themes/Controls.xaml") });
            var window = Showcase.Build();
            Save(window, Path.Combine(output, $"showcase-{theme}.png"));
        }
        Console.WriteLine($"Saved to {output}");
        return 0;
    }

    /// <summary>Lays the window out off screen and saves what it would show.</summary>
    public static void Save(Window window, string path, double width = 760, double height = 640, bool keepOpen = false)
    {
        window.Width = width;
        window.Height = height;
        window.WindowStyle = WindowStyle.None;
        window.ShowInTaskbar = false;
        window.Left = -20000;
        window.Top = -20000;
        if (!window.IsVisible)
            window.Show();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)(width * dpi.DpiScaleX), (int)(height * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        if (!keepOpen)
            window.Close();
    }
}

public static class Showcase
{
    public static Window Build()
    {
        var window = new MiniPrinter.Tray.ThemedWindow { Title = "Showcase" };
        var tabs = new TabControl { Margin = new Thickness(12) };

        var page = new StackPanel { Margin = new Thickness(10) };
        var label = new TextBlock { Text = "Impresora" };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        page.Children.Add(label);
        page.Children.Add(new TextBlock { Text = "X5h-E07A · 7A:E0:0C:1D:87:AE · Conectada", FontWeight = FontWeights.SemiBold });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 10) };
        buttons.Children.Add(new Button { Content = "Normal" });
        buttons.Children.Add(new Button { Content = "Predeterminado", IsDefault = true });
        buttons.Children.Add(new Button { Content = "Desactivado", IsEnabled = false });
        page.Children.Add(buttons);
        page.Children.Add(new TextBox { Text = "Texto de ejemplo", Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) });
        page.Children.Add(new TextBox { Text = "Desactivado", Width = 260, IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) });
        var combos = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var combo = new ComboBox { Width = 200, Margin = new Thickness(0, 0, 10, 0) };
        combo.Items.Add("Automático");
        combo.Items.Add("Atkinson");
        combo.Items.Add("Floyd–Steinberg");
        combo.SelectedIndex = 0;
        var editable = new ComboBox { Width = 200, IsEditable = true, Text = "Favorito editable" };
        editable.Items.Add("Uno");
        editable.Items.Add("Dos");
        combos.Children.Add(combo);
        combos.Children.Add(editable);
        page.Children.Add(combos);
        page.Children.Add(new CheckBox { Content = "Casilla marcada", IsChecked = true, Margin = new Thickness(0, 0, 0, 4) });
        page.Children.Add(new CheckBox { Content = "Casilla sin marcar", Margin = new Thickness(0, 0, 0, 4) });
        page.Children.Add(new CheckBox { Content = "Casilla desactivada", IsEnabled = false, Margin = new Thickness(0, 0, 0, 4) });
        page.Children.Add(new RadioButton { Content = "Opción elegida", IsChecked = true, GroupName = "a", Margin = new Thickness(0, 0, 0, 4) });
        page.Children.Add(new RadioButton { Content = "Otra opción", GroupName = "a", Margin = new Thickness(0, 0, 0, 8) });
        page.Children.Add(new Slider { Minimum = 1, Maximum = 5, Value = 3, Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) });

        var list = new ListView { Height = 120, Margin = new Thickness(0, 0, 0, 8) };
        var grid = new GridView();
        grid.Columns.Add(new GridViewColumn { Header = "#", Width = 40, DisplayMemberBinding = new System.Windows.Data.Binding("Id") });
        grid.Columns.Add(new GridViewColumn { Header = "Documento", Width = 220, DisplayMemberBinding = new System.Windows.Data.Binding("Name") });
        grid.Columns.Add(new GridViewColumn { Header = "Estado", Width = 140, DisplayMemberBinding = new System.Windows.Data.Binding("State") });
        list.View = grid;
        list.Items.Add(new { Id = 3, Name = "Ticket ESC/POS", State = "Completado" });
        list.Items.Add(new { Id = 2, Name = "Etiqueta", State = "En espera" });
        list.Items.Add(new { Id = 1, Name = "Nota rápida", State = "Cancelado" });
        list.SelectedIndex = 1;
        page.Children.Add(list);

        var expander = new Expander { Header = "Detalles", IsExpanded = true };
        expander.Content = new TextBlock { Text = "Firmware 1.2.3 · Servicio 0.8.0" };
        page.Children.Add(expander);

        tabs.Items.Add(new TabItem { Header = "Estado", Content = page });
        tabs.Items.Add(new TabItem { Header = "Buscar impresoras", Content = new TextBlock { Text = "…" } });
        tabs.Items.Add(new TabItem { Header = "Plantillas", Content = new TextBlock { Text = "…" } });
        tabs.Items.Add(new TabItem { Header = "Ajustes", Content = new TextBlock { Text = "…" } });
        window.Content = tabs;
        return window;
    }
}

/// <summary>The real panel (MainWindow) with a simulated service, in each theme and text size.</summary>
public static class AppShots
{
    private static IEnumerable<T> LogicalTreeHelperFind<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T found)
                yield return found;
            foreach (var nested in LogicalTreeHelperFind<T>(child))
                yield return nested;
        }
    }

    private static IReadOnlyList<MiniPrinter.Control.JobDto> Jobs()
    {
        var now = new DateTimeOffset(2026, 10, 3, 12, 30, 0, TimeSpan.Zero);
        MiniPrinter.Control.JobDto Job(int id, string name, string state, string? message, string source, string? origin, bool canReprint) =>
            new(id, name, "maria", state, message, now.AddMinutes(id), null, 1, 1000) { Source = source, Origin = origin, CanReprint = canReprint };
        return
        [
            Job(6, "Reimpresión de Tique", "Pending", null, "Panel", "aleix", false),
            Job(5, "Ticket ESC/POS", "Processing", "Printing", "Raw", "192.168.0.30", false),
            Job(4, "Etiqueta envío.pdf", "ProcessingStopped", "Printer needs attention: OutOfPaper", "Windows", "maria", false),
            Job(3, "Hola desde la API", "Completed", "Printed", "Api", "127.0.0.1", true),
            Job(2, "Página de prueba", "Completed", "Printed", "Panel", "aleix", true),
            Job(1, "Informe.pdf", "Aborted", "Printer did not answer", "Windows", "maria", false),
        ];
    }

    public static void Run(string output)
    {
        var app = new MiniPrinter.Tray.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var status = new MiniPrinter.Control.StatusDto
        {
            Link = "Connected",
            Ready = true,
            Printer = new MiniPrinter.Control.PrinterSelection { Name = "X5h-E07A", Address = "7A:E0:0C:1D:87:AE" },
            Firmware = "1.2.3",
            Version = "0.8.0",
            BatteryLevel = 39,
            IppUrls = ["http://127.0.0.1:8631/ipp/print"],
            RawPort = new MiniPrinter.Control.RawPortDto(true, 9100, true, null, ["127.0.0.1:9100"]),
        };
        foreach (var (theme, fontSize) in new[] { ("Light", 13.0), ("Dark", 13.0), ("Dark", 15.0) })
        {
            app.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri($"pack://application:,,,/MiniPrinter.Tray;component/Themes/Theme.{theme}.xaml") };
            app.Resources["Ui.FontSize"] = fontSize;
            var window = new MiniPrinter.Tray.MainWindow(new MiniPrinter.Tray.ServiceConnection());
            window.ShowStatus(status, true, null);
            var tabs = (TabControl)window.FindName("Tabs");
            // Status: the card in each state, with the list of jobs of several origins.
            window.Status.MergeJobs(Jobs());
            window.Status.DetailsOpen = true;
            var states = new (string Name, MiniPrinter.Control.StatusDto? Status, bool Available)[]
            {
                ("lista", status with { BatteryLevel = 39, BatteryUnit = MiniPrinter.Control.BatteryUnit.Percent, BatteryPercent = 39 }, true),
                ("sinpapel", status with { Alarms = ["OutOfPaper"], Ready = false, BatteryLevel = 12, BatteryUnit = MiniPrinter.Control.BatteryUnit.Percent, BatteryPercent = 12, LowBattery = true }, true),
                ("sinimpresora", status with { Printer = null }, true),
                ("servicio", null, false),
                ("error", status with { Link = "Error", LastError = "No se pudo abrir el puerto Bluetooth" }, true),
            };
            tabs.SelectedIndex = 0;
            foreach (var (name, state, available) in states)
            {
                window.ShowStatus(state, available, available ? null : "No se puede conectar con el servicio");
                Program.Save(window, Path.Combine(output, $"app-{theme}-{fontSize}-estado-{name}.png"), 860, 760, keepOpen: true);
            }
            // Diagnostics: Bluetooth off, the printer of Windows missing and fonts missing.
            var diagnostics = new MiniPrinter.Gui.DiagnosticsViewModel(new FakeDiagnostics());
            var diagnosticsWindow = new MiniPrinter.Tray.DiagnosticsWindow(window, diagnostics,
                () => new MiniPrinter.Gui.DiagnosticsReportContext("0.9.0", "0.9.0", "Windows 11", null, null, null, DateTimeOffset.Now), _ => { });
            Program.Save(diagnosticsWindow, Path.Combine(output, $"app-{theme}-{fontSize}-diagnostico.png"), 700, 700, keepOpen: false);
            window.ShowStatus(status, true, null);
            tabs.SelectedIndex = 1;
            Program.Save(window, Path.Combine(output, $"app-{theme}-{fontSize}-buscar.png"), 820, 760, keepOpen: true);

            // Templates: the gallery with pictures made up, then the same in the list view.
            var gallery = new MiniPrinter.Gui.TemplateGalleryViewModel(new FakeThumbnails(), MiniPrinter.Gui.TemplatesViewChoice.Gallery, ["label"]);
            gallery.SetTemplates(
            [
                new("qr", "Código QR", "Un código QR con un texto", [], true),
                new("label", "Etiqueta", "Etiqueta con título", [], false),
                new("todo", "Lista de tareas", "Tareas con casillas", [], false),
                new("wifi", "Wi-Fi", "Código QR para unirse a la red", [], true, "override"),
                new("mia", "Mi plantilla", "Hecha por mí", [], false, "user"),
                new("receipt", "Recibo", "Recibo con total", [], false),
            ]);
            gallery.LoadThumbnailsAsync().GetAwaiter().GetResult();
            gallery.SelectedName = "qr";
            ((FrameworkElement)window.FindName("TemplatesRoot")).DataContext = gallery;
            tabs.SelectedIndex = 2;
            Program.Save(window, Path.Combine(output, $"app-{theme}-{fontSize}-plantillas.png"), 860, 760, keepOpen: true);
            gallery.Search = "etiq";
            Program.Save(window, Path.Combine(output, $"app-{theme}-{fontSize}-plantillas-busqueda.png"), 860, 760, keepOpen: true);
            gallery.Search = "";
            gallery.IsGallery = false;
            Program.Save(window, Path.Combine(output, $"app-{theme}-{fontSize}-plantillas-lista.png"), 860, 760, keepOpen: true);

            // Settings: every section with the values of a service, then one with unsaved changes and one found by the search.
            tabs.SelectedIndex = 3;
            var settings = new MiniPrinter.Control.ServiceSettings
            {
                Printer = status.Printer,
                Darkness = 4,
                KeepAlive = true,
                RawPortEnabled = true,
                AutomationApiEnabled = true,
                BatteryUnit = MiniPrinter.Control.BatteryUnit.Percent,
            };
            foreach (var section in window.Settings.Sections)
                section.Load(settings);
            window.Settings.Raw.Tools.Gateway = new FakeRawPort();
            window.Settings.Raw.ShowStatus(new MiniPrinter.Control.RawPortDto(true, 9100, true, null, ["127.0.0.1:9100", "mi-pc:9100", "192.168.1.5:9100"]), MiniPrinter.Control.NetworkMode.Lan);
            window.Settings.Raw.Tools.RefreshClientsAsync().GetAwaiter().GetResult();
            foreach (var section in window.Settings.Sections)
            {
                window.Settings.Selected = section;
                Program.Save(window, Path.Combine(output, $"app-{theme}-{fontSize}-ajustes-{section.Key}.png"), 900, 700, keepOpen: true);
            }
            var print = window.Settings.Sections.OfType<MiniPrinter.Gui.PrintSectionViewModel>().Single();
            window.Settings.Selected = print;
            print.Darkness = 5;
            print.IdleSeconds = "x";
            Program.Save(window, Path.Combine(output, $"app-{theme}-{fontSize}-ajustes-cambios.png"), 900, 700, keepOpen: true);
            print.Discard();
            window.Settings.Search = "bateria";
            Program.Save(window, Path.Combine(output, $"app-{theme}-{fontSize}-ajustes-busqueda.png"), 900, 700, keepOpen: true);
            window.Settings.Search = "";
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Environment.SetEnvironmentVariable("MINIPRINTER_KEEP", "1");
        }
    }
}

/// <summary>Results made up to photograph the diagnostics.</summary>
internal sealed class FakeDiagnostics : MiniPrinter.Gui.IDiagnosticsSource
{
    public MiniPrinter.Control.PrinterSelection? Printer { get; } = new() { Name = "X5h-E07A", Address = "7A:E0:0C:1D:87:AE" };

    public Task<IReadOnlyList<MiniPrinter.Control.DiagnosticCheckDto>> GetServiceChecksAsync() => Task.FromResult<IReadOnlyList<MiniPrinter.Control.DiagnosticCheckDto>>(
    [
        new(MiniPrinter.Control.DiagnosticIds.Link, MiniPrinter.Control.DiagnosticStatus.Ok, null),
        new(MiniPrinter.Control.DiagnosticIds.Paper, MiniPrinter.Control.DiagnosticStatus.Warn, "OutOfPaper"),
        new(MiniPrinter.Control.DiagnosticIds.Queue, MiniPrinter.Control.DiagnosticStatus.Fail, "X5h Thermal Printer"),
        new(MiniPrinter.Control.DiagnosticIds.Ipp, MiniPrinter.Control.DiagnosticStatus.Ok, "8631"),
        new(MiniPrinter.Control.DiagnosticIds.Raw, MiniPrinter.Control.DiagnosticStatus.Ok, "Desactivado"),
        new(MiniPrinter.Control.DiagnosticIds.Fonts, MiniPrinter.Control.DiagnosticStatus.Warn, "árabe y tailandés"),
    ]);

    public Task<bool?> IsBluetoothOnAsync() => Task.FromResult<bool?>(false);

    public Task<bool?> IsPairedAsync(MiniPrinter.Control.PrinterSelection printer) => Task.FromResult<bool?>(true);
}

/// <summary>Clients made up to photograph the direct printing section.</summary>
internal sealed class FakeRawPort : MiniPrinter.Gui.IRawPortGateway
{
    public Task<MiniPrinter.Control.JobDto> SendTestAsync() => throw new InvalidOperationException("not used");

    public Task<IReadOnlyList<MiniPrinter.Control.RawClientDto>> GetClientsAsync() => Task.FromResult<IReadOnlyList<MiniPrinter.Control.RawClientDto>>(
    [
        new(DateTimeOffset.Now, "192.168.1.30", "ESC/POS", 1420, 2, 12, MiniPrinter.Control.RawClientResult.Queued),
        new(DateTimeOffset.Now.AddMinutes(-3), "192.168.1.41", "PNG", 48200, 0, 11, MiniPrinter.Control.RawClientResult.Queued),
        new(DateTimeOffset.Now.AddMinutes(-9), "192.168.1.41", "unknown", 8, 0, null, MiniPrinter.Control.RawClientResult.Rejected),
    ]);
}

/// <summary>Pictures made up (bars) to photograph the gallery.</summary>
internal sealed class FakeThumbnails : MiniPrinter.Gui.IThumbnailSource
{
    public Task<byte[]?> GetAsync(string name, int width)
    {
        const int w = 160, h = 90;
        var pixels = new byte[w * h];
        var seed = name.Sum(c => c);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            pixels[y * w + x] = (byte)(((x / 6 + y / 9 + seed) % 5 == 0 || y < 6) ? 30 : 250);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(
            System.Windows.Media.Imaging.BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Gray8, null, pixels, w)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return Task.FromResult<byte[]?>(stream.ToArray());
    }
}
