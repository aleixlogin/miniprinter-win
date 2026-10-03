using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using MiniPrinter.Control;
using MiniPrinter.Escpos;
using MiniPrinter.Imaging;
using MiniPrinter.Protocol;
using MiniPrinter.Transport;

namespace MiniPrinter.Service;

/// <summary>What a RAW connection carries, decided once from its first bytes.</summary>
public enum RawContent
{
    NeedMore,
    Unknown,
    Png,
    Jpeg,
    Pdf,
    PwgRaster,
    EscPos,
    PlainText,
}

/// <summary>
/// The RAW/JetDirect port (9100): whatever a client sends is queued as a job, like the IPP and API entries.
/// Disabled by default; loopback only in <see cref="NetworkMode.Local"/>, every interface (with a firewall
/// rule for private networks) in <see cref="NetworkMode.Lan"/>. The port has no authentication, so it is
/// limited in connections, size and time, and every job shows its origin as <c>raw@address</c>.
/// </summary>
public sealed class RawPortHost : BackgroundService
{
    public const int MaxConnections = 4;
    public const int MaxBytes = 16 * 1024 * 1024;

    private readonly SettingsStore _settings;
    private readonly JobQueue _queue;
    private readonly PrinterManager _printer;
    private readonly PrintRequests _requests;
    private readonly ILogger<RawPortHost> _logger;
    private readonly SemaphoreSlim _restart = new(0);
    private readonly SemaphoreSlim _connections = new(MaxConnections, MaxConnections);
    private readonly object _gate = new();
    private RawPortDto _current = new(false, 9100, false, null, []);
    private ServiceSettings? _applied;   // the settings the listener (or its error / disabled state) was last started with
    private bool _firewallRule;

    public RawPortHost(SettingsStore settings, JobQueue queue, PrinterManager printer, PrintRequests requests, ILogger<RawPortHost> logger, IConfiguration configuration)
    {
        _settings = settings;
        _queue = queue;
        _printer = printer;
        _requests = requests;
        _logger = logger;
        EndOfJobSilence = TimeSpan.FromSeconds(configuration.GetValue("RawPort:SilenceSeconds", 2.0));   // overridable for tests
        IdleTimeout = TimeSpan.FromSeconds(configuration.GetValue("RawPort:IdleSeconds", 30.0));
        KanjiCodePage = configuration.GetValue("RawPort:KanjiCodePage", 932);
        _current = _current with { Port = settings.Current.RawPort, Enabled = settings.Current.RawPortEnabled };
        settings.Changed += (previous, current) =>
        {
            if (previous.RawPortEnabled != current.RawPortEnabled || previous.RawPort != current.RawPort || previous.NetworkMode != current.NetworkMode)
            {
                lock (_gate)
                    _current = _current with { Enabled = current.RawPortEnabled, Port = current.RawPort };
                Changed?.Invoke();
                _restart.Release();
            }
        };
    }

    /// <summary>Raised when the listener starts, stops or fails.</summary>
    public event Action? Changed;

    /// <summary>Time without data after which a received job is considered complete.</summary>
    public TimeSpan EndOfJobSilence { get; }

    /// <summary>Time without data after which a connection is closed.</summary>
    public TimeSpan IdleTimeout { get; }

    /// <summary>Code page of kanji mode (<c>FS &amp;</c>): 932 Shift-JIS, 936 GBK, 950 Big5, 949 EUC-KR (configuration <c>RawPort:KanjiCodePage</c>).</summary>
    public int KanjiCodePage { get; }

