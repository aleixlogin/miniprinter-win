using MiniPrinter.Protocol;

namespace MiniPrinter.Transport;

public enum LinkState
{
    Disconnected,
    Connecting,
    Connected,
    Error,
}

/// <summary>Snapshot of what is known about the printer and its link.</summary>
public sealed record PrinterStatus
{
    public LinkState Link { get; init; } = LinkState.Disconnected;
    public string? Target { get; init; }
    public DeviceState? State { get; init; }
    public string? Firmware { get; init; }
    public bool Printing { get; init; }
    public TransportErrorKind? LastErrorKind { get; init; }
    public string? LastError { get; init; }
    public DateTimeOffset? LastSeen { get; init; }

    /// <summary>While set and in the future, the session stays connected and polls (battery sampling).</summary>
    public DateTimeOffset? KeepAliveUntil { get; init; }

    /// <summary>Persistent mode (keep-alive option): never idle-disconnects, heartbeats and reconnects.</summary>
    public bool Persistent { get; init; }

    /// <summary>True while persistent mode is waiting to reconnect after losing the link.</summary>
    public bool Reconnecting { get; init; }
}

public sealed record SessionOptions
{
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan QueryTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public int ConnectAttempts { get; init; } = 3;
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Persistent mode: start in persistent (keep-alive) mode.</summary>
    public bool Persistent { get; init; }

    /// <summary>Persistent mode: interval between <c>A3</c> heartbeats.</summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Persistent mode: first and maximum wait between reconnection attempts.</summary>
    public TimeSpan ReconnectBaseDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ReconnectMaxDelay { get; init; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Owns the connection to one printer: connects on demand with retries, serialises all use of
/// the link (jobs and queries), polls <c>A3</c> while connected and disconnects when idle so other
/// apps can use the printer.
/// </summary>
public sealed class PrinterSession : IAsyncDisposable
{
    private readonly Func<IByteTransport> _transportFactory;
    private readonly SessionOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _maintenance;
    private PrinterConnection? _connection;
    private DateTimeOffset _lastUse = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPoll = DateTimeOffset.MinValue;
    private PrinterStatus _status;
    private TimeSpan _keepAlivePoll = TimeSpan.FromSeconds(60);
    private DateTimeOffset _lastConnectAttempt = DateTimeOffset.MinValue;
    private bool _persistent;
    private TimeSpan _heartbeat;
    private TimeSpan _reconnectDelay;
    private DateTimeOffset _nextReconnect = DateTimeOffset.MinValue;

    public PrinterSession(Func<IByteTransport> transportFactory, string target, SessionOptions? options = null)
    {
        _transportFactory = transportFactory;
        _options = options ?? new SessionOptions();
        _persistent = _options.Persistent;
        _heartbeat = _options.HeartbeatInterval;
        _reconnectDelay = _options.ReconnectBaseDelay;
        _status = new PrinterStatus { Target = target, Persistent = _persistent };
        _maintenance = Task.Run(() => MaintenanceLoopAsync(_lifetime.Token));
    }

    public PrinterStatus Status => _status;

    public event Action<PrinterStatus>? StatusChanged;

    /// <summary>Raised for every message from the printer (flow control, state replies…).</summary>
    public event Action<PrinterMessage>? MessageReceived;

    /// <summary>Connects if needed and refreshes state, info and battery.</summary>
    public Task<PrinterStatus> RefreshAsync(CancellationToken cancellationToken) =>
        UseAsync(async (connection, ct) =>
        {
            await QueryStateAsync(connection, ct).ConfigureAwait(false);
            return _status;
        }, cancellationToken);

    /// <summary>Runs <paramref name="action"/> with exclusive use of a connected link.</summary>
    public async Task<T> UseAsync<T>(Func<PrinterConnection, CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await action(connection, cancellationToken).ConfigureAwait(false);
            }
            catch (TransportException ex)
            {
                await DropAsync(ex).ConfigureAwait(false);
                throw;
            }
            finally
            {
                _lastUse = DateTimeOffset.UtcNow;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task UseAsync(Func<PrinterConnection, CancellationToken, Task> action, CancellationToken cancellationToken) =>
        UseAsync<bool>(async (c, ct) => { await action(c, ct).ConfigureAwait(false); return true; }, cancellationToken);

    /// <summary>
    /// Keeps the link open until <paramref name="until"/> (no idle disconnect), reconnecting if it
    /// drops and querying <c>A3</c> every <paramref name="poll"/>. Pass null to stop.
    /// </summary>
    public void KeepAlive(DateTimeOffset? until, TimeSpan? poll = null)
    {
        _keepAlivePoll = poll ?? TimeSpan.FromSeconds(60);
        _lastUse = DateTimeOffset.UtcNow;
        Update(s => s with { KeepAliveUntil = until });
    }

    /// <summary>
    /// Turns persistent (keep-alive) mode on or off: the link is kept open, an <c>A3</c> heartbeat is
    /// sent every <paramref name="heartbeat"/> and the session reconnects with a growing delay when
    /// the link drops. Turning it off returns to on-demand mode (idle disconnect applies again).
    /// </summary>
    public void SetPersistent(bool enabled, TimeSpan? heartbeat = null)
    {
        _heartbeat = heartbeat ?? _heartbeat;
        _persistent = enabled;
        _reconnectDelay = _options.ReconnectBaseDelay;
        _nextReconnect = DateTimeOffset.MinValue;
        _lastUse = DateTimeOffset.UtcNow;
        Update(s => s with { Persistent = enabled, Reconnecting = enabled && s.Reconnecting });
    }

    /// <summary>Marks the session as printing (shown in status).</summary>
    public void SetPrinting(bool printing) => Update(s => s with { Printing = printing });

    public async Task DisconnectAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await CloseAsync().ConfigureAwait(false);
            Update(s => s with { Link = LinkState.Disconnected });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        try { await _maintenance.ConfigureAwait(false); } catch (OperationCanceledException) { }
        await DisconnectAsync().ConfigureAwait(false);
        _lifetime.Dispose();
        _lock.Dispose();
    }

    private async Task<PrinterConnection> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsConnected: true })
            return _connection;

