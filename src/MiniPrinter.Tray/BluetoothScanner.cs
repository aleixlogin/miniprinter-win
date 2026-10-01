using System.Collections.Concurrent;
using MiniPrinter.Protocol.Catalog;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace MiniPrinter.Tray;

/// <summary>A Bluetooth Classic device seen by the scanner.</summary>
public sealed record FoundDevice(string Id, string Name, string Address, bool Paired, CatalogMatch? Match)
{
    public bool IsKnownPrinter => Match is not null;
    public string Model => Match is null ? "—" : $"{Match.Model.Key} ({Match.Profile.Key})";
    public string PairedText => Paired ? "Sí" : "No";
}

/// <summary>Discovers paired and nearby Bluetooth Classic devices and pairs them (design.md D5).</summary>
public sealed class BluetoothScanner : IDisposable
{
    private readonly ConcurrentDictionary<string, FoundDevice> _devices = new();
    private DeviceWatcher? _paired;
    private DeviceWatcher? _nearby;

    public event Action<FoundDevice>? DeviceFound;
    public event Action<string>? DeviceRemoved;
    public event Action? Completed;

    public IReadOnlyCollection<FoundDevice> Devices => _devices.Values.ToList();

    public void Start()
    {
        Stop();
        _devices.Clear();
        _paired = Create(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
        _nearby = Create(BluetoothDevice.GetDeviceSelectorFromPairingState(false));
        _paired.Start();
        _nearby.Start();
    }

    public void Stop()
    {
        foreach (var watcher in new[] { _paired, _nearby })
        {
            if (watcher?.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                watcher.Stop();
        }
        _paired = null;
        _nearby = null;
    }

    /// <summary>Pairs a device, accepting confirmation prompts and answering PIN requests with "0000".</summary>
    public static async Task<string> PairAsync(string deviceId)
    {
        var info = await DeviceInformation.CreateFromIdAsync(deviceId);
        if (info.Pairing.IsPaired)
            return "Ya estaba emparejada.";

        void OnRequested(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
        {
            if (args.PairingKind == DevicePairingKinds.ProvidePin)
                args.Accept("0000");
            else
                args.Accept();
        }

        info.Pairing.Custom.PairingRequested += OnRequested;
        try
        {
            var result = await info.Pairing.Custom.PairAsync(
                DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ProvidePin | DevicePairingKinds.DisplayPin | DevicePairingKinds.ConfirmPinMatch);
            return result.Status switch
            {
                DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired => "Emparejada correctamente.",
                DevicePairingResultStatus.AuthenticationFailure or DevicePairingResultStatus.Failed =>
                    "No se pudo emparejar (¿PIN distinto de 0000?). Prueba desde Configuración > Bluetooth.",
                _ => $"Emparejamiento: {result.Status}",
            };
        }
        finally
        {
            info.Pairing.Custom.PairingRequested -= OnRequested;
        }
    }

    public void Dispose() => Stop();

    private DeviceWatcher Create(string selector)
    {
        var watcher = DeviceInformation.CreateWatcher(selector, ["System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsPaired"],
            DeviceInformationKind.AssociationEndpoint);
        watcher.Added += async (_, info) => await AddAsync(info);
        watcher.Updated += (sender, update) =>
        {
            if (_devices.TryGetValue(update.Id, out var existing)
                && update.Properties.TryGetValue("System.Devices.Aep.IsPaired", out var paired) && paired is bool isPaired)
                Publish(existing with { Paired = isPaired });
        };
        watcher.Removed += (sender, update) =>
        {
            if (_devices.TryRemove(update.Id, out _))
                DeviceRemoved?.Invoke(update.Id);
        };
        watcher.EnumerationCompleted += (_, _) => Completed?.Invoke();
        return watcher;
    }

    private async Task AddAsync(DeviceInformation info)
    {
        var address = info.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var a) ? a as string : null;
        if (string.IsNullOrEmpty(address))
        {
            try
            {
                using var device = await BluetoothDevice.FromIdAsync(info.Id);
                if (device is not null)
                    address = string.Join(':', BitConverter.GetBytes(device.BluetoothAddress).Take(6).Reverse().Select(b => b.ToString("X2")));
            }
            catch (Exception)
            {
                // Device vanished or access denied; skip it.
            }
        }
        if (string.IsNullOrEmpty(address) || string.IsNullOrWhiteSpace(info.Name))
            return;

        var name = info.Name.Trim();
        Publish(new FoundDevice(info.Id, name, address.ToUpperInvariant(), info.Pairing.IsPaired, PrinterCatalog.Default.Detect(name)));
    }

    private void Publish(FoundDevice device)
    {
        _devices[device.Id] = device;
        DeviceFound?.Invoke(device);
    }
}