    public RawPortDto Current
    {
        get { lock (_gate) return _current; }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = _settings.Current;
            using var cycle = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            Task? accepting = null;
            TcpListener? listener = null;
            if (settings.RawPortEnabled)
            {
                try
                {
                    listener = new TcpListener(settings.NetworkMode == NetworkMode.Lan ? IPAddress.Any : IPAddress.Loopback, settings.RawPort);
                    listener.Start();

                    Publish(new RawPortDto(true, settings.RawPort, true, null, Addresses(settings)), settings);
                    _logger.LogInformation("RAW print port listening on {Addresses}", string.Join(", ", Current.Addresses));
                    if (settings.NetworkMode == NetworkMode.Lan)
                        _firewallRule = FirewallRule.Add(settings.RawPort, _logger, FirewallRule.RawName);
                    accepting = AcceptLoopAsync(listener, cycle.Token);
                }
                catch (SocketException ex)
                {
                    listener?.Stop();
                    listener = null;
                    var message = ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                        ? $"El puerto {settings.RawPort} está ocupado por otro programa."
                        : $"No se pudo abrir el puerto {settings.RawPort}: {ex.Message}";
                    Publish(new RawPortDto(true, settings.RawPort, false, message, []), settings);
                    _logger.LogWarning("RAW print port: {Message}", message);
                }
            }
            else
            {
                Publish(new RawPortDto(false, settings.RawPort, false, null, []), settings);
            }

            try
            {
                await _restart.WaitAsync(stoppingToken);
                // Apply the change only when no job is in progress, like the IPP listener.
                while (!_queue.IsIdle && !stoppingToken.IsCancellationRequested)
                    await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
            await StopAsync(listener, accepting, cycle);
        }
    }

    private async Task StopAsync(TcpListener? listener, Task? accepting, CancellationTokenSource cycle)
    {
        await cycle.CancelAsync();
        listener?.Stop();
        if (accepting is not null)
        {
            try
            {
                await accepting;
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                // the listener was closed
            }
        }
        if (_firewallRule)
        {
            FirewallRule.Remove(_logger, FirewallRule.RawName);
            _firewallRule = false;
        }
        _applied = null;
    }

    private void Publish(RawPortDto state, ServiceSettings applied)
    {
        lock (_gate)
        {
            _current = state;
            _applied = applied;
        }
        Changed?.Invoke();
    }

