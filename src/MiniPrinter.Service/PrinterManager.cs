using MiniPrinter.Control;
using MiniPrinter.Protocol;
using MiniPrinter.Protocol.Catalog;
using MiniPrinter.Transport;

namespace MiniPrinter.Service;

/// <summary>Owns the <see cref="PrinterSession"/> for the selected printer and recreates it when the selection changes.</summary>
public sealed class PrinterManager : IAsyncDisposable
{
    private readonly SettingsStore _settings;
    private readonly ILogger<PrinterManager> _logger;
    private readonly Func<PrinterSelection, IByteTransport>? _transportOverride;
    private readonly object _gate = new();
    private PrinterSession? _session;
    private PrinterSelection? _selection;
    private FakePrinterTransport? _simulated;

    public PrinterManager(SettingsStore settings, ILogger<PrinterManager> logger, Func<PrinterSelection, IByteTransport>? transportOverride = null)
    {
        _settings = settings;
        _logger = logger;
        _transportOverride = transportOverride;
        _settings.Changed += (previous, current) =>
        {
            if (previous.Printer != current.Printer || previous.IdleTimeoutSeconds != current.IdleTimeoutSeconds)
                _ = ReplaceSessionAsync(current);
        };
        CreateSession(settings.Current);
    }

    /// <summary>Raised whenever the printer status changes (including session replacement).</summary>
    public event Action<PrinterStatus>? StatusChanged;

    public PrinterSelection? Selection
    {
        get { lock (_gate) return _selection; }
    }

    public PrinterProfile Profile =>
        PrinterCatalog.Default.GetProfile(Selection?.ProfileKey ?? "d1") ?? PrinterCatalog.Default.RequireProfile("d1");

    public PrinterStatus Status
    {
        get { lock (_gate) return _session?.Status ?? new PrinterStatus { Link = LinkState.Disconnected }; }
    }

    /// <summary>The simulated printer, when the selected transport is <see cref="TransportChoice.Simulated"/>.</summary>
    public FakePrinterTransport? Simulated
    {
        get { lock (_gate) return _simulated; }
    }

    /// <exception cref="PrinterNotConfiguredException">No printer has been selected yet.</exception>
    public PrinterSession RequireSession()
    {
        lock (_gate)
            return _session ?? throw new PrinterNotConfiguredException();
    }

    public PrinterSession? RequireSessionOrNull()
    {
        lock (_gate)
            return _session;
    }

    public async Task<PrinterStatus> RefreshAsync(CancellationToken ct) => await RequireSession().RefreshAsync(ct);

    public async Task DisconnectAsync()
    {
        PrinterSession? session;
        lock (_gate)
            session = _session;
        if (session is not null)
            await session.DisconnectAsync();
    }

    /// <summary>Feeds paper; also used for IPP Identify-Printer.</summary>
    public Task FeedAsync(int dots, CancellationToken ct) =>
        RequireSession().UseAsync((c, token) => c.SendAsync(Commands.FeedPaper(dots), Stream, token), ct);

    public StreamSettings Stream => new(Profile.ChunkSize, Profile.DelayMs);

    public async ValueTask DisposeAsync()
    {
        PrinterSession? session;
        lock (_gate)
        {
            session = _session;
            _session = null;
        }
        if (session is not null)
            await session.DisposeAsync();
    }

    private async Task ReplaceSessionAsync(ServiceSettings settings)
    {
        PrinterSession? old;
        lock (_gate)
            old = _session;
        if (old is not null)
            await old.DisposeAsync();
        CreateSession(settings);
        StatusChanged?.Invoke(Status);
    }

    private void CreateSession(ServiceSettings settings)
    {
        lock (_gate)
        {
            _selection = settings.Printer;
            _session = null;
            _simulated = null;
            if (settings.Printer is not { } printer)
                return;

            var target = printer.Transport switch
            {
                TransportChoice.Serial => new TransportTarget(TransportKind.Serial, printer.Address, printer.Port),
                TransportChoice.Simulated => new TransportTarget(TransportKind.Simulated),
                _ => new TransportTarget(TransportKind.Rfcomm, printer.Address),
            };
            if (printer.Transport == TransportChoice.Simulated)
                _simulated = new FakePrinterTransport { Description = $"Simulated {printer.Name}" };

            var simulated = _simulated;
            Func<IByteTransport> factory = _transportOverride is not null ? () => _transportOverride(printer)
                : simulated is not null ? () => simulated
                : () => TransportFactory.Create(target);

            var session = new PrinterSession(factory, target.ToString(), new MiniPrinter.Transport.SessionOptions
            {
                IdleTimeout = TimeSpan.FromSeconds(settings.IdleTimeoutSeconds),
            });
            session.StatusChanged += s => StatusChanged?.Invoke(s);
            _session = session;
            _logger.LogInformation("Printer {Name} ({Target}, profile {Profile})", printer.Name, target, printer.ProfileKey);
        }
    }
}

public sealed class PrinterNotConfiguredException() : Exception("No printer has been selected. Open the MiniPrinter tray app to choose one.");
