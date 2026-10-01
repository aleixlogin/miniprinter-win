using System.Diagnostics;
using MiniPrinter.Cli;
using MiniPrinter.Protocol;
using MiniPrinter.Protocol.Catalog;
using MiniPrinter.Transport;

const string usage = """
    miniprinter — diagnostics for "tiny" protocol thermal printers (X5h-E07A, …)

    Usage:
      miniprinter probe        <target> [--watch <seconds>]
      miniprinter test-print   <target> [--darkness 1..5]
      miniprinter stripes      <target> --rows <n> [--darkness 1..5]
      miniprinter print        <target> <file.png|jpg|pwg> [--darkness 1..5] [--dither atkinson|floyd|threshold] [--text]
      miniprinter feed         <target> [--dots <n>]
      miniprinter find-port    <mac>

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
            return await PrintAsync([TestPatterns.Calibration(profile.WidthPx)], isText: false);
        case "stripes":
        {
            var rows = int.Parse(cli.Value("--rows") ?? throw new CliException("stripes needs --rows <n>."));
            return await PrintAsync([TestPatterns.Stripes(profile.WidthPx, rows)], isText: false);
        }
        case "print":
        {
            var file = cli.Positional(0) ?? throw new CliException("print needs a file.");
            var pages = PrintFile.Rasterize(file, profile.WidthPx, cli.Value("--dither"), cli.Has("--text"));
            return await PrintAsync(pages, cli.Has("--text"));
        }
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

async Task<int> PrintAsync(IReadOnlyList<MonoBitmap> pages, bool isText)
{
    var options = new PrintOptions { Darkness = int.Parse(cli.Value("--darkness") ?? "3"), IsText = isText };
    var job = new PrintJobBuilder(profile).BuildDocument(pages, options);
    var rows = pages.Sum(p => p.Height);
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