    /// <summary>Waits until the listener (or its error / disabled state) reflects the current settings.</summary>
    public async Task WaitForCurrentAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var settings = _settings.Current;
            ServiceSettings? applied;
            lock (_gate)
                applied = _applied;
            if (applied is not null && applied.RawPortEnabled == settings.RawPortEnabled && applied.RawPort == settings.RawPort
                && applied.NetworkMode == settings.NetworkMode)
                return;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("El puerto de impresión directa no terminó de reiniciarse.");
            await Task.Delay(50, ct);
        }
    }

    private static IReadOnlyList<string> Addresses(ServiceSettings settings)
    {
        var list = new List<string> { $"127.0.0.1:{settings.RawPort}" };
        if (settings.NetworkMode != NetworkMode.Lan)
            return list;
        list.Add($"{Environment.MachineName.ToLowerInvariant()}:{settings.RawPort}");
        list.AddRange(NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => $"{a.Address}:{settings.RawPort}"));
        return list;
    }

    // ---- connections ---------------------------------------------------------------------------------

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(ct);
            if (!_connections.Wait(0))
            {
                _logger.LogWarning("RAW print port: connection from {Remote} refused (limit of {Max} reached)", client.Client.RemoteEndPoint, MaxConnections);
                client.Dispose();
                continue;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleAsync(client, ct);
                }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
                {
                    // the client went away or the listener stopped
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "RAW print connection failed");
                }
                finally
                {
                    client.Dispose();
                    _connections.Release();
                }
            }, CancellationToken.None);
        }
    }

    /// <summary>Reads for at most <paramref name="timeout"/>; returns 0 on timeout or when the peer closed.</summary>
    private static async Task<(int Read, bool TimedOut)> ReadAsync(NetworkStream stream, byte[] buffer, TimeSpan timeout, CancellationToken ct)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timer.CancelAfter(timeout);
        try
        {
            return (await stream.ReadAsync(buffer, timer.Token), false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (0, true);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        var user = $"raw@{(client.Client.RemoteEndPoint as IPEndPoint)?.Address.MapToIPv4() ?? IPAddress.Loopback}";
        var stream = client.GetStream();
        var buffer = new byte[16 * 1024];
        var received = new MemoryStream();
        var total = 0;

        // 1. The first bytes decide what the connection carries.
        var kind = RawContent.NeedMore;
        var closed = false;
        while (kind == RawContent.NeedMore)
        {
            var (read, timedOut) = await ReadAsync(stream, buffer, received.Length == 0 ? IdleTimeout : EndOfJobSilence, ct);
            if (read == 0)
            {
                closed = true;
                if (timedOut && received.Length == 0)
                    _logger.LogInformation("RAW print connection from {User} closed: no data for {Seconds} s", user, IdleTimeout.TotalSeconds);
                break;
            }
            total += read;
            received.Write(buffer, 0, read);
            kind = Detect(received.GetBuffer().AsSpan(0, (int)Math.Min(received.Length, 512)), final: false);
        }
        if (kind == RawContent.NeedMore)
            kind = received.Length == 0 ? RawContent.Unknown : Detect(received.GetBuffer().AsSpan(0, (int)Math.Min(received.Length, 512)), final: true);
        if (received.Length == 0)
            return;
        if (kind == RawContent.Unknown)
        {
            _logger.LogWarning("RAW print connection from {User} rejected: unrecognised content", user);
            return;
        }

        // 2. Hand it over according to its type.
        if (kind == RawContent.EscPos)
        {
            await RunEscPosAsync(stream, received, closed, total, user, ct);
            return;
        }

        while (!closed)
        {
            var (read, _) = await ReadAsync(stream, buffer, EndOfJobSilence, ct);
            if (read == 0)
                break;
            total += read;
            if (total > MaxBytes)
            {
                _logger.LogWarning("RAW print connection from {User} closed: more than {Mb} MB", user, MaxBytes / 1024 / 1024);
                return;
            }
            received.Write(buffer, 0, read);
        }
        SubmitDocument(kind, received, user);
    }

    private void SubmitDocument(RawContent kind, MemoryStream data, string user)
    {
        try
        {
            if (kind == RawContent.PlainText)
            {
                _requests.PrintText(new TextPrintRequest { Text = Encoding.UTF8.GetString(data.GetBuffer(), 0, (int)data.Length).Replace("\0", "") }, user);
                return;
            }
            var (format, name) = kind switch
            {
                RawContent.Png => ("image/png", "Raw PNG"),
                RawContent.Jpeg => ("image/jpeg", "Raw JPEG"),
                RawContent.Pdf => ("application/pdf", "Raw PDF"),
                _ => ("image/pwg-raster", "Raw PWG"),
            };
            data.Position = 0;
            var job = _queue.CreateJob(name, user);
            _queue.SubmitDocument(job.Id, new MemoryStream(data.ToArray(), writable: false), format, lastDocument: true);
            _logger.LogInformation("RAW print: {Name} ({Bytes} bytes) from {User} queued as job {Id}", name, data.Length, user, job.Id);
        }
        catch (Exception ex) when (ex is PrintRequestException or NotSupportedException)
        {
            _logger.LogWarning("RAW print from {User} not queued: {Message}", user, ex.Message);
        }
    }

    private async Task RunEscPosAsync(NetworkStream stream, MemoryStream first, bool closed, int total, string user, CancellationToken ct)
    {
        var interpreter = new EscposInterpreter(StatusOf, message => _logger.LogDebug("{Message}", message), kanjiCodePage: KanjiCodePage);
        var buffer = new byte[16 * 1024];
        var tickets = 0;

        async Task PumpAsync()
        {
            var responses = interpreter.DrainResponses();
            if (responses.Length > 0)
                await stream.WriteAsync(responses, ct);
            foreach (var ticket in interpreter.DrainTickets())
            {
                tickets++;
                var job = _queue.SubmitBitmaps("Ticket ESC/POS", [ticket], isText: true, user);
                _logger.LogInformation("RAW print: ESC/POS ticket {Number} ({Rows} rows) from {User} queued as job {Id}", tickets, ticket.Height, user, job.Id);
            }
        }

        interpreter.Feed(first.GetBuffer().AsSpan(0, (int)first.Length));
        await PumpAsync();
        while (!closed)
        {
            // After content, silence ends the ticket; with nothing pending, the connection may idle longer.
            var timeout = interpreter.HasPending ? EndOfJobSilence : IdleTimeout;
            var (read, timedOut) = await ReadAsync(stream, buffer, timeout, ct);
            if (read == 0)
            {
                if (timedOut && interpreter.HasPending)
                {
                    interpreter.EndTicket();
                    await PumpAsync();
                    continue;
                }
                break;
            }
            total += read;
            if (total > MaxBytes)
            {
                _logger.LogWarning("RAW print connection from {User} closed: more than {Mb} MB", user, MaxBytes / 1024 / 1024);
                return;
            }
            interpreter.Feed(buffer.AsSpan(0, read));
            await PumpAsync();
            if (interpreter.Overflowed)
            {
                _logger.LogWarning("RAW print connection from {User} closed: it asked for more paper than a ticket connection may (limit {Rows} dots)", user, EscposInterpreter.MaxTotalRows);
                interpreter.EndTicket();
                await PumpAsync();
                return;
            }
        }
        interpreter.EndTicket();
        await PumpAsync();
    }

    private PrinterCondition? StatusOf()
    {
        var status = _printer.Status;
        if (status.Link != LinkState.Connected || status.State is not { } state)
            return null;
        return new PrinterCondition(PaperOut: state.Alarms.HasFlag(PrinterAlarms.OutOfPaper), Error: state.Alarms.HasFlag(PrinterAlarms.Overheated));
    }

    // ---- content detection ----------------------------------------------------------------------------

    private static readonly byte[][] Magic =
    [
        [0x89, (byte)'P', (byte)'N', (byte)'G'],
        [0xFF, 0xD8, 0xFF],
        [(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-'],
        [(byte)'R', (byte)'a', (byte)'S', (byte)'2'],
        [(byte)'R', (byte)'a', (byte)'S', (byte)'t'],
    ];

    /// <summary>Classifies a connection by its first bytes (the first 512 at most).</summary>
    public static RawContent Detect(ReadOnlySpan<byte> head, bool final)
    {
        if (head.IsEmpty)
            return final ? RawContent.Unknown : RawContent.NeedMore;
        if (head.StartsWith(Magic[0])) return RawContent.Png;
        if (head.StartsWith(Magic[1])) return RawContent.Jpeg;
        if (head.StartsWith(Magic[2])) return RawContent.Pdf;
        if (head.StartsWith(Magic[3]) || head.StartsWith(Magic[4])) return RawContent.PwgRaster;
        if (!final)
        {
            // A chunk too short to tell a signature apart from anything else waits for more bytes.
            foreach (var magic in Magic)
                if (head.Length < magic.Length && magic.AsSpan(0, head.Length).SequenceEqual(head))
                    return RawContent.NeedMore;
        }
        if (head[0] is 0x1B or 0x1D or 0x10)
            return RawContent.EscPos;
        if (head.IndexOfAny((byte)0x1B, (byte)0x1D, (byte)0x10) >= 0)
            return RawContent.EscPos;

        // Plain text: UTF-8 (a character cut by the 512-byte window is fine) without other control characters.
        foreach (var b in head)
            if (b < 0x20 && b is not (0x09 or 0x0A or 0x0D or 0x0C))
                return RawContent.Unknown;
        if (System.Text.Unicode.Utf8.IsValid(head))
            return RawContent.PlainText;
        for (var cut = 1; head.Length == 512 && cut <= 3; cut++)
            if (System.Text.Unicode.Utf8.IsValid(head[..^cut]))
                return RawContent.PlainText;
        return RawContent.Unknown;
    }
}
