namespace MiniPrinter.Transport.Tests;

public class TransportHelpersTests
{
    [Theory]
    [InlineData("7A:E0:0C:1D:87:AE")]
    [InlineData("7a-e0-0c-1d-87-ae")]
    [InlineData("7AE00C1D87AE")]
    public void Bluetooth_address_parses_common_formats(string text)
    {
        var address = BluetoothAddress.Parse(text);
        Assert.Equal(0x7AE00C1D87AEUL, address.Value);
        Assert.Equal("7AE00C1D87AE", address.Compact);
        Assert.Equal("7A:E0:0C:1D:87:AE", address.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("COM5")]
    [InlineData("7A:E0:0C:1D:87")]
    public void Bluetooth_address_rejects_invalid_text(string text)
    {
        Assert.False(BluetoothAddress.TryParse(text, out _));
    }

    [Fact]
    public void Duplicate_name_error_means_printer_busy()
    {
        // HRESULT_FROM_WIN32(ERROR_DUP_NAME), as raised by SerialPort.Open on a Bluetooth port in use.
        var io = new IOException("No se conectó porque hay un nombre duplicado en la red.", unchecked((int)0x80070034));
        Assert.Equal(TransportErrorKind.Busy, SerialTransport.Translate(io).Kind);
    }

    [Fact]
    public void Semaphore_timeout_means_printer_unavailable()
    {
        var io = new IOException("timeout", unchecked((int)0x80070079));
        Assert.Equal(TransportErrorKind.Unavailable, SerialTransport.Translate(io).Kind);
    }
}
