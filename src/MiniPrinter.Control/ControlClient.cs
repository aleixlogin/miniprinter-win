using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MiniPrinter.Control;

/// <summary>Client for the service's loopback control API (used by the tray app and tests).</summary>
public sealed class ControlClient : IDisposable
{
    private readonly HttpClient _http;

    public ControlClient(string token, int port = ControlDefaults.Port, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri($"http://127.0.0.1:{port}/api/");
        _http.DefaultRequestHeaders.Add(ControlDefaults.TokenHeader, token);
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    /// <summary>Creates a client using the token file written by the service.</summary>
    public static ControlClient FromTokenFile(int port = ControlDefaults.Port) =>
        new(File.ReadAllText(ControlDefaults.TokenPath).Trim(), port);

    public Task<StatusDto> GetStatusAsync(CancellationToken ct = default) => Get<StatusDto>("status", ct);

    public Task<ServiceSettings> GetSettingsAsync(CancellationToken ct = default) => Get<ServiceSettings>("settings", ct);

    public Task<ServiceSettings> SaveSettingsAsync(ServiceSettings settings, CancellationToken ct = default) =>
        Send<ServiceSettings>(HttpMethod.Put, "settings", settings, ct);

    public Task<StatusDto> SelectPrinterAsync(PrinterSelection printer, CancellationToken ct = default) =>
        Send<StatusDto>(HttpMethod.Post, "printer", printer, ct);

    public Task<StatusDto> ConnectAsync(CancellationToken ct = default) => Send<StatusDto>(HttpMethod.Post, "connect", null, ct);

    public Task<StatusDto> DisconnectAsync(CancellationToken ct = default) => Send<StatusDto>(HttpMethod.Post, "disconnect", null, ct);

    public Task<JobDto> TestPrintAsync(CancellationToken ct = default) => Send<JobDto>(HttpMethod.Post, "test-print", null, ct);

    public Task<StatusDto> FeedAsync(CancellationToken ct = default) => Send<StatusDto>(HttpMethod.Post, "feed", null, ct);

    public Task<IReadOnlyList<JobDto>> GetJobsAsync(CancellationToken ct = default) => Get<IReadOnlyList<JobDto>>("jobs", ct);

    /// <summary>PNG of a page of the last job, exactly as sent to the printer (null if none).</summary>
    public async Task<byte[]?> GetLastJobPageAsync(int page, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"jobs/last/pages/{page}", ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    public async Task CancelJobAsync(int id, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync($"jobs/{id}", ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
    }

    /// <summary>Streams status updates (server-sent events) until cancelled or the service stops.</summary>
    public async IAsyncEnumerable<StatusDto> WatchStatusAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "events");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
                yield break;
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;
            var status = JsonSerializer.Deserialize<StatusDto>(line[6..], ControlDefaults.Json);
            if (status is not null)
                yield return status;
        }
    }

    public void Dispose() => _http.Dispose();

    private async Task<T> Get<T>(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<T>(ControlDefaults.Json, ct).ConfigureAwait(false))!;
    }

    private async Task<T> Send<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: ControlDefaults.Json);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<T>(ControlDefaults.Json, ct).ConfigureAwait(false))!;
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;
        string message;
        try
        {
            message = (await response.Content.ReadFromJsonAsync<ApiError>(ControlDefaults.Json, ct).ConfigureAwait(false))?.Error
                      ?? response.ReasonPhrase ?? "error";
        }
        catch (JsonException)
        {
            message = response.ReasonPhrase ?? "error";
        }
        throw new ControlApiException((int)response.StatusCode, message);
    }
}

public sealed class ControlApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
