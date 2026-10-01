using System.IO.Ports;
using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace MiniPrinter.Transport;

/// <summary>
/// Printer link over a Windows "Standard Serial over Bluetooth link" COM port (SPP).
/// Opening the port makes Windows establish the RFCOMM connection.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class SerialTransport : IByteTransport
{
    private readonly string _portName;
    private SerialPort? _port;

    public SerialTransport(string portName)
    {
        _portName = portName;
    }

    public string Description => _portName;
    public bool IsConnected => _port?.IsOpen == true;

    public Task ConnectAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (!SerialPort.GetPortNames().Contains(_portName, StringComparer.OrdinalIgnoreCase))
            throw new TransportException(TransportErrorKind.NotFound, $"Port {_portName} does not exist.");

        // Baud rate is irrelevant for a virtual Bluetooth port but must be valid.
        var port = new SerialPort(_portName, 115200, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = SerialPort.InfiniteTimeout,
            WriteTimeout = 10_000,
        };
        try
        {
            port.Open();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            port.Dispose();
            throw Translate(ex);
        }
        _port = port;
    }, cancellationToken);

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var port = _port ?? throw new TransportException(TransportErrorKind.IoError, "Not connected.");
        try
        {
            await port.BaseStream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            await port.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            throw new TransportException(TransportErrorKind.IoError, $"Write to {_portName} failed: {ex.Message}", ex);
        }
    }

    public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var port = _port;
        if (port is null || !port.IsOpen)
            return 0;
        try
        {
            // Serial reads ignore the token on Windows; closing the port completes them instead.
            await using var registration = cancellationToken.Register(() => { try { port.Close(); } catch { } });
            return await port.BaseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            if (!port.IsOpen)
                return 0;
            throw new TransportException(TransportErrorKind.IoError, $"Read from {_portName} failed: {ex.Message}", ex);
        }
    }

    public Task DisconnectAsync()
    {
        var port = _port;
        _port = null;
        if (port is not null)
        {
            try { port.Close(); } catch (IOException) { }
            port.Dispose();
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => new(DisconnectAsync());

    internal static TransportException Translate(Exception ex)
    {
        var win32 = ex.HResult & 0xFFFF;
        if (win32 == TransportException.ErrorDupName || ex is UnauthorizedAccessException)
            return new TransportException(TransportErrorKind.Busy,
                "The printer is in use by another application (close TiMini-Print or the mobile app).", ex);
        // ERROR_SEM_TIMEOUT (121) / ERROR_GEN_FAILURE (31): the remote device did not answer.
        if (win32 is 121 or 31 or 1167)
            return new TransportException(TransportErrorKind.Unavailable, "The printer did not respond (is it on and in range?).", ex);
        return new TransportException(TransportErrorKind.IoError, ex.Message, ex);
    }

    /// <summary>
    /// Finds the outgoing SPP COM port Windows created for <paramref name="address"/>. Outgoing
    /// ports carry the remote MAC in their PnP id (…_LOCALMFG&amp;0002\…_7AE00C1D87AE_C00000000);
    /// incoming ones use 000000000000.
    /// </summary>
    public static string? FindPortForAddress(BluetoothAddress address)
    {
        const string sppService = "{00001101-0000-1000-8000-00805F9B34FB}";
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'BTHENUM%'");
        foreach (var entity in searcher.Get().Cast<ManagementObject>())
        {
            var id = entity["PNPDeviceID"] as string ?? "";
            var name = entity["Name"] as string ?? "";
            if (!id.Contains(sppService, StringComparison.OrdinalIgnoreCase)
                || !id.Contains("&" + address.Compact + "_", StringComparison.OrdinalIgnoreCase))
                continue;
            var match = ComName().Match(name);
            if (match.Success)
                return match.Groups[1].Value;
        }
        return null;
    }

    [GeneratedRegex(@"\((COM\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex ComName();
}