        await CloseAsync().ConfigureAwait(false);
        Update(s => s with { Link = LinkState.Connecting });

        TransportException? last = null;
        for (var attempt = 0; attempt < Math.Max(1, _options.ConnectAttempts); attempt++)
        {
            if (attempt > 0)
                await Task.Delay(_options.RetryBaseDelay * Math.Pow(2, attempt - 1), cancellationToken).ConfigureAwait(false);

            var connection = new PrinterConnection(_transportFactory());
            try
            {
                await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
                connection.MessageReceived += OnMessage;
                connection.Disconnected += OnDisconnected;
                _connection = connection;
                _lastUse = DateTimeOffset.UtcNow;
                Update(s => s with { Link = LinkState.Connected, LastError = null, LastErrorKind = null });
                await QueryStateAsync(connection, cancellationToken).ConfigureAwait(false);
                if (_status.Firmware is null)
                {
                    try
                    {
                        var info = await connection.GetInfoAsync(_options.QueryTimeout, cancellationToken).ConfigureAwait(false);
                        Update(s => s with { Firmware = info.Firmware });
                    }
                    catch (TimeoutException) { }
                }
                return connection;
            }
            catch (TransportException ex)
            {
                last = ex;
                await connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
                // Another app holding the link or an unknown device will not fix itself by retrying.
                if (ex.Kind is TransportErrorKind.Busy or TransportErrorKind.NotFound)
                    break;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
                throw;
            }
        }

