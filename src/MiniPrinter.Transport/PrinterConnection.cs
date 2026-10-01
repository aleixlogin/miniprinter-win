using MiniPrinter.Protocol;

namespace MiniPrinter.Transport;

/// <summary>Stream settings for sending a job.</summary>
public sealed record StreamSettings(int ChunkSize = 180, int DelayMs = 4)
{
    /// <summary>How long a printer-requested pause may last before the job is failed.</summary>
    public TimeSpan PauseTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Protocol-level session on top of an <see cref="IByteTransport"/>: decodes incoming frames,
/// honours <c>AE</c> pause/resume flow control while streaming, and answers queries.
/// </summary>
public sealed class PrinterConnection : IAsyncDisposable
{
    private readonly IByteTransport _transport;
    private readonly FrameDecoder _decoder = new();
    private readonly object _gate = new();
    private readonly List<(byte Opcode, TaskCompletionSource<PrinterMessage> Reply)> _waiters = [];
    private TaskCompletionSource _resumed = CompletedSource();
    private CancellationTokenSource? _readCts;
    private Task? _readLoop;

    public PrinterConnection(IByteTransport transport)
    {
        _transport = transport;
    }

    public string Description => _transport.Description;
    public bool IsConnected => _transport.IsConnected;

    /// <summary>True while the printer has asked the host to stop sending.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>Raised for every decoded printer message (on the read-loop thread).</summary>
    public event Action<PrinterMessage>? MessageReceived;

    /// <summary>Raised when the link drops unexpectedly.</summary>
    public event Action<Exception?>? Disconnected;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
        SetPaused(false);
        _readCts = new CancellationTokenSource();
        _readLoop = Task.Run(() => ReadLoopAsync(_readCts.Token), CancellationToken.None);
    }

    /// <summary>
    /// Streams <paramref name="data"/> in chunks with a pause between them. Before each chunk it
    /// waits while the printer has requested a pause, failing after <see cref="StreamSettings.PauseTimeout"/>.
    /// Cancellation stops between chunks.
    /// </summary>
    public async Task SendAsync(ReadOnlyMemory<byte> data, StreamSettings settings, CancellationToken cancellationToken,
        IProgress<long>? progress = null)
    {
        var chunk = Math.Max(1, settings.ChunkSize);
        for (var offset = 0; offset < data.Length; offset += chunk)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitWhilePausedAsync(settings.PauseTimeout, cancellationToken).ConfigureAwait(false);

            var length = Math.Min(chunk, data.Length - offset);
            await _transport.WriteAsync(data.Slice(offset, length), cancellationToken).ConfigureAwait(false);
            progress?.Report(offset + length);

            if (settings.DelayMs > 0 && offset + length < data.Length)
                await Task.Delay(settings.DelayMs, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Sends a command and waits for the first reply with the same opcode.</summary>
    /// <exception cref="TimeoutException">No reply within <paramref name="timeout"/>.</exception>
    public async Task<T> QueryAsync<T>(byte[] command, TimeSpan timeout, CancellationToken cancellationToken)
        where T : PrinterMessage
    {
        var reply = new TaskCompletionSource<PrinterMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiter = (Opcode: command[2], Reply: reply);
        lock (_gate)
            _waiters.Add(waiter);
        try
        {
            await _transport.WriteAsync(command, cancellationToken).ConfigureAwait(false);
            var message = await reply.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return message as T ?? throw new InvalidDataException($"Unexpected reply {message.Frame}.");
        }
        finally
        {
            lock (_gate)
                _waiters.Remove(waiter);
        }
    }

    public Task<DeviceState> GetStateAsync(TimeSpan timeout, CancellationToken ct) =>
        QueryAsync<DeviceState>(Commands.GetDeviceState(), timeout, ct);

    public Task<DeviceInfo> GetInfoAsync(TimeSpan timeout, CancellationToken ct) =>
        QueryAsync<DeviceInfo>(Commands.GetDeviceInfo(), timeout, ct);

    public Task<DeviceId> GetIdAsync(TimeSpan timeout, CancellationToken ct) =>
        QueryAsync<DeviceId>(Commands.GetDeviceId(), timeout, ct);

    public async Task DisconnectAsync()
    {
        _readCts?.Cancel();
        await _transport.DisconnectAsync().ConfigureAwait(false);
        if (_readLoop is not null)
        {
            try { await _readLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _readLoop = null;
        _readCts?.Dispose();
        _readCts = null;
        SetPaused(false);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        await _transport.DisposeAsync().ConfigureAwait(false);
    }

    private async Task WaitWhilePausedAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task resumed;
        lock (_gate)
            resumed = _resumed.Task;
        if (resumed.IsCompleted)
            return;
        try
        {
            await resumed.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TransportException(TransportErrorKind.Unavailable,
                $"The printer paused the transfer and did not resume within {timeout.TotalSeconds:0} s.", ex);
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[512];
        Exception? failure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await _transport.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                foreach (var frame in _decoder.Feed(buffer.AsSpan(0, read)))
                    Dispatch(PrinterMessage.Parse(frame));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            SetPaused(false);
            FailWaiters(failure ?? new TransportException(TransportErrorKind.IoError, "The printer closed the connection."));
            Disconnected?.Invoke(failure);
        }
    }

    private void Dispatch(PrinterMessage message)
    {
        if (message is FlowControl flow)
            SetPaused(flow.Paused);

        lock (_gate)
        {
            var index = _waiters.FindIndex(w => w.Opcode == message.Frame.Command);
            if (index >= 0)
            {
                _waiters[index].Reply.TrySetResult(message);
                _waiters.RemoveAt(index);
            }
        }
        MessageReceived?.Invoke(message);
    }

    private void SetPaused(bool paused)
    {
        lock (_gate)
        {
            IsPaused = paused;
            if (paused && _resumed.Task.IsCompleted)
                _resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            else if (!paused)
                _resumed.TrySetResult();
        }
    }

    private void FailWaiters(Exception error)
    {
        lock (_gate)
        {
            foreach (var waiter in _waiters)
                waiter.Reply.TrySetException(error);
            _waiters.Clear();
        }
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
