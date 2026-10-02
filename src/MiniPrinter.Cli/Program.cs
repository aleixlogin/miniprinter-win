using System.Diagnostics;
using MiniPrinter.Cli;
using MiniPrinter.Control;
using MiniPrinter.Imaging;
using MiniPrinter.Protocol;
using MiniPrinter.Protocol.Catalog;
using MiniPrinter.Transport;

const string usage = """
    miniprinter — diagnostics for "tiny" protocol thermal printers (X5h-E07A, …)

    Usage:
      miniprinter probe        <target> [--watch <seconds>]
      miniprinter test-print   <target> [--darkness 1..5]
      miniprinter stripes      <target> --rows <n> [--darkness 1..5]
      miniprinter print        <target> <file.png|jpg|pwg|pdf> [--darkness 1..5] [--dither auto|atkinson|floyd|threshold] [--text] [--pages 2-3,5]
      miniprinter template     <name> <target> [--<field> <value> …] [--copies <n>] [--csv <file.csv>]
      miniprinter templates                                          list templates and their fields
      miniprinter template list|show <name>|validate <file.json>     inspect templates (no printer needed)
      miniprinter template add <file.json> [--name <n>] | remove <name>   manage user templates (via the service)
      miniprinter feed         <target> [--dots <n>]
      miniprinter find-port    <mac>
      miniprinter queue-recreate [--old <name>] [--result <file.json>] [--control-port <n>]   recreate the Windows print queue (needs administrator rights; the tray runs it with UAC when the service cannot)

    Target (one of):
      --rfcomm <mac>     WinRT RFCOMM socket (e.g. --rfcomm 7A:E0:0C:1D:87:AE)
      --port <COMn>      Bluetooth serial port (e.g. --port COM5)
      --mac <mac>        Bluetooth serial port looked up from the MAC address
      --simulate         In-memory simulated printer

    Options:
      --profile <key>    Printer profile (default: d1, the X5h-E07A)
      --log              Print every frame sent and received
    """;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine(usage);
    return 0;
}

