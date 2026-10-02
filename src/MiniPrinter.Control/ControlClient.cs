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

    /// <summary>Keeps the printer connected and samples its state every minute for <paramref name="duration"/>.</summary>
    public Task<StatusDto> StartSamplingAsync(TimeSpan duration, CancellationToken ct = default) =>
        Send<StatusDto>(HttpMethod.Post, "telemetry/sampling", new { minutes = (int)duration.TotalMinutes }, ct);

    public async Task StopSamplingAsync(CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync("telemetry/sampling", ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
    }

    /// <summary>All retained status readings as CSV.</summary>
    public async Task<string> ExportTelemetryAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("telemetry/export", ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Prints text rendered by the service (fontSize in points; null = configured default).</summary>
    public Task<JobDto> PrintTextAsync(string text, float? sizePt = null, bool bold = false, string align = "Left", CancellationToken ct = default) =>
        Send<JobDto>(HttpMethod.Post, "print/text", new { text, sizePt, bold, align }, ct);

    /// <summary>PNG preview of text exactly as it would be printed.</summary>
    public async Task<byte[]> PreviewTextAsync(string text, float? sizePt = null, bool bold = false, string align = "Left", CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "print/text/preview")
        {
            Content = JsonContent.Create(new { text, sizePt, bold, align }, options: ControlDefaults.Json),
        };
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Prints a file (PNG, JPEG, PWG, PDF or TXT).</summary>
    public async Task<JobDto> PrintFileAsync(byte[] content, string fileName, string? contentType = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "print/file") { Content = new ByteArrayContent(content) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType ?? GuessContentType(fileName));
        request.Headers.Add("X-File-Name", Uri.EscapeDataString(Path.GetFileName(fileName)));
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<JobDto>(ControlDefaults.Json, ct).ConfigureAwait(false))!;
    }

    public static string GuessContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".pdf" => "application/pdf",
        ".pwg" => "image/pwg-raster",
        ".txt" => "text/plain",
        _ => "application/octet-stream",
    };

    public Task<IReadOnlyList<TemplateDto>> GetTemplatesAsync(CancellationToken ct = default) =>
        Get<IReadOnlyList<TemplateDto>>("templates", ct);

    /// <summary>PNG preview of a template (counters are not consumed). With <paramref name="rows"/>, the first label.</summary>
    public async Task<byte[]> PreviewTemplateAsync(string name, IReadOnlyDictionary<string, string> fields,
        IReadOnlyList<IReadOnlyDictionary<string, string>>? rows = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"templates/{Uri.EscapeDataString(name)}/preview")
        {
            Content = JsonContent.Create(TemplateBody(fields, 1, rows), options: ControlDefaults.Json),
        };
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Prints a template as one job: <paramref name="copies"/> of each label, one label per row.</summary>
    public Task<JobDto> PrintTemplateAsync(string name, IReadOnlyDictionary<string, string> fields, int copies = 1,
        IReadOnlyList<IReadOnlyDictionary<string, string>>? rows = null, CancellationToken ct = default) =>
        Send<JobDto>(HttpMethod.Post, $"print/template/{Uri.EscapeDataString(name)}", TemplateBody(fields, copies, rows), ct);

    private static Dictionary<string, object?> TemplateBody(IReadOnlyDictionary<string, string> fields, int copies,
        IReadOnlyList<IReadOnlyDictionary<string, string>>? rows)
    {
        var body = new Dictionary<string, object?>(fields.Select(f => new KeyValuePair<string, object?>(f.Key, f.Value)));
        if (copies != 1)
            body["copies"] = copies;
        if (rows is not null)
            body["rows"] = rows;
        return body;
    }

    /// <summary>The JSON of a template (the user's file, or the built-in one).</summary>
    public async Task<string> GetTemplateJsonAsync(string name, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"templates/{Uri.EscapeDataString(name)}", ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Creates or replaces a user template (validated by the service).</summary>
    public async Task<TemplateDto> SaveTemplateAsync(string name, string json, string? draft = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"templates/{Uri.EscapeDataString(name)}{(draft is null ? "" : "?draft=" + Uri.EscapeDataString(draft))}")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<TemplateDto>(ControlDefaults.Json, ct).ConfigureAwait(false))!;
    }

    /// <summary>The block catalog (types, properties, ranges, options) used to build the editor's controls.</summary>
    public Task<TemplateSchemaDto> GetTemplateSchemaAsync(CancellationToken ct = default) => Get<TemplateSchemaDto>("templates/schema", ct);

    /// <summary>Preview of a saved template with the rows each block occupies.</summary>
    public async Task<TemplatePreviewDto> PreviewTemplateDetailedAsync(string name, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"templates/{Uri.EscapeDataString(name)}/preview")
        {
            Content = JsonContent.Create(TemplateBody(fields, 1, null), options: ControlDefaults.Json),
        };
        return await ReadPreview(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Preview of a template that is not saved. Required fields without a value are not an error; images are
    /// looked up in the draft (see <see cref="CreateDraftAsync"/>) first.
    /// </summary>
    public async Task<TemplatePreviewDto> PreviewDraftAsync(string templateJson, IReadOnlyDictionary<string, string> fields,
        string? draft = null, CancellationToken ct = default)
    {
        using var template = JsonDocument.Parse(templateJson);
        var body = new Dictionary<string, object?> { ["template"] = template.RootElement.Clone(), ["fields"] = fields };
        if (draft is not null)
            body["draft"] = draft;
        using var request = new HttpRequestMessage(HttpMethod.Post, "templates/preview")
        {
            Content = JsonContent.Create(body, options: ControlDefaults.Json),
        };
        return await ReadPreview(request, ct).ConfigureAwait(false);
    }

    private async Task<TemplatePreviewDto> ReadPreview(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        var blocks = response.Headers.TryGetValues("X-Template-Blocks", out var values)
            ? JsonSerializer.Deserialize<List<BlockRowsDto>>(values.First(), ControlDefaults.Json) ?? []
            : [];
        return new TemplatePreviewDto(await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false), blocks);
    }

    /// <summary>Opens an editor draft: a place for the images of a template that is not saved yet.</summary>
    public async Task<string> CreateDraftAsync(CancellationToken ct = default) =>
        (await Send<DraftDto>(HttpMethod.Post, "templates/drafts", null, ct).ConfigureAwait(false)).Id;

    public async Task SaveDraftAssetAsync(string draft, string file, byte[] content, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"templates/drafts/{Uri.EscapeDataString(draft)}/assets/{Uri.EscapeDataString(file)}") { Content = new ByteArrayContent(content) };
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
    }

    public async Task DeleteDraftAssetAsync(string draft, string file, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync($"templates/drafts/{Uri.EscapeDataString(draft)}/assets/{Uri.EscapeDataString(file)}", ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
    }

    /// <summary>Discards a draft (the editor was cancelled).</summary>
    public async Task DiscardDraftAsync(string draft, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync($"templates/drafts/{Uri.EscapeDataString(draft)}", ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
    }


    public async Task DeleteTemplateAsync(string name, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync($"templates/{Uri.EscapeDataString(name)}", ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
    }

    /// <summary>Checks a template without saving it; throws <see cref="ControlApiException"/> with the reason if invalid.</summary>
    public async Task ValidateTemplateAsync(string json, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "templates/validate")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
    }

    public async Task SaveTemplateAssetAsync(string template, string file, byte[] content, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"templates/{Uri.EscapeDataString(template)}/assets/{Uri.EscapeDataString(file)}") { Content = new ByteArrayContent(content) };
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
    }

    public async Task DeleteTemplateAssetAsync(string template, string file, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync($"templates/{Uri.EscapeDataString(template)}/assets/{Uri.EscapeDataString(file)}", ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
    }

    public Task<AutomationInfo> GetAutomationAsync(CancellationToken ct = default) => Get<AutomationInfo>("automation", ct);

    public Task<AutomationInfo> RegenerateAutomationTokenAsync(CancellationToken ct = default) =>
        Send<AutomationInfo>(HttpMethod.Post, "automation/token", null, ct);

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
        int? block = null;
        string? property = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(ControlDefaults.Json, ct).ConfigureAwait(false);
            message = error?.Error ?? response.ReasonPhrase ?? "error";
            block = error?.Block;
            property = error?.Property;
        }
        catch (JsonException)
        {
            message = response.ReasonPhrase ?? "error";
        }
        throw new ControlApiException((int)response.StatusCode, message, block, property);
    }
}

/// <param name="Block">Template block (1-based) the error belongs to, when the service could tell.</param>
public sealed class ControlApiException(int statusCode, string message, int? block = null, string? property = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public int? Block { get; } = block;
    public string? Property { get; } = property;
}
