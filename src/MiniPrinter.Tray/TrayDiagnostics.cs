using MiniPrinter.Control;
using MiniPrinter.Gui;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace MiniPrinter.Tray;

/// <summary>What the diagnostics ask of this PC: the service, the Bluetooth radio and the paired devices.</summary>
internal sealed class TrayDiagnosticsSource(ServiceConnection service) : IDiagnosticsSource
{
    public PrinterSelection? Printer => service.Status?.Printer;

    public Task<IReadOnlyList<DiagnosticCheckDto>> GetServiceChecksAsync() => service.Client.GetDiagnosticsAsync();

    public async Task<bool?> IsBluetoothOnAsync()
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
                return null;
            var bluetooth = (await Radio.GetRadiosAsync()).Where(r => r.Kind == RadioKind.Bluetooth).ToList();
            // A PC without a Bluetooth radio cannot be said to have it "off": the check is unknown.
            return bluetooth.Count == 0 ? null : bluetooth.Any(r => r.State == RadioState.On);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<bool?> IsPairedAsync(PrinterSelection printer)
    {
        if (printer.Transport == TransportChoice.Serial)
            return true; // a COM port: there is nothing to pair
        try
        {
            var wanted = Normalize(printer.Address);
            var paired = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
            foreach (var info in paired)
            {
                using var device = await BluetoothDevice.FromIdAsync(info.Id);
                if (device is not null && Normalize(string.Join(':', BitConverter.GetBytes(device.BluetoothAddress).Take(6).Reverse().Select(b => b.ToString("X2")))) == wanted)
                    return true;
            }
            return false;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Normalize(string address) =>
        new(address.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
}