var cli = new CliArgs(args.Skip(1));
var profile = PrinterCatalog.Default.RequireProfile(cli.Value("--profile") ?? "d1");
var stream = new StreamSettings(profile.ChunkSize, profile.DelayMs);
var timeout = TimeSpan.FromSeconds(3);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    switch (args[0])
    {
        case "find-port":
        {
            var mac = BluetoothAddress.Parse(cli.Positional(0) ?? throw new CliException("find-port needs a MAC address."));
            Console.WriteLine(SerialTransport.FindPortForAddress(mac) ?? "no outgoing Bluetooth serial port bound to that address");
            return 0;
        }
        case "probe":
        {
            await using var connection = await ConnectAsync();
            await ProbeAsync(connection);
            if (cli.Value("--watch") is { } watch)
            {
                var until = DateTime.UtcNow.AddSeconds(int.Parse(watch));
                while (DateTime.UtcNow < until && !cts.IsCancellationRequested)
                {
                    await Task.Delay(2000, cts.Token);
                    var state = await connection.GetStateAsync(timeout, cts.Token);
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {Describe(state)}");
                }
            }
            return 0;
        }
        case "test-print":
            return await PrintAsync([new RasterResult(TestPatterns.Calibration(profile.WidthPx), false)]);
        case "stripes":
        {
            var rows = int.Parse(cli.Value("--rows") ?? throw new CliException("stripes needs --rows <n>."));
            return await PrintAsync([new RasterResult(TestPatterns.Stripes(profile.WidthPx, rows), false)]);
        }
        case "print":
        {
            var file = cli.Positional(0) ?? throw new CliException("print needs a file.");
            var pages = PrintFile.Rasterize(file, profile.WidthPx, cli.Value("--dither"), cli.Has("--text"), cli.Value("--pages"));
            return await PrintAsync(pages);
        }
        case "templates":
            ListTemplates();
            return 0;
        case "template":
        {
            var name = cli.Positional(0) ?? throw new CliException("template needs a template name (see 'miniprinter templates').");
            switch (name)
            {
                case "list":
                    ListTemplates();
                    return 0;
                case "show":
                {
                    var which = cli.Positional(1) ?? throw new CliException("template show needs a template name.");
                    Console.WriteLine(TemplateCatalog.Default.GetJson(which) ?? throw new CliException($"Unknown template '{which}'."));
                    return 0;
                }
                case "validate":
                {
                    var layout = Validate(cli.Positional(1) ?? throw new CliException("template validate needs a .json file."), out _);
                    Console.WriteLine($"OK: '{layout.Name}' ({layout.Blocks.Count} bloques, {layout.Fields.Count} campos)");
                    return 0;
                }
                case "add":
                {
                    var file = cli.Positional(1) ?? throw new CliException("template add needs a .json file.");
                    var layout = Validate(file, out var json);
                    var target = cli.Value("--name") ?? layout.Name;
                    using var client = ControlClient.FromTokenFile();
                    var saved = await CallService(() => client.SaveTemplateAsync(target, json));
                    Console.WriteLine($"Plantilla '{saved.Name}' guardada ({(saved.Source == "override" ? "sustituye a la integrada" : "de usuario")}).");
                    return 0;
                }
                case "remove":
                {
                    var which = cli.Positional(1) ?? throw new CliException("template remove needs a template name.");
                    using var client = ControlClient.FromTokenFile();
                    await CallServiceVoid(() => client.DeleteTemplateAsync(which));
                    Console.WriteLine($"Plantilla '{which}' borrada.");
                    return 0;
                }
            }

            var definition = TemplateRenderer.Find(name) ?? throw new CliException($"Unknown template '{name}'. See 'miniprinter templates'.");
            var reserved = new HashSet<string>(["rfcomm", "port", "mac", "simulate", "profile", "log", "darkness", "copies", "csv"], StringComparer.OrdinalIgnoreCase);
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in cli.Options)
            {
                if (reserved.Contains(key) || value is null)
                    continue;
                var field = definition.Fields.FirstOrDefault(f => string.Equals(f.Name, key, StringComparison.OrdinalIgnoreCase));
                // Image fields take a file path on the command line; "\n" in text means a new line.
                fields[key] = field?.Kind == TemplateFieldKind.Image
                    ? Convert.ToBase64String(File.ReadAllBytes(value))
                    : value.Replace("\\n", "\n");
            }
            var copies = int.TryParse(cli.Value("--copies") ?? "1", out var parsedCopies) ? parsedCopies : throw new CliException("--copies must be a whole number.");
            IReadOnlyList<IReadOnlyDictionary<string, string>>? csvRows = null;
            if (cli.Value("--csv") is { } csvFile)
            {
                try
                {
                    var csv = TemplateCsv.Parse(File.ReadAllText(csvFile), definition.Fields.Select(f => f.Name));
                    foreach (var column in csv.IgnoredColumns)
                        Console.Error.WriteLine($"aviso: la columna '{column}' del CSV no es un campo de la plantilla; se ignora.");
                    csvRows = csv.Rows;
                    Console.WriteLine($"{csv.Rows.Count} etiqueta(s) desde {Path.GetFileName(csvFile)}");
                }
                catch (InvalidDataException ex)
                {
                    throw new CliException(ex.Message);
                }
            }
            IReadOnlyList<MonoBitmap> bitmaps;
            try
            {
                bitmaps = TemplateCatalog.Default.RenderBatch(name, fields, csvRows, copies, new RenderOptions { Width = profile.WidthPx });
            }
            catch (TemplateException ex)
            {
                throw new CliException(ex.Message);
            }
            catch (UnauthorizedAccessException)
            {
                throw new CliException("No se puede actualizar el contador de numeración (counters.json): ejecuta la orden como administrador o imprime desde la bandeja.");
            }
            return await PrintAsync([.. bitmaps.Select(b => new RasterResult(b, !definition.IsImage))]);
        }
        case "queue-recreate":
            return await QueueRecreateCommand.RunAsync(cli, cts.Token);
        case "feed":
        {
            await using var connection = await ConnectAsync();
            await connection.SendAsync(Commands.FeedPaper(int.Parse(cli.Value("--dots") ?? "96")), stream, cts.Token);
            await Task.Delay(300);
            return 0;
        }
        default:
            throw new CliException($"Unknown command '{args[0]}'.");
    }
}
catch (CliException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(usage);
    return 64;
}
catch (TransportException ex)
{
    Console.Error.WriteLine($"Transport error ({ex.Kind}): {ex.Message}");
    return 2;
}
catch (TimeoutException)
{
    Console.Error.WriteLine("The printer did not answer in time.");
    return 3;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}

