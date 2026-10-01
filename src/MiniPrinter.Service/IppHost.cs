using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MiniPrinter.Control;
using MiniPrinter.Ipp;

namespace MiniPrinter.Service;

/// <summary>
/// Runs the IPP endpoint in its own Kestrel instance so it can be rebound when the network mode
/// or port changes: loopback only (<see cref="NetworkMode.Local"/>) or all interfaces with mDNS
/// advertising and a firewall rule (<see cref="NetworkMode.Lan"/>). Changes wait for an idle queue.
/// </summary>
public sealed class IppHost : BackgroundService
{
    private readonly SettingsStore _settings;
    private readonly JobQueue _queue;
    private readonly ILogger<IppHost> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _restart = new(0);
    private WebApplication? _app;
    private MdnsAdvertiser? _mdns;
    private ServiceSettings? _active;

    public IppHost(SettingsStore settings, JobQueue queue, ILogger<IppHost> logger, ILoggerFactory loggerFactory)
    {
        _settings = settings;
        _queue = queue;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _settings.Changed += (previous, current) =>
        {
            if (previous.NetworkMode != current.NetworkMode || previous.IppPort != current.IppPort || previous.PrinterName != current.PrinterName)
                _restart.Release();
        };
    }

    /// <summary>URLs clients can use to reach the printer in the current mode.</summary>
    public IReadOnlyList<string> Urls { get; private set; } = [];

    public IppPrinterService? Printer { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = _settings.Current;
            try
            {
                await StartAsync(settings, stoppingToken);
            }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException)
            {
                _logger.LogError(ex, "Could not start the IPP endpoint on port {Port}", settings.IppPort);
            }

