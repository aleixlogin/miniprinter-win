using System.Runtime.InteropServices;

namespace MiniPrinter.Service;

/// <summary>
/// Advertises the IPP printer over mDNS/DNS-SD (<c>_ipp._tcp</c>) using the Windows built-in
/// responder (<c>DnsServiceRegister</c>, Windows 10+), so it does not compete for UDP 5353.
/// </summary>
public sealed class MdnsAdvertiser : IDisposable
{
    private const uint DnsQueryRequestVersion1 = 1;
    private const int DnsRequestPending = 9506;

    private readonly ILogger _logger;
    private readonly RegisterCallback _callback;
    private IntPtr _instance;
    private IntPtr _request;
    private bool _registered;

    public MdnsAdvertiser(ILogger logger)
    {
        _logger = logger;
        _callback = OnRegistered;
    }

    public bool IsRegistered => _registered;

    /// <summary>Registers "<paramref name="instanceName"/>._ipp._tcp.local" on <paramref name="port"/>.</summary>
    public void Register(string instanceName, int port, IReadOnlyDictionary<string, string> txt)
    {
        Unregister();
        var host = Environment.MachineName.ToLowerInvariant() + ".local";
        var service = $"{instanceName}._ipp._tcp.local";
        var keys = txt.Keys.ToArray();
        var values = keys.Select(k => txt[k]).ToArray();

        _instance = DnsServiceConstructInstance(service, host, IntPtr.Zero, IntPtr.Zero, (ushort)port, 0, 0,
            (uint)keys.Length, keys, values);
        if (_instance == IntPtr.Zero)
        {
            _logger.LogWarning("mDNS: could not construct service instance for {Service}", service);
            return;
        }

        var request = new DnsServiceRegisterRequest
        {
            Version = DnsQueryRequestVersion1,
            InterfaceIndex = 0,
            ServiceInstance = _instance,
            RegisterCompletionCallback = Marshal.GetFunctionPointerForDelegate(_callback),
            QueryContext = IntPtr.Zero,
            Credentials = IntPtr.Zero,
            UnicastEnabled = false,
        };
        _request = Marshal.AllocHGlobal(Marshal.SizeOf<DnsServiceRegisterRequest>());
        Marshal.StructureToPtr(request, _request, false);

        var status = DnsServiceRegister(_request, IntPtr.Zero);
        if (status != DnsRequestPending)
        {
            _logger.LogWarning("mDNS: DnsServiceRegister failed with {Status}", status);
            Free();
            return;
        }
        _registered = true;
        _logger.LogInformation("mDNS: advertising {Service} on port {Port}", service, port);
    }

    public void Unregister()
    {
        if (_registered && _request != IntPtr.Zero)
        {
            var status = DnsServiceDeRegister(_request, IntPtr.Zero);
            if (status != DnsRequestPending && status != 0)
                _logger.LogDebug("mDNS: DnsServiceDeRegister returned {Status}", status);
            // De-registration completes asynchronously through the same callback; give it a moment.
            Thread.Sleep(200);
        }
        _registered = false;
        Free();
    }

    public void Dispose() => Unregister();

    private void OnRegistered(uint status, IntPtr context, IntPtr instance)
    {
        if (status != 0)
            _logger.LogWarning("mDNS: registration completed with status {Status}", status);
        if (instance != IntPtr.Zero)
            DnsServiceFreeInstance(instance);
    }

    private void Free()
    {
        if (_request != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_request);
            _request = IntPtr.Zero;
        }
        if (_instance != IntPtr.Zero)
        {
            DnsServiceFreeInstance(_instance);
            _instance = IntPtr.Zero;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void RegisterCallback(uint status, IntPtr queryContext, IntPtr instance);

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceRegisterRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public IntPtr ServiceInstance;
        public IntPtr RegisterCompletionCallback;
        public IntPtr QueryContext;
        public IntPtr Credentials;
        [MarshalAs(UnmanagedType.Bool)] public bool UnicastEnabled;
    }

    [DllImport("dnsapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DnsServiceConstructInstance(
        string serviceName, string hostName, IntPtr ip4, IntPtr ip6, ushort port, ushort priority, ushort weight,
        uint propertiesCount, string[] keys, string[] values);

    [DllImport("dnsapi.dll")]
    private static extern void DnsServiceFreeInstance(IntPtr instance);

    [DllImport("dnsapi.dll")]
    private static extern int DnsServiceRegister(IntPtr request, IntPtr cancel);

    [DllImport("dnsapi.dll")]
    private static extern int DnsServiceDeRegister(IntPtr request, IntPtr cancel);
}
