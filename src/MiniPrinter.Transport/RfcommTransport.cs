using System.Runtime.Versioning;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;

namespace MiniPrinter.Transport;

/// <summary>
/// Printer link over Bluetooth Classic RFCOMM (SPP service) using WinRT sockets. The printer is
/// addressed by MAC, so it does not depend on which COM number Windows assigned.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class RfcommTransport : IByteTransport
{
    private readonly BluetoothAddress _address;
    private StreamSocket? _socket;
    private Stream? _input;
    private Stream? _output;

    public RfcommTransport(BluetoothAddress address)
    {
        _address = address;
    }

    public string Description => $"RFCOMM {_address}";
    public bool IsConnected => _socket is not null;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        BluetoothDevice? device;
        try
        {
            device = await BluetoothDevice.FromBluetoothAddressAsync(_address.Value).AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new TransportException(TransportErrorKind.NotFound, $"Bluetooth device {_address} is not available: {ex.Message}", ex);
        }
        if (device is null)
            throw new TransportException(TransportErrorKind.NotFound, $"Bluetooth device {_address} is not paired or not known to Windows.");

        using (device)
        {
            var services = await device.GetRfcommServicesForIdAsync(RfcommServiceId.SerialPort, BluetoothCacheMode.Uncached)
                .AsTask(cancellationToken).ConfigureAwait(false);
            if (services.Error != BluetoothError.Success || services.Services.Count == 0)
            {
                // Uncached lookups need the printer to answer; fall back to the cached SDP record.
                services = await device.GetRfcommServicesForIdAsync(RfcommServiceId.SerialPort, BluetoothCacheMode.Cached)
                    .AsTask(cancellationToken).ConfigureAwait(false);
            }
            if (services.Services.Count == 0)
            {
                var kind = services.Error == BluetoothError.DeviceNotConnected ? TransportErrorKind.Unavailable : TransportErrorKind.NotFound;
                throw new TransportException(kind, $"{_address} does not expose a serial port service ({services.Error}).");
            }

            var service = services.Services[0];
            var socket = new StreamSocket();
            try
            {
                await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName,
                        SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication)
                    .AsTask(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                socket.Dispose();
                throw SerialTransport.Translate(ex);
            }
            finally
            {
                service.Dispose();
            }

            _socket = socket;
            _input = socket.InputStream.AsStreamForRead(bufferSize: 0);
            _output = socket.OutputStream.AsStreamForWrite(bufferSize: 0);
        }
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var output = _output ?? throw new TransportException(TransportErrorKind.IoError, "Not connected.");
        try
        {
            await output.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new TransportException(TransportErrorKind.IoError, $"Write to {_address} failed: {ex.Message}", ex);
        }
    }

    public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var input = _input;
        if (input is null)
            return 0;
        try
        {
            return await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception ex) when (_socket is null)
        {
            _ = ex;
            return 0;
        }
        catch (Exception ex)
        {
            throw new TransportException(TransportErrorKind.IoError, $"Read from {_address} failed: {ex.Message}", ex);
        }
    }

    public Task DisconnectAsync()
    {
        var socket = _socket;
        _socket = null;
        _input?.Dispose();
        _output?.Dispose();
        _input = null;
        _output = null;
        socket?.Dispose();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => new(DisconnectAsync());
}
