using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Channels;
using MiniPrinter.Control;
using MiniPrinter.Ipp;
using MiniPrinter.Protocol;
using MiniPrinter.Transport;

namespace MiniPrinter.Service;

/// <summary>Loopback-only JSON API used by the tray app (design.md D6).</summary>
public sealed record SamplingRequest(int Minutes);

public static class ControlApi
{
    private static readonly string Version =
        typeof(ControlApi).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";

    public static void Map(WebApplication app, string token)
    {
        var api = app.MapGroup("/api");
        api.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (!http.Request.Headers.TryGetValue(ControlDefaults.TokenHeader, out var supplied)
                || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(supplied.ToString()), System.Text.Encoding.UTF8.GetBytes(token)))
                return Results.Json(new ApiError("Missing or invalid control token."), ControlDefaults.Json, statusCode: 401);
            try
            {
                return await next(context);
            }
            catch (PrinterNotConfiguredException ex)
            {
                return Results.Json(new ApiError(ex.Message), ControlDefaults.Json, statusCode: 409);
            }
            catch (TransportException ex)
            {
                return Results.Json(new ApiError($"{ex.Kind}: {ex.Message}"), ControlDefaults.Json, statusCode: 503);
            }
            catch (TimeoutException)
            {
                return Results.Json(new ApiError("The printer did not answer in time."), ControlDefaults.Json, statusCode: 504);
            }
            catch (System.Text.Json.JsonException ex)
            {
                return Results.Json(new ApiError($"JSON no válido: {ex.Message}"), ControlDefaults.Json, statusCode: 400);
            }
            catch (PrintRequestException ex)
            {
                return Results.Json(new ApiError(ex.Message, ex.Block, ex.Property), ControlDefaults.Json, statusCode: 400);
            }
        });

        api.MapGet("/status", (StatusBuilder status) => Json(status.Build()));

        api.MapGet("/diagnostics", async (DiagnosticsService diagnostics, CancellationToken ct) => Json(await diagnostics.RunAsync(ct)));

        api.MapGet("/raw-port/clients", (RawPortHost raw) => Json(raw.Clients));

        api.MapPost("/raw-port/test", (SettingsStore settings, RawPortHost raw, JobQueue queue, CancellationToken ct) =>
            RawPortTest.RunAsync(settings.Current, raw.Current, queue, DiagnosticsService.DetectFonts().Cjk, ct));

        api.MapGet("/settings", (SettingsStore settings) => Json(settings.Current));

        api.MapPut("/settings", async (HttpContext http, SettingsStore settings) =>
        {
            var body = await http.Request.ReadFromJsonAsync<ServiceSettings>(ControlDefaults.Json);
            if (body is null)
                return Results.Json(new ApiError("Missing settings."), ControlDefaults.Json, statusCode: 400);
            if (body.RawPort is < 1024 or > 65535)
                return Results.Json(new ApiError("El puerto de impresión directa debe estar entre 1024 y 65535."), ControlDefaults.Json, statusCode: 400);
            if (PaperCatalog.Problem(body.PaperSizes) is { } paperProblem)
                return Results.Json(new ApiError(paperProblem), ControlDefaults.Json, statusCode: 400);
            // The printer is only changed through POST /printer: a client holding an older copy of
            // the settings must not clear or replace the selection when it saves other options.
            return Json(settings.Update(current => body with { Printer = current.Printer, PrinterUuid = current.PrinterUuid }));
        });

        api.MapPost("/printer", async (HttpContext http, SettingsStore settings, StatusBuilder status) =>
        {
            var printer = await http.Request.ReadFromJsonAsync<PrinterSelection>(ControlDefaults.Json);
            if (printer is null || (printer.Transport != TransportChoice.Simulated && !BluetoothAddress.TryParse(printer.Address, out _)))
                return Results.Json(new ApiError("A printer name and a valid Bluetooth address are required."), ControlDefaults.Json, statusCode: 400);
            if (Protocol.Catalog.PrinterCatalog.Default.GetProfile(printer.ProfileKey) is null)
                return Results.Json(new ApiError($"Unknown profile '{printer.ProfileKey}'."), ControlDefaults.Json, statusCode: 400);
            settings.Update(s => s with { Printer = printer });
            await Task.Delay(100); // let the session be recreated
            return Json(status.Build());
        });

        api.MapPost("/connect", async (PrinterManager printer, StatusBuilder status, CancellationToken ct) =>
        {
            await printer.RefreshAsync(ct);
            return Json(status.Build());
        });

        api.MapPost("/disconnect", async (PrinterManager printer, StatusBuilder status) =>
        {
            await printer.DisconnectAsync();
            return Json(status.Build());
        });

        api.MapPost("/feed", async (PrinterManager printer, StatusBuilder status, CancellationToken ct) =>
        {
            await printer.FeedAsync(96, ct);
            return Json(status.Build());
        });

        api.MapPost("/test-print", (PrinterManager printer, JobQueue queue) =>
        {
            printer.RequireSession();
            var job = queue.SubmitBitmap("Test page", TestPatterns.Calibration(printer.Profile.WidthPx));
            return Json(ToDto(job));
        });

        api.MapGet("/jobs/last/pages/{page:int}", (int page, JobQueue queue) =>
        {
            var path = Path.Combine(queue.DiagnosticsDirectory, $"page-{page}.png");
            return File.Exists(path)
                ? Results.File(File.ReadAllBytes(path), "image/png")
                : Results.Json(new ApiError("No preview for that page."), ControlDefaults.Json, statusCode: 404);
        });

        api.MapPost("/telemetry/sampling", async (HttpContext http, PrinterManager printer, StatusBuilder status) =>
        {
            var body = await http.Request.ReadFromJsonAsync<SamplingRequest>(ControlDefaults.Json);
            var minutes = Math.Clamp(body?.Minutes ?? 480, 1, 24 * 60);
            printer.RequireSession().KeepAlive(DateTimeOffset.UtcNow.AddMinutes(minutes), TimeSpan.FromSeconds(60));
            return Json(status.Build());
        });

        api.MapDelete("/telemetry/sampling", (PrinterManager printer) =>
        {
            printer.RequireSessionOrNull()?.KeepAlive(null);
            return Results.NoContent();
        });

        api.MapGet("/telemetry/export", (TelemetryLog telemetry) =>
            Results.Text(telemetry.Export(), "text/csv; charset=utf-8"));

        api.MapPost("/print/text", async (HttpContext http, PrintRequests print) =>
        {
            var request = await ReadTextRequest(http);
            return Json(ToDto(print.PrintText(request, Environment.UserName)));
        });

        api.MapPost("/print/text/preview", async (HttpContext http, PrintRequests print) =>
        {
            var bitmap = print.RenderText(await ReadTextRequest(http));
            using var png = new MemoryStream();
            Imaging.MonoPng.Save(bitmap, png);
            return Results.File(png.ToArray(), "image/png");
        });

        api.MapPost("/print/file", async (HttpContext http, PrintRequests print) =>
        {
            using var body = new MemoryStream();
            await http.Request.Body.CopyToAsync(body);
            var name = http.Request.Headers["X-File-Name"].ToString();
            var job = print.PrintFile(body.ToArray(), Uri.UnescapeDataString(name), http.Request.ContentType, Environment.UserName);
            return Json(ToDto(job));
        });

        api.MapGet("/automation", (SettingsStore settings, AutomationToken automation, IppHost ipp) =>
            Json(AutomationInfoOf(settings, automation, ipp)));

        api.MapPost("/automation/token", (SettingsStore settings, AutomationToken automation, IppHost ipp) =>
        {
            automation.Regenerate();
            return Json(AutomationInfoOf(settings, automation, ipp));
        });

        TemplateEndpoints.Map(api, app.Services.GetRequiredService<Imaging.TemplateCatalog>(), app.Services.GetRequiredService<DraftStore>(), app.Services.GetRequiredService<PrintRequests>());

        api.MapGet("/templates/{name}/thumbnail", (string name, int? width, TemplateThumbnails thumbnails) =>
            thumbnails.Get(name, width ?? TemplateThumbnails.DefaultWidth) is { } png
                ? Results.File(png, "image/png")
                : Results.Json(new ApiError($"Unknown template '{name}'."), ControlDefaults.Json, statusCode: 404));

        api.MapPost("/print/template/{name}", async (string name, HttpContext http, PrintRequests print) =>
        {
            var request = await TemplateEndpoints.ReadRequest(http);
            return Json(ToDto(print.PrintTemplate(name, request.Fields, Environment.UserName, request.Darkness, request.Copies, request.Rows)));
        });

        // Recreation of the Windows print queue so it reads the current paper sizes (control API only).
        api.MapGet("/windows-queue", async (WindowsQueue queue, CancellationToken ct) => Json(await queue.GetAsync(ct)));

        api.MapPost("/windows-queue/recreate", async (HttpContext http, WindowsQueue queue) =>
        {
            RecreateQueueRequest? request = null;
            if (http.Request.HasJsonContentType())
            {
                try
                {
                    request = await http.Request.ReadFromJsonAsync<RecreateQueueRequest>(ControlDefaults.Json);
                }
                catch (JsonException)
                {
                    // An empty body means "no previous name".
                }
            }
            try
            {
                queue.Start(request?.PreviousName);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new ApiError(ex.Message), ControlDefaults.Json, statusCode: 409);
            }
            return Results.Json(queue.Current, ControlDefaults.Json, statusCode: 202);
        });

        // Used by the elevated helper: a new printer identity, announced by the listener, before it creates a queue itself.
        api.MapPost("/windows-queue/prepare", async (WindowsQueue queue, CancellationToken ct) =>
        {
            await queue.PrepareAsync(ct);
            return Results.NoContent();
        });


        api.MapGet("/jobs", (JobQueue queue) => Json(queue.AllJobs().Select(j => ToDto(j, queue.CanReprint(j.Id))).ToList()));

        api.MapGet("/jobs/{id:int}/pages/{page:int}", (int id, int page, JobQueue queue) =>
            queue.KeptPagePath(id, page) is { } path
                ? Results.File(File.ReadAllBytes(path), "image/png")
                : Results.Json(new ApiError("No preview for that page."), ControlDefaults.Json, statusCode: 404));

        api.MapPost("/jobs/{id:int}/reprint", (int id, JobQueue queue) =>
        {
            try
            {
                return queue.Reprint(id) is { } job
                    ? Json(ToDto(job))
                    : Results.Json(new ApiError("The pages of that job were not kept."), ControlDefaults.Json, statusCode: 404);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new ApiError(ex.Message), ControlDefaults.Json, statusCode: 409);
            }
        });

        api.MapDelete("/jobs/{id:int}", (int id, JobQueue queue) =>
            queue.CancelJob(id) ? Results.NoContent() : Results.Json(new ApiError("Job not found or already finished."), ControlDefaults.Json, statusCode: 404));

        api.MapGet("/events", async (HttpContext http, StatusBuilder status, CancellationToken ct) =>
        {
            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            var updates = Channel.CreateBounded<StatusDto>(new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropOldest });
            void OnChange() => updates.Writer.TryWrite(status.Build());
            status.Changed += OnChange;
            try
            {
                OnChange();
                await foreach (var dto in updates.Reader.ReadAllAsync(ct))
                {
                    await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(dto, ControlDefaults.Json)}\n\n", ct);
                    await http.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                status.Changed -= OnChange;
            }
        });
    }

    /// <summary>Reads a JSON object of template fields (string values; numbers and booleans are converted).</summary>
    public static async Task<Dictionary<string, string>> ReadFields(HttpContext http)
    {
        try
        {
            var element = await http.Request.ReadFromJsonAsync<JsonElement>(ControlDefaults.Json);
            if (element.ValueKind != JsonValueKind.Object)
                throw new PrintRequestException("Expected a JSON object with the template fields.");
            return element.EnumerateObject().ToDictionary(p => p.Name,
                p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            throw new PrintRequestException($"Invalid JSON: {ex.Message}");
        }
    }

    private static async Task<TextPrintRequest> ReadTextRequest(HttpContext http)
    {
        try
        {
            return await http.Request.ReadFromJsonAsync<TextPrintRequest>(ControlDefaults.Json)
                   ?? throw new PrintRequestException("Missing body.");
        }
        catch (JsonException ex)
        {
            throw new PrintRequestException($"Invalid JSON: {ex.Message}");
        }
    }

    private static AutomationInfo AutomationInfoOf(SettingsStore settings, AutomationToken automation, IppHost ipp) => new(
        settings.Current.AutomationApiEnabled,
        automation.Current,
        ipp.Urls.Select(u => u.Replace("ipp://", "http://", StringComparison.Ordinal).Replace("/ipp/print", "/api/v1", StringComparison.Ordinal)).ToList());

    public static JobDto ToDto(JobInfo job, bool canReprint = false) => new(job.Id, job.Name, job.UserName, job.State.ToString(), job.StateMessage,
        job.Created, job.Completed, job.PagesCompleted, job.SizeBytes)
    {
        Source = job.Source.ToString(),
        Origin = job.Origin,
        CanReprint = canReprint,
    };

    private static IResult Json<T>(T value) => Results.Json(value, ControlDefaults.Json);

    /// <summary>Creates the token file once; readable by local users, writable by SYSTEM/Administrators.</summary>
    public static string EnsureToken(ServicePaths paths, ILogger logger)
    {
        if (File.Exists(paths.TokenPath))
        {
            var existing = File.ReadAllText(paths.TokenPath).Trim();
            if (existing.Length >= 32)
                return existing;
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(paths.TokenPath, token);
        try
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), FileSystemRights.Read, AccessControlType.Allow));
            // The account that runs the service (in development, the current user).
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(paths.TokenPath).SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not restrict access to {Path}", paths.TokenPath);
        }
        return token;
    }
}

