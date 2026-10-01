using System.Globalization;

namespace MiniPrinter.Transport;

/// <summary>A 48-bit Bluetooth device address.</summary>
public readonly record struct BluetoothAddress(ulong Value)
{
    /// <summary>Parses "7A:E0:0C:1D:87:AE", "7A-E0-…" or "7AE00C1D87AE".</summary>
    public static BluetoothAddress Parse(string text)
    {
        if (!TryParse(text, out var address))
            throw new FormatException($"'{text}' is not a Bluetooth address.");
        return address;
    }

    public static bool TryParse(string? text, out BluetoothAddress address)
    {
        address = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var hex = text.Replace(":", "").Replace("-", "").Trim();
        if (hex.Length != 12 || !ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;
        address = new BluetoothAddress(value);
        return true;
    }

    /// <summary>12 upper-case hex digits without separators, as used in Windows PnP ids.</summary>
    public string Compact => Value.ToString("X12", CultureInfo.InvariantCulture);

    public override string ToString()
    {
        var compact = Compact;
        return string.Join(':', Enumerable.Range(0, 6).Select(i => compact.Substring(i * 2, 2)));
    }
}
