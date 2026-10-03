using System.Collections.ObjectModel;
using System.Globalization;
using MiniPrinter.Control;

namespace MiniPrinter.Gui;

/// <summary>What the tools of the RAW port ask of the service.</summary>
public interface IRawPortGateway
{
    Task<JobDto> SendTestAsync();

    Task<IReadOnlyList<RawClientDto>> GetClientsAsync();
}

/// <summary>A recent client of the RAW port as the list shows it.</summary>
public sealed record RawClientRow(string Time, string Address, string Kind, string Size, string Detail, string Result, bool Succeeded)
{
    public static RawClientRow From(RawClientDto client)
    {
        var detail = client.Tickets > 0
            ? Strings.Get(client.Tickets == 1 ? "Raw.Client.OneTicket" : "Raw.Client.Tickets", client.Tickets)
            : client.JobId is { } job ? Strings.Get("Raw.Client.Job", job) : "";
        var result = client.Result switch
        {
            RawClientResult.Queued => Strings.Get("Raw.Result.Queued"),
            RawClientResult.Rejected => Strings.Get("Raw.Result.Rejected"),
            RawClientResult.ClosedByLimit => Strings.Get("Raw.Result.ClosedByLimit"),
            RawClientResult.RefusedLimit => Strings.Get("Raw.Result.RefusedLimit"),
            RawClientResult.Empty => Strings.Get("Raw.Result.Empty"),
            RawClientResult.Error => Strings.Get("Raw.Result.Error"),
            var other => other,
        };
        return new RawClientRow(client.Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture), client.Address, client.Kind, FormatSize(client.Bytes),
            detail, result, client.Result == RawClientResult.Queued);
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
    };
}

/// <summary>
/// The tools of the direct printing section: the addresses to use (copy, QR code), the test ticket and the recent clients. What can be
/// done depends on the state of the port: nothing is offered while it is off or does not listen.
/// </summary>
public sealed class RawPortToolsViewModel : ObservableObject
{
    private RawPortDto? _port;
    private NetworkMode _mode;
    private string _testResult = "";
    private bool _testFailed;

    public RawPortToolsViewModel()
    {
        CopyAddress = new RelayCommand(p => CopyRequested?.Invoke(p as string ?? BestAddress ?? ""), _ => CanCopy);
        ShowQr = new RelayCommand(p => QrRequested?.Invoke(p as string ?? BestAddress ?? ""), _ => CanCopy);
        SendTest = new AsyncCommand(SendTestAsync, () => CanTest && Gateway is not null);
        Refresh = new AsyncCommand(RefreshClientsAsync, () => Gateway is not null);
    }

    public IRawPortGateway? Gateway { get; set; }

    /// <summary>Raised with the text to put in the clipboard.</summary>
    public event Action<string>? CopyRequested;

    /// <summary>Raised with the text to show as a QR code.</summary>
    public event Action<string>? QrRequested;

    public RelayCommand CopyAddress { get; }

    public RelayCommand ShowQr { get; }

    public AsyncCommand SendTest { get; }

    public AsyncCommand Refresh { get; }

    public ObservableCollection<RawClientRow> Clients { get; } = [];

    /// <summary>Takes the state of the port the service reports.</summary>
    public void Update(RawPortDto? port, NetworkMode mode)
    {
        _port = port;
        _mode = mode;
        OnPropertyChanged(string.Empty);
        CopyAddress.RaiseCanExecuteChanged();
        ShowQr.RaiseCanExecuteChanged();
        SendTest.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// The addresses worth giving to someone: on this PC only, the loopback one; on the local network, the ones of the network
    /// (the name of the machine and its addresses), because the loopback one is of no use from another device.
    /// </summary>
    public IReadOnlyList<string> Addresses
    {
        get
        {
            if (_port is not { Listening: true })
                return [];
            var all = _port.Addresses;
            if (_mode != NetworkMode.Lan)
                return [.. all.Where(IsLoopback).DefaultIfEmpty(all.FirstOrDefault() ?? "")];
            var network = all.Where(a => !IsLoopback(a)).ToList();
            return network.Count > 0 ? network : [.. all];
        }
    }

    /// <summary>The address to copy when there is only one thing to choose from: the first useful one.</summary>
    public string? BestAddress => Addresses.FirstOrDefault(a => a.Length > 0);

    public bool HasSeveralAddresses => Addresses.Count > 1;

    public bool CanCopy => BestAddress is not null;

    public bool CanTest => _port is { Enabled: true, Listening: true };

    /// <summary>On this PC only the port cannot be reached from another device: the section says how to change it.</summary>
    public bool NeedsNetworkNotice => _port is { Listening: true } && _mode != NetworkMode.Lan;

    public string TestResult
    {
        get => _testResult;
        private set => Set(ref _testResult, value);
    }

    public bool TestFailed
    {
        get => _testFailed;
        private set => Set(ref _testFailed, value);
    }

    private static bool IsLoopback(string address) =>
        address.StartsWith("127.", StringComparison.Ordinal) || address.StartsWith("localhost", StringComparison.OrdinalIgnoreCase);

    private async Task SendTestAsync()
    {
        TestFailed = false;
        TestResult = Strings.Get("Raw.Test.Sending");
        try
        {
            var job = await Gateway!.SendTestAsync();
            TestResult = Strings.Get("Raw.Test.Sent", job.Id);
        }
        catch (Exception ex)
        {
            TestFailed = true;
            TestResult = ex.Message;
        }
        await RefreshClientsAsync();
    }

    /// <summary>Reads the recent clients (newest first) and replaces the list.</summary>
    public async Task RefreshClientsAsync()
    {
        if (Gateway is null)
            return;
        IReadOnlyList<RawClientDto> clients;
        try
        {
            clients = await Gateway.GetClientsAsync();
        }
        catch (Exception)
        {
            return; // the section already shows when the service does not answer
        }
        Clients.Clear();
        foreach (var client in clients)
            Clients.Add(RawClientRow.From(client));
        OnPropertyChanged(nameof(HasClients));
    }

    public bool HasClients => Clients.Count > 0;

    /// <summary>Says that an address was copied (the window puts it in the clipboard and calls this).</summary>
    public void NotifyCopied(string text)
    {
        TestFailed = false;
        TestResult = Strings.Get("Raw.Copied", text);
    }
}
