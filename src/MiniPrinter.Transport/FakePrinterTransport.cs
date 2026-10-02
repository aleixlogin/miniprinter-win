using System.Threading.Channels;
using MiniPrinter.Protocol;

namespace MiniPrinter.Transport;

/// <summary>
/// In-memory printer used by tests and by the service's "simulated printer" mode. Records every
/// write, answers <c>A3</c>/<c>A8</c>/<c>BB</c> like the real X5h, and lets tests inject
/// notifications such as flow-control pauses.
/// </summary>
public sealed class FakePrinterTransport : IByteTransport
{
    private Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
    private readonly FrameDecoder _decoder = new();
    private readonly List<byte[]> _writes = [];
    private readonly object _gate = new();

    public string Description { get; init; } = "Simulated printer";
    public bool IsConnected { get; private set; }

    /// <summary>Alarm byte reported in <c>A3</c> replies.</summary>
    public byte AlarmByte { get; set; }

    /// <summary>When false, queries are not answered (to test timeouts).</summary>
    public bool AnswerQueries { get; set; } = true;

    /// <summary>When set, <see cref="ConnectAsync"/> throws it.</summary>
    public TransportException? ConnectError { get; set; }

    /// <summary>Called for each write (after recording), e.g. to inject a pause mid-job.</summary>
    public Action<FakePrinterTransport, byte[]>? OnWrite { get; set; }

    public int ConnectCount { get; private set; }

    public IReadOnlyList<byte[]> Writes
    {
        get { lock (_gate) return _writes.ToList(); }
    }

    public byte[] AllWrittenBytes
    {
        get { lock (_gate) return _writes.SelectMany(w => w).ToArray(); }
    }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (ConnectError is not null)
            throw ConnectError;
        ConnectCount++;
        _incoming = Channel.CreateUnbounded<byte[]>();
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (!IsConnected)
            throw new TransportException(TransportErrorKind.IoError, "Not connected.");
        var copy = data.ToArray();
        lock (_gate)
            _writes.Add(copy);

        foreach (var frame in _decoder.Feed(copy))
        {
            if (!AnswerQueries)
                continue;
            var reply = frame.Command switch
            {
                Opcode.GetDeviceState => Frame.Encode(Opcode.GetDeviceState, [AlarmByte, 0x0E, 0x28], Frame.FlagsFromPrinter),
                Opcode.GetDeviceInfo => Frame.Encode(Opcode.GetDeviceInfo, Convert.FromHexString("780003332E302E354400"), Frame.FlagsFromPrinter),
                Opcode.GetDeviceId => Frame.Encode(Opcode.GetDeviceId, new byte[6], Frame.FlagsFromPrinter),
                _ => null,
            };
            if (reply is not null)
                Inject(reply);
        }
        OnWrite?.Invoke(this, copy);
        return Task.CompletedTask;
    }

    /// <summary>Simulates the printer dropping the link (switched off, out of range).</summary>
    public void SimulateDrop()
    {
        IsConnected = false;
        _incoming.Writer.TryComplete();
    }

    /// <summary>Queues bytes as if the printer had sent them.</summary>
    public void Inject(byte[] bytes) => _incoming.Writer.TryWrite(bytes);

    public void InjectPause() => Inject(Frame.Encode(Opcode.FlowControl, [0x10], Frame.FlagsFromPrinter));

    public void InjectResume() => Inject(Frame.Encode(Opcode.FlowControl, [0x00], Frame.FlagsFromPrinter));

    public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            var chunk = await _incoming.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            chunk.CopyTo(buffer);
            return chunk.Length;
        }
        catch (ChannelClosedException)
        {
            return 0;
        }
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        _incoming.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => new(DisconnectAsync());
}