/// <summary>Builds <see cref="StatusDto"/> and signals when anything visible changes.</summary>
public sealed class StatusBuilder
{
    private readonly PrinterManager _printer;
    private readonly JobQueue _queue;
    private readonly SettingsStore _settings;
    private readonly IppHost _ipp;
    private readonly string _version;

    private readonly BatteryMonitor _battery;

    private readonly WindowsQueue _windowsQueue;

    private readonly RawPortHost _raw;

    public StatusBuilder(PrinterManager printer, JobQueue queue, SettingsStore settings, IppHost ipp, BatteryMonitor battery, WindowsQueue windowsQueue, RawPortHost raw)
    {
        _raw = raw;
        raw.Changed += () => Changed?.Invoke();
        _battery = battery;
        _windowsQueue = windowsQueue;
        windowsQueue.Changed += () => Changed?.Invoke();
        _printer = printer;
        _queue = queue;
        _settings = settings;
        _ipp = ipp;
        _version = typeof(StatusBuilder).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";
        printer.StatusChanged += _ => Changed?.Invoke();
        queue.JobsChanged += () => Changed?.Invoke();
        settings.Changed += (_, _) => Changed?.Invoke();
    }

    public event Action? Changed;

    public StatusDto Build()
    {
        var s = _printer.Status;
        var state = s.State;
        return new StatusDto
        {
            Link = s.Link.ToString(),
            Printer = _printer.Selection,
            Ready = s.Link == LinkState.Connected && state is { IsReady: true },
            AlarmByte = state?.AlarmByte,
            Alarms = state is null || state.IsReady ? [] : Enum.GetValues<PrinterAlarms>()
                .Where(a => a != PrinterAlarms.None && state.Alarms.HasFlag(a)).Select(a => a.ToString()).ToList(),
            BatteryLevel = state?.BatteryLevel,
            PaperSensor = state?.PaperSensor,
            BatteryPercent = _battery.Percent,
            BatteryUnit = _settings.Current.BatteryUnit,
            LowBattery = _battery.IsLow,
            SamplingUntil = s.KeepAliveUntil,
            KeepAlive = s.Persistent,
            Reconnecting = s.Reconnecting,
            Firmware = s.Firmware,
            Printing = s.Printing,
            LastError = s.LastError,
            LastErrorKind = s.LastErrorKind?.ToString(),
            LastSeen = s.LastSeen,
            NetworkMode = _settings.Current.NetworkMode,
            IppUrls = _ipp.Urls,
            RawPort = _raw.Current,
            QueuedJobs = _queue.GetPrinter().QueuedJobs,
            Version = _version,
            WindowsQueue = _windowsQueue.Current,
        };
    }
}
