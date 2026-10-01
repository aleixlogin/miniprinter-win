using System.Runtime.Versioning;

namespace MiniPrinter.Transport;

public enum TransportKind
{
    /// <summary>WinRT RFCOMM socket addressed by MAC.</summary>
    Rfcomm,
    /// <summary>Bluetooth virtual COM port.</summary>
    Serial,
    /// <summary>In-memory simulated printer.</summary>
    Simulated,
}

/// <summary>Where and how to reach a printer.</summary>
public sealed record TransportTarget(TransportKind Kind, string? Address = null, string? Port = null)
{
    public override string ToString() => Kind switch
    {
        TransportKind.Rfcomm => $"rfcomm:{Address}",
        TransportKind.Serial => $"serial:{Port ?? Address}",
        _ => "simulated",
    };
}

[SupportedOSPlatform("windows10.0.19041.0")]
public static class TransportFactory
{
    /// <summary>
    /// Creates the transport for <paramref name="target"/>. For serial targets without an explicit
    /// port, the outgoing COM port bound to the MAC address is looked up.
    /// </summary>
    public static IByteTransport Create(TransportTarget target) => target.Kind switch
    {
        TransportKind.Rfcomm => new RfcommTransport(BluetoothAddress.Parse(
            target.Address ?? throw new ArgumentException("RFCOMM target needs a Bluetooth address."))),
        TransportKind.Serial => new SerialTransport(target.Port ?? ResolvePort(target.Address)),
        TransportKind.Simulated => new FakePrinterTransport(),
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    private static string ResolvePort(string? address)
    {
        if (!BluetoothAddress.TryParse(address, out var mac))
            throw new ArgumentException("Serial target needs a port name or a Bluetooth address.");
        return SerialTransport.FindPortForAddress(mac)
            ?? throw new TransportException(TransportErrorKind.NotFound, $"No Bluetooth serial port is bound to {mac}.");
    }
}
