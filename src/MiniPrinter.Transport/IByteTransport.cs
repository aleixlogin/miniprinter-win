namespace MiniPrinter.Transport;

/// <summary>A raw, bidirectional byte link to a printer (RFCOMM socket, serial port, test fake…).</summary>
public interface IByteTransport : IAsyncDisposable
{
    /// <summary>Human-readable target, e.g. "COM5" or "RFCOMM 7A:E0:0C:1D:87:AE".</summary>
    string Description { get; }

    bool IsConnected { get; }

    /// <exception cref="TransportException">The link could not be opened.</exception>
    Task ConnectAsync(CancellationToken cancellationToken);

    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

    /// <summary>Reads available bytes; returns 0 when the link has been closed.</summary>
    Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    Task DisconnectAsync();
}

public enum TransportErrorKind
{
    /// <summary>The configured printer/port does not exist or is not paired.</summary>
    NotFound,
    /// <summary>Another application holds the connection (Windows ERROR_DUP_NAME on Bluetooth COM ports).</summary>
    Busy,
    /// <summary>The printer did not answer: off, out of range or asleep.</summary>
    Unavailable,
    /// <summary>The link failed while in use.</summary>
    IoError,
}

public sealed class TransportException : Exception
{
    public TransportException(TransportErrorKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }

    public TransportErrorKind Kind { get; }

    /// <summary>Windows <c>ERROR_DUP_NAME</c> (52), reported when a Bluetooth link is already in use.</summary>
    public const int ErrorDupName = 52;
}