            await _restart.WaitAsync(stoppingToken);
            // Apply network changes only when no job is in progress.
            while (!_queue.IsIdle && !stoppingToken.IsCancellationRequested)
                await Task.Delay(1000, stoppingToken);
            await StopCurrentAsync();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await StopCurrentAsync();
    }

    private async Task StartAsync(ServiceSettings settings, CancellationToken ct)
    {
        var description = new IppPrinterDescription
        {
            Name = settings.PrinterName,
            Location = settings.NetworkMode == NetworkMode.Lan ? Environment.MachineName : "",
        };
        var printer = new IppPrinterService(description, _queue);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_loggerFactory);
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Limits.MaxRequestBodySize = IppPrinterService.MaxDocumentBytes + 1024 * 1024;
            if (settings.NetworkMode == NetworkMode.Lan)
                k.ListenAnyIP(settings.IppPort);
            else
                k.Listen(IPAddress.Loopback, settings.IppPort);
        });
        var app = builder.Build();
        var logger = _loggerFactory.CreateLogger("MiniPrinter.Ipp");
        app.MapPost(description.ResourcePath, context => IppEndpoint.HandleAsync(context, printer, logger));
        app.MapPost("/", context => IppEndpoint.HandleAsync(context, printer, logger));
        app.MapGet("/", context => IppEndpoint.StatusPageAsync(context, printer, _queue));
        app.MapGet(description.ResourcePath, context => IppEndpoint.StatusPageAsync(context, printer, _queue));

        await app.StartAsync(ct);
        _app = app;
        _active = settings;
        Printer = printer;
        Urls = BuildUrls(settings, description.ResourcePath);
        _logger.LogInformation("IPP printer '{Name}' listening ({Mode}): {Urls}", settings.PrinterName, settings.NetworkMode, string.Join(", ", Urls));

        if (settings.NetworkMode == NetworkMode.Lan)
        {
            FirewallRule.Add(settings.IppPort, _logger);
            _mdns = new MdnsAdvertiser(_logger);
            _mdns.Register(settings.PrinterName, settings.IppPort, new Dictionary<string, string>
            {
                ["txtvers"] = "1",
                ["qtotal"] = "1",
                ["rp"] = description.ResourcePath.TrimStart('/'),
                ["ty"] = description.MakeAndModel,
                ["note"] = description.Location,
                ["product"] = "(MiniPrinter)",
                ["pdl"] = string.Join(",", description.DocumentFormats.Where(f => f != "application/octet-stream")),
                ["Color"] = "F",
                ["Duplex"] = "F",
                ["UUID"] = description.Uuid.ToString(),
                ["TLS"] = "",
                ["kind"] = "document,receipt,label",
                ["PaperMax"] = "<legal-A4",
                ["URF"] = "none",
            });
        }
    }

    private async Task StopCurrentAsync()
    {
        // StopAsync and the restart loop may race during shutdown; whoever takes the app stops it.
        var app = Interlocked.Exchange(ref _app, null);
        var active = Interlocked.Exchange(ref _active, null);
        Interlocked.Exchange(ref _mdns, null)?.Dispose();
        if (active?.NetworkMode == NetworkMode.Lan)
            FirewallRule.Remove(_logger);
        if (app is not null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
        Urls = [];
    }

    private static IReadOnlyList<string> BuildUrls(ServiceSettings settings, string path)
    {
        var urls = new List<string> { $"http://127.0.0.1:{settings.IppPort}{path}" };
        if (settings.NetworkMode != NetworkMode.Lan)
            return urls;
        urls.Add($"ipp://{Environment.MachineName.ToLowerInvariant()}.local:{settings.IppPort}{path}");
        foreach (var address in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                     .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                     .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
            urls.Add($"ipp://{address.Address}:{settings.IppPort}{path}");
        return urls;
    }
}

/// <summary>HTTP glue between Kestrel and <see cref="IppPrinterService"/>.</summary>
public static class IppEndpoint
{
    public static async Task HandleAsync(HttpContext context, IppPrinterService printer, ILogger logger)
    {
        if (!string.Equals(context.Request.ContentType?.Split(';')[0].Trim(), "application/ipp", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        IppMessage request;
        try
        {
            request = await IppCodec.ReadAsync(context.Request.Body, context.RequestAborted);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            logger.LogWarning("Malformed IPP request from {Remote}: {Message}", context.Connection.RemoteIpAddress, ex.Message);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var authority = context.Request.Host.HasValue ? context.Request.Host.Value : $"127.0.0.1:{context.Connection.LocalPort}";
        var response = await printer.HandleAsync(request, context.Request.Body, authority, context.RequestAborted);
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var requested = request.Operation("requested-attributes")?.Values.Select(v => v.ToString());
            logger.LogDebug("IPP 0x{Op:X4} from {Remote} -> 0x{Status:X4} {Requested}", request.Code,
                context.Connection.RemoteIpAddress, response.Code, requested is null ? "" : string.Join(",", requested));
        }

        context.Response.ContentType = "application/ipp";
        var bytes = IppCodec.Encode(response);
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
    }

    public static Task StatusPageAsync(HttpContext context, IppPrinterService printer, JobQueue queue)
    {
        var snapshot = queue.GetPrinter();
        var name = WebUtility.HtmlEncode(printer.Description.Name);
        var html = $"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><title>{name}</title>
            <meta name="viewport" content="width=device-width, initial-scale=1"></head>
            <body style="font-family:system-ui,sans-serif;max-width:40rem;margin:2rem auto;padding:0 1rem">
            <h1>{name}</h1>
            <p>State: <b>{snapshot.State}</b> — {WebUtility.HtmlEncode(string.Join(", ", snapshot.StateReasons))}</p>
            <p>{WebUtility.HtmlEncode(snapshot.StateMessage ?? "")}</p>
            <p>Queued jobs: {snapshot.QueuedJobs}</p>
            <p>Manage the printer from the MiniPrinter tray app.</p>
            </body></html>
            """;
        context.Response.ContentType = "text/html; charset=utf-8";
        return context.Response.WriteAsync(html);
    }
}
