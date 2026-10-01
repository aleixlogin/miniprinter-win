using System.IO;
using System.Net.Http;
using MiniPrinter.Control;

namespace MiniPrinter.Tray;

/// <summary>Keeps a live view of the service status (SSE with automatic reconnect).</summary>
public sealed class ServiceConnection : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private ControlClient? _client;

    public StatusDto? Status { get; private set; }

    /// <summary>True when the service answered recently.</summary>
    public bool ServiceAvailable { get; private set; }

    public string? ServiceError { get; private set; }

    /// <summary>Raised on the thread pool; marshal to the UI thread before touching controls.</summary>
    public event Action? Changed;

    public ControlClient Client => _client ?? throw new InvalidOperationException(ServiceError ?? "Servicio no disponible.");

    public void Start() => _ = Task.Run(() => WatchLoopAsync(_lifetime.Token));

    public void Dispose()
    {
        _lifetime.Cancel();
        _client?.Dispose();
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _client ??= ControlClient.FromTokenFile();
                await foreach (var status in _client.WatchStatusAsync(ct))
                {
                    Status = status;
                    ServiceAvailable = true;
                    ServiceError = null;
                    delay = TimeSpan.FromSeconds(1);
                    Changed?.Invoke();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or ControlApiException)
            {
                ServiceError = ex switch
                {
                    FileNotFoundException or DirectoryNotFoundException => "El servicio MiniPrinter no está instalado o no se ha iniciado nunca.",
                    UnauthorizedAccessException => "Sin permiso para leer el token del servicio.",
                    ControlApiException { StatusCode: 401 } => "Token del servicio no válido (¿se reinstaló?).",
                    _ => "No se puede contactar con el servicio MiniPrinter.",
                };
                if (ex is ControlApiException { StatusCode: 401 } or FileNotFoundException)
                {
                    _client?.Dispose();
                    _client = null;
                }
            }
            ServiceAvailable = false;
            Changed?.Invoke();
            try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { return; }
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 15));
        }
    }
}