async Task<int> PrintAsync(IReadOnlyList<RasterResult> pages)
{
    // --text forces text mode; otherwise each page uses its own classification.
    var options = new PrintOptions { Darkness = int.Parse(cli.Value("--darkness") ?? "3") };
    var builder = new PrintJobBuilder(profile);
    var job = pages.Where(p => p.Bitmap.Height > 0)
        .SelectMany(p => builder.BuildPage(p.Bitmap, options with { IsText = cli.Has("--text") || p.IsText }))
        .ToArray();
    var rows = pages.Sum(p => p.Bitmap.Height);
    await using var connection = await ConnectAsync();

    var before = await connection.GetStateAsync(timeout, cts.Token);
    Console.WriteLine($"before: {Describe(before)}");
    if (!before.IsReady)
    {
        Console.Error.WriteLine("The printer reports an alarm; not printing.");
        return 4;
    }

    var pauses = 0;
    connection.MessageReceived += m => { if (m is FlowControl { Paused: true }) pauses++; };
    var watch = Stopwatch.StartNew();
    await connection.SendAsync(job, stream, cts.Token);
    Console.WriteLine($"sent {job.Length} bytes ({rows} rows, {pages.Count} page(s)) in {watch.ElapsedMilliseconds} ms, flow-control pauses: {pauses}");

    // The job ends with an A3 query; give the printer time to finish and answer.
    await Task.Delay(TimeSpan.FromMilliseconds(500 + rows * 2));
    var after = await connection.GetStateAsync(TimeSpan.FromSeconds(10), cts.Token);
    Console.WriteLine($"after:  {Describe(after)}");
    return 0;
}

async Task<PrinterConnection> ConnectAsync()
{
    var target = cli.Has("--simulate") ? new TransportTarget(TransportKind.Simulated)
        : cli.Value("--rfcomm") is { } rfcomm ? new TransportTarget(TransportKind.Rfcomm, Address: rfcomm)
        : cli.Value("--port") is { } port ? new TransportTarget(TransportKind.Serial, Port: port)
        : cli.Value("--mac") is { } mac ? new TransportTarget(TransportKind.Serial, Address: mac)
        : throw new CliException("Choose a target: --rfcomm <mac>, --port <COMn>, --mac <mac> or --simulate.");

    var connection = new PrinterConnection(TransportFactory.Create(target));
    if (cli.Has("--log"))
    {
        connection.MessageReceived += m => Console.WriteLine($"  <- {m.Frame}  {m.GetType().Name}");
    }
    var watch = Stopwatch.StartNew();
    await connection.ConnectAsync(cts.Token);
    Console.WriteLine($"connected to {connection.Description} in {watch.ElapsedMilliseconds} ms (profile {profile.Key})");
    await Task.Delay(300, cts.Token); // let the link settle before the first query
    return connection;
}

async Task ProbeAsync(PrinterConnection connection)
{
    var state = await connection.GetStateAsync(timeout, cts.Token);
    Console.WriteLine($"A3 state : {Convert.ToHexString(state.Frame.Payload),-12} {Describe(state)}");
    var info = await connection.GetInfoAsync(timeout, cts.Token);
    Console.WriteLine($"A8 info  : {Convert.ToHexString(info.Frame.Payload),-12} firmware {info.Firmware ?? "?"}");
    var id = await connection.GetIdAsync(timeout, cts.Token);
    Console.WriteLine($"BB id    : {Convert.ToHexString(id.Frame.Payload),-12} {(id.IsProgrammed ? "programmed" : "not programmed")}");
}

static string Describe(DeviceState state)
{
    var alarms = state.IsReady ? "ready" : $"ALARM 0x{state.AlarmByte:X2} ({state.Alarms})";
    var battery = state.BatteryLevel is { } level ? $", paper sensor {state.PaperSensor}, battery {level} (raw)" : "";
    return alarms + battery;
}

static void ListTemplates()
{
    foreach (var t in TemplateCatalog.Default.Definitions)
    {
        var origin = t.Source switch { "user" => " [usuario]", "override" => " [usuario, sustituye a la integrada]", _ => "" };
        Console.WriteLine($"{t.Name,-10} {t.Title} — {t.Description}{origin}");
        foreach (var f in t.Fields)
            Console.WriteLine($"           --{f.Name,-10} {f.Label}{(f.Required ? " (obligatorio)" : "")}{(f.Choices is null ? "" : $" [{string.Join('|', f.Choices)}]")}");
    }
    foreach (var error in TemplateCatalog.Default.Errors)
        Console.Error.WriteLine($"aviso: plantilla omitida — {error}");
}

static TemplateLayout Validate(string file, out string json)
{
    json = File.Exists(file) ? File.ReadAllText(file) : throw new CliException($"No existe el archivo '{file}'.");
    try
    {
        return TemplateCatalog.Default.Validate(json);
    }
    catch (TemplateException ex)
    {
        throw new CliException($"{Path.GetFileName(file)}: {ex.Message}");
    }
}

static async Task<T> CallService<T>(Func<Task<T>> call)
{
    try
    {
        return await call();
    }
    catch (ControlApiException ex)
    {
        throw new CliException(ex.Message);
    }
    catch (Exception ex) when (ex is HttpRequestException or IOException)
    {
        throw new CliException($"No se puede hablar con el servicio MiniPrinter ({ex.Message}). ¿Está en marcha?");
    }
}

static Task CallServiceVoid(Func<Task> call) => CallService(async () =>
{
    await call();
    return 0;
});