        Update(s => s with { Link = LinkState.Error, LastError = last!.Message, LastErrorKind = last.Kind });
        throw last!;
    }

    private async Task QueryStateAsync(PrinterConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            var state = await connection.GetStateAsync(_options.QueryTimeout, cancellationToken).ConfigureAwait(false);
            _lastPoll = DateTimeOffset.UtcNow;
            Update(s => s with { State = state, LastSeen = DateTimeOffset.UtcNow });
        }
        catch (TimeoutException)
        {
            // A missing reply is not an alarm; keep the last known state.
        }
    }

    private async Task MaintenanceLoopAsync(CancellationToken cancellationToken)
    {
        var tick = TimeSpan.FromMilliseconds(Math.Clamp(_options.IdleTimeout.TotalMilliseconds / 4, 50, 1000));
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(tick, cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var keepAliveUntil = _status.KeepAliveUntil;
            if (keepAliveUntil is { } expired && now >= expired)
            {
                Update(s => s with { KeepAliveUntil = null });
                keepAliveUntil = null;
                _lastUse = now; // idle countdown restarts when sampling ends
            }
            var sampling = keepAliveUntil is not null;
            var persistent = _persistent;
            var keepAlive = sampling || persistent;
            if ((_connection is null && !keepAlive) || !_lock.Wait(0))
                continue; // a job holds the link: heartbeats never interleave with it
            try
            {
                var interval = persistent && sampling ? Min(_heartbeat, _keepAlivePoll)
                    : persistent ? _heartbeat : _keepAlivePoll;
                if (keepAlive)
                {
                    if (_connection is not { IsConnected: true })
                    {
                        if (persistent)
                            await ReconnectAsync(now, cancellationToken).ConfigureAwait(false);
                        else if (now - _lastConnectAttempt >= _keepAlivePoll)
                        {
                            _lastConnectAttempt = now;
                            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else if (now - _lastPoll >= interval)
                    {
                        await QueryStateAsync(_connection, cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (_connection is { IsConnected: true } connection)
                {
                    if (now - _lastUse >= _options.IdleTimeout)
                    {
                        await CloseAsync().ConfigureAwait(false);
                        Update(s => s with { Link = LinkState.Disconnected });
                    }
                    else if (now - _lastPoll >= _options.PollInterval)
                    {
                        await QueryStateAsync(connection, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (TransportException ex)
            {
                await DropAsync(ex).ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    /// <summary>Persistent mode: one reconnection attempt when due, doubling the wait after each failure.</summary>
    private async Task ReconnectAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (now < _nextReconnect)
            return;
        try
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            _reconnectDelay = _options.ReconnectBaseDelay;
            Update(s => s with { Reconnecting = false });
        }
        catch (TransportException ex)
        {
            _nextReconnect = DateTimeOffset.UtcNow + _reconnectDelay;
            _reconnectDelay = Min(_reconnectDelay * 2, _options.ReconnectMaxDelay);
            Update(s => s with { Link = LinkState.Connecting, Reconnecting = true, LastError = ex.Message, LastErrorKind = ex.Kind });
        }
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private void OnMessage(PrinterMessage message)
    {
        if (message is DeviceState state)
            Update(s => s with { State = state, LastSeen = DateTimeOffset.UtcNow });
        MessageReceived?.Invoke(message);
    }

    private void OnDisconnected(Exception? error)
    {
        Update(s => s with
        {
            Link = error is null ? LinkState.Disconnected : LinkState.Error,
            LastError = error?.Message,
            LastErrorKind = (error as TransportException)?.Kind ?? (error is null ? null : TransportErrorKind.IoError),
        });
    }

    private async Task DropAsync(TransportException error)
    {
        await CloseAsync().ConfigureAwait(false);
        Update(s => s with { Link = LinkState.Error, LastError = error.Message, LastErrorKind = error.Kind });
    }

    private async Task CloseAsync()
    {
        var connection = _connection;
        _connection = null;
        if (connection is null)
            return;
        connection.MessageReceived -= OnMessage;
        connection.Disconnected -= OnDisconnected;
        await connection.DisposeAsync().ConfigureAwait(false);
    }

    private void Update(Func<PrinterStatus, PrinterStatus> change)
    {
        PrinterStatus updated;
        lock (_lifetime)
        {
            updated = change(_status);
            if (updated == _status)
                return;
            _status = updated;
        }
        StatusChanged?.Invoke(updated);
    }
}
