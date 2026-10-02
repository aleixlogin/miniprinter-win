using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using MiniPrinter.Control;
using MiniPrinter.Imaging;
using MiniPrinter.Ipp;

namespace MiniPrinter.Service;

/// <summary>Bearer token of the automation API, stored in <c>automation.token</c> (separate from the control token).</summary>
public sealed class AutomationToken
{
    private readonly string _path;
    private readonly object _gate = new();
    private string? _token;

    public AutomationToken(ServicePaths paths)
    {
        _path = Path.Combine(paths.DataDirectory, "automation.token");
    }

    /// <summary>The current token, generated on first use.</summary>
    public string Current
    {
        get
        {
            lock (_gate)
            {
                if (_token is not null)
                    return _token;
                if (File.Exists(_path) && File.ReadAllText(_path).Trim() is { Length: >= 32 } stored)
                    return _token = stored;
                return Regenerate();
            }
        }
    }

    /// <summary>Creates a new token; the previous one stops working immediately.</summary>
    public string Regenerate()
    {
        lock (_gate)
        {
            _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            File.WriteAllText(_path, _token);
            return _token;
        }
    }

    public bool Matches(string? supplied)
    {
        if (string.IsNullOrEmpty(supplied))
            return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(Current));
    }
}

/// <summary>Request bodies of the automation API (documented in the README).</summary>
public sealed record AutomationTextRequest(string? Text, float? FontSize, TextAlign Align = TextAlign.Left, bool Bold = false, int? Darkness = null);

public sealed record AutomationQrRequest(string? Data, string? Caption, int? Darkness = null);

public sealed record AutomationAccepted(int JobId);

public sealed record AutomationJobStatus(int Id, string State, string? Message, int Pages);

/// <summary>
/// REST printing API for scripts, Home Assistant and n8n (design.md D6). Mapped on the IPP listener,
/// so it follows the network mode; disabled by default; never exposes control endpoints.
/// </summary>
public static class AutomationApi
{
    public const long MaxBodyBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = ControlDefaults.Json;

    public static void Map(WebApplication app, SettingsStore settings, AutomationToken token, PrintRequests print, JobQueue queue)
    {
        var api = app.MapGroup("/api/v1");
        api.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (!settings.Current.AutomationApiEnabled)
                return Results.NotFound();
            var header = http.Request.Headers.Authorization.ToString();
            var supplied = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
            if (!token.Matches(supplied))
                return Results.Json(new ApiError("Missing or invalid token (Authorization: Bearer <token>)."), Json, statusCode: 401);
            if (http.Request.ContentLength > MaxBodyBytes)
                return Results.Json(new ApiError("The request body exceeds 16 MB."), Json, statusCode: 413);
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                limit.MaxRequestBodySize = MaxBodyBytes;
            try
            {
                return await next(context);
            }
            catch (Exception ex) when (ex is PrintRequestException or JsonException or BadHttpRequestException or InvalidDataException)
            {
                return Results.Json(new ApiError(ex.Message), Json, statusCode: 400);
            }
            catch (PrinterNotConfiguredException ex)
            {
                return Results.Json(new ApiError(ex.Message), Json, statusCode: 409);
            }
        });

        api.MapPost("/print/text", async (HttpContext http) =>
        {
            var body = await Read<AutomationTextRequest>(http);
            var job = print.PrintText(new TextPrintRequest
            {
                Text = body.Text ?? "",
                SizePt = body.FontSize,
                Align = body.Align,
                Bold = body.Bold,
                Darkness = body.Darkness,
            }, User(http));
            return Accepted(job);
        });

        api.MapPost("/print/image", async (HttpContext http) =>
        {
            byte[] content;
            string? name = null, type = http.Request.ContentType;
            if (http.Request.HasFormContentType)
            {
                var form = await http.Request.ReadFormAsync();
                var file = form.Files.FirstOrDefault() ?? throw new PrintRequestException("The form has no file.");
                using var memory = new MemoryStream();
                await file.CopyToAsync(memory);
                content = memory.ToArray();
                name = file.FileName;
                type = file.ContentType;
            }
            else
            {
                using var memory = new MemoryStream();
                await http.Request.Body.CopyToAsync(memory);
                content = memory.ToArray();
            }
            if (type?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true)
                throw new PrintRequestException("Use /api/v1/print/text for text.");
            return Accepted(print.PrintFile(content, name ?? "image", type, User(http), Darkness(http)));
        });

        api.MapPost("/print/qr", async (HttpContext http) =>
        {
            var body = await Read<AutomationQrRequest>(http);
            if (string.IsNullOrWhiteSpace(body.Data))
                throw new PrintRequestException("Missing field 'data'.");
            return Accepted(print.PrintQr(body.Data, body.Caption, User(http), body.Darkness));
        });

        api.MapPost("/print/template/{name}", async (string name, HttpContext http) =>
        {
            var fields = await ControlApi.ReadFields(http);
            int? darkness = fields.Remove("darkness", out var d) && int.TryParse(d, out var parsed) ? parsed : null;
            return Accepted(print.PrintTemplate(name, fields, User(http), darkness));
        });

        api.MapGet("/jobs/{id:int}", (int id) =>
            queue.GetJob(id) is { } job
                ? Results.Json(new AutomationJobStatus(job.Id, StateName(job.State), job.StateMessage, job.PagesCompleted), Json)
                : Results.Json(new ApiError("Job not found."), Json, statusCode: 404));
    }

    private static IResult Accepted(JobInfo job) => Results.Json(new AutomationAccepted(job.Id), Json, statusCode: 202);

    private static string StateName(JobState state) => state switch
    {
        JobState.Pending or JobState.PendingHeld => "pending",
        JobState.Processing or JobState.ProcessingStopped => "processing",
        JobState.Completed => "completed",
        JobState.Canceled => "canceled",
        _ => "aborted",
    };

    private static string User(HttpContext http) => $"api@{http.Connection.RemoteIpAddress}";

    private static int? Darkness(HttpContext http) =>
        int.TryParse(http.Request.Query["darkness"], out var d) ? d : null;

    private static async Task<T> Read<T>(HttpContext http)
    {
        if (!http.Request.HasJsonContentType())
            throw new PrintRequestException("Expected Content-Type: application/json.");
        return await http.Request.ReadFromJsonAsync<T>(Json) ?? throw new PrintRequestException("Missing body.");
    }
}
