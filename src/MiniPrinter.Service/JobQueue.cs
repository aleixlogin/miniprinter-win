using System.Threading.Channels;
using MiniPrinter.Control;
using MiniPrinter.Imaging;
using MiniPrinter.Ipp;
using MiniPrinter.Protocol;
using MiniPrinter.Transport;

namespace MiniPrinter.Service;

/// <summary>
/// FIFO print queue with a single worker. Converts documents to printer jobs, holds jobs while
/// the printer reports an alarm or is unreachable (up to <see cref="ServiceSettings.JobRetryMinutes"/>),
/// and cancels cleanly between rows.
/// </summary>
public sealed class JobQueue : IPrintBackend, IHostedService, IAsyncDisposable
{
    public const int HistorySize = 20;

    private readonly PrinterManager _printer;
    private readonly SettingsStore _settings;
    private readonly string _diagnosticsDir;
    private readonly ILogger<JobQueue> _logger;
    private readonly Channel<int> _pending = Channel.CreateUnbounded<int>();
    private readonly Dictionary<int, QueuedJob> _jobs = [];
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stopping = new();
    private Task _worker = Task.CompletedTask;
    private int _nextId = 1;
    private string? _waitingReason;
    private int _stopped;

    public JobQueue(PrinterManager printer, SettingsStore settings, ServicePaths paths, ILogger<JobQueue> logger)
    {
        _printer = printer;
        _settings = settings;
        _diagnosticsDir = Path.Combine(paths.DataDirectory, "last-job");
        _logger = logger;
    }

    /// <summary>Raised when any job changes state.</summary>
    public event Action? JobsChanged;

    /// <summary>Polling interval while waiting for the printer to become ready.</summary>
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(5);

    public bool IsIdle
    {
        get { lock (_gate) return _jobs.Values.All(j => j.Info.IsTerminal); }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _worker = Task.Run(() => WorkerAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
            return;
        _stopping.Cancel();
        _pending.Writer.TryComplete();
        try { await _worker.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }

    // ---- IPrintBackend -------------------------------------------------------------------------

    public PrinterSnapshot GetPrinter()
    {
        var status = _printer.Status;
        var reasons = new List<string>();
        string? message = null;
        var processing = false;
        int queued;
        lock (_gate)
        {
            queued = _jobs.Values.Count(j => !j.Info.IsTerminal);
            processing = _jobs.Values.Any(j => j.Info.State == JobState.Processing);
            if (_waitingReason is not null)
                message = _waitingReason;
        }

        if (status.State is { IsReady: false } state)
        {
            if (state.Alarms.HasFlag(PrinterAlarms.OutOfPaper)) reasons.Add("media-empty-error");
            if (state.Alarms.HasFlag(PrinterAlarms.Overheated)) reasons.Add("other-error");
            if (state.Alarms.HasFlag(PrinterAlarms.LowBattery)) reasons.Add("other-warning");
            if (reasons.Count == 0) reasons.Add("other-error");
        }
        if (status.Link == LinkState.Error)
            reasons.Add("offline-report");
        if (status.Link == LinkState.Connecting)
            reasons.Add("connecting-to-device");
        if (_printer.Selection is null)
        {
            reasons.Add("offline-report");
            message ??= "No printer selected";
        }

        var stopped = reasons.Any(r => r.EndsWith("-error", StringComparison.Ordinal)) || (_waitingReason is not null && !processing);
        var printerState = stopped ? PrinterState.Stopped : processing ? PrinterState.Processing : PrinterState.Idle;
        return new PrinterSnapshot(printerState, reasons.Count > 0 ? reasons : ["none"], message ?? status.LastError, true, queued);
    }

    public JobInfo CreateJob(string name, string userName)
    {
        QueuedJob job;
        lock (_gate)
        {
            var id = _nextId++;
            job = new QueuedJob(new JobInfo { Id = id, Name = name, UserName = userName, StateReasons = ["job-incoming"] });
            _jobs[id] = job;
            Prune();
        }
        JobsChanged?.Invoke();
        return job.Info;
    }

    public JobInfo SubmitDocument(int jobId, Stream document, string? format, bool lastDocument)
    {
        if (!IsSupported(document, format))
            throw new NotSupportedException($"Document format {format ?? "unknown"} is not supported.");

        QueuedJob job;
        lock (_gate)
        {
            job = _jobs.GetValueOrDefault(jobId) ?? throw new KeyNotFoundException($"Job {jobId} not found.");
            job.Document = document;
            job.Format = format;
            job.Info = job.Info with { StateReasons = ["none"], SizeBytes = document.Length };
        }
        _pending.Writer.TryWrite(jobId);
        JobsChanged?.Invoke();
        return job.Info;
    }

    public JobInfo? GetJob(int jobId)
    {
        lock (_gate)
            return _jobs.GetValueOrDefault(jobId)?.Info;
    }

    public IReadOnlyList<JobInfo> GetJobs(bool completed, int limit)
    {
        lock (_gate)
            return _jobs.Values.Select(j => j.Info).Where(j => j.IsTerminal == completed)
                .OrderByDescending(j => j.Id).Take(limit).ToList();
    }

    public IReadOnlyList<JobInfo> AllJobs()
    {
        lock (_gate)
            return _jobs.Values.Select(j => j.Info).OrderByDescending(j => j.Id).ToList();
    }

    public bool CancelJob(int jobId)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(jobId, out var job) || job.Info.IsTerminal)
                return false;
            job.Cancellation.Cancel();
            if (job.Info.State != JobState.Processing)
                Finish(job, JobState.Canceled, "job-canceled-by-user", "Canceled");
        }
        JobsChanged?.Invoke();
        return true;
    }

    public Task IdentifyAsync(CancellationToken cancellationToken) => _printer.FeedAsync(48, cancellationToken);

    /// <summary>Queues a raster directly (used for test prints).</summary>
    public JobInfo SubmitBitmap(string name, MonoBitmap page)
    {
        var info = CreateJob(name, "MiniPrinter");
        lock (_gate)
        {
            var job = _jobs[info.Id];
            job.Bitmap = page;
            job.Info = job.Info with { StateReasons = ["none"] };
            info = job.Info;
        }
        _pending.Writer.TryWrite(info.Id);
        JobsChanged?.Invoke();
        return info;
    }

    // ---- Worker --------------------------------------------------------------------------------

    private async Task WorkerAsync(CancellationToken stopping)
    {
        await foreach (var id in _pending.Reader.ReadAllAsync(stopping))
        {
            QueuedJob? job;
            lock (_gate)
                job = _jobs.GetValueOrDefault(id);
            if (job is null || job.Info.IsTerminal)
                continue;

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping, job.Cancellation.Token);
            try
            {
                await ProcessAsync(job, linked.Token);
                lock (_gate)
                    Finish(job, JobState.Completed, "job-completed-successfully", "Printed");
            }
            catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
            {
                lock (_gate)
                    Finish(job, JobState.Canceled, "job-canceled-by-user", "Canceled");
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                lock (_gate)
                    Finish(job, JobState.Aborted, "aborted-by-system", "Service stopped");
                return;
            }
            catch (NotSupportedException ex)
            {
                lock (_gate)
                    Finish(job, JobState.Aborted, "document-format-error", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {Id} failed", job.Info.Id);
                lock (_gate)
                    Finish(job, JobState.Aborted, "job-aborted-by-system", ex.Message);
            }
            finally
            {
                _printer.RequireSessionOrNull()?.SetPrinting(false);
                lock (_gate)
                    _waitingReason = null;
                job.Document?.Dispose();
                job.Document = null;
                JobsChanged?.Invoke();
            }
        }
    }

    private async Task ProcessAsync(QueuedJob job, CancellationToken ct)
    {
        var settings = _settings.Current;
        var options = new PrintOptions
        {
            Darkness = settings.Darkness,
            FeedPadding = settings.FeedPadding,
            PostPrintFeedCount = _printer.Profile.PostPrintFeedCount + settings.ExtraFeedSteps,
        };
        var raster = new RasterOptions
        {
            WidthPx = _printer.Profile.WidthPx,
            Dither = settings.Dither switch
            {
                DitherChoice.Atkinson => DitherMode.Atkinson,
                DitherChoice.FloydSteinberg => DitherMode.FloydSteinberg,
                DitherChoice.Threshold => DitherMode.Threshold,
                _ => DitherMode.Auto,
            },
        };

        Update(job, j => j with { State = JobState.Processing, StateReasons = ["job-printing"], Processing = DateTimeOffset.UtcNow, StateMessage = "Printing" });

        BeginDiagnostics(job);
        var pages = job.Bitmap is { } bitmap
            ? [bitmap]
            : ImageDecoder.Decode(job.Document!, job.Format).Select(p => Rasterizer.Rasterize(p, raster));

        var printed = 0;
        var index = 0;
        foreach (var page in pages)
        {
            ct.ThrowIfCancellationRequested();
            SavePageDiagnostics(++index, page);
            if (page.Height == 0)
                continue; // blank page after trimming
            await PrintPageAsync(job, page, options, ct);
            printed++;
            Update(job, j => j with { PagesCompleted = printed });
        }
    }

    private async Task PrintPageAsync(QueuedJob job, MonoBitmap page, PrintOptions options, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(_settings.Current.JobRetryMinutes);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            string reason;
            try
            {
                var session = _printer.RequireSession();
                var builder = new PrintJobBuilder(_printer.Profile);
                var bytes = builder.BuildPage(page, options, endsPage: false);
                var sent = false;
                await session.UseAsync(async (connection, token) =>
                {
                    var state = await connection.GetStateAsync(TimeSpan.FromSeconds(3), token);
                    if (!state.IsReady)
                        throw new PrinterAlarmException(state);
                    session.SetPrinting(true);
                    SetWaiting(job, null);
                    try
                    {
                        await connection.SendAsync(bytes, _printer.Stream, token);
                        sent = true;
                    }
                    finally
                    {
                        // Always close the page so the printer is left in a clean state, even when cancelled.
                        await connection.SendAsync(builder.BuildPageEnd(options), _printer.Stream, CancellationToken.None);
                    }
                }, ct);
                if (sent)
                    return;
                reason = "Interrupted";
            }
            catch (PrinterAlarmException ex)
            {
                reason = $"Printer needs attention: {ex.State.Alarms}";
            }
            catch (PrinterNotConfiguredException ex)
            {
                reason = ex.Message;
            }
            catch (TransportException ex)
            {
                reason = ex.Kind == TransportErrorKind.Busy ? "Printer is in use by another application" : $"Printer not available: {ex.Message}";
            }
            catch (TimeoutException)
            {
                reason = "Printer did not answer";
            }

            if (DateTimeOffset.UtcNow >= deadline)
                throw new InvalidOperationException(reason);
            SetWaiting(job, reason);
            await Task.Delay(RetryInterval, ct);
        }
    }

    /// <summary>Directory holding the last job's document and rendered pages.</summary>
    public string DiagnosticsDirectory => _diagnosticsDir;

    /// <summary>Keeps the last job's raw document and the exact rasters sent, for the tray preview.</summary>
    private void BeginDiagnostics(QueuedJob job)
    {
        try
        {
            if (Directory.Exists(_diagnosticsDir))
                Directory.Delete(_diagnosticsDir, recursive: true);
            Directory.CreateDirectory(_diagnosticsDir);
            File.WriteAllLines(Path.Combine(_diagnosticsDir, "job.txt"),
                [job.Info.Id.ToString(), job.Info.Name, job.Format ?? "(sniffed)", DateTimeOffset.Now.ToString("O")]);
            if (job.Document is MemoryStream memory)
                File.WriteAllBytes(Path.Combine(_diagnosticsDir, "document.bin"), memory.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not write job diagnostics");
        }
    }

    private void SavePageDiagnostics(int index, MonoBitmap page)
    {
        try
        {
            if (page.Height > 0)
                MonoPng.Save(page, Path.Combine(_diagnosticsDir, $"page-{index}.png"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not write page preview");
        }
    }

    private void SetWaiting(QueuedJob job, string? reason)
    {
        lock (_gate)
            _waitingReason = reason;
        Update(job, j => reason is null
            ? j with { State = JobState.Processing, StateReasons = ["job-printing"], StateMessage = "Printing" }
            : j with { State = JobState.ProcessingStopped, StateReasons = ["printer-stopped"], StateMessage = reason });
    }

    private void Update(QueuedJob job, Func<JobInfo, JobInfo> change)
    {
        lock (_gate)
        {
            if (job.Info.IsTerminal)
                return;
            job.Info = change(job.Info);
        }
        JobsChanged?.Invoke();
    }

    private static void Finish(QueuedJob job, JobState state, string reason, string message)
    {
        if (job.Info.IsTerminal)
            return;
        job.Info = job.Info with { State = state, StateReasons = [reason], StateMessage = message, Completed = DateTimeOffset.UtcNow };
    }

    private void Prune()
    {
        var finished = _jobs.Values.Where(j => j.Info.IsTerminal).OrderByDescending(j => j.Info.Id).Skip(HistorySize).ToList();
        foreach (var job in finished)
            _jobs.Remove(job.Info.Id);
    }

    private static bool IsSupported(Stream document, string? format)
    {
        if (format is "image/pwg-raster" or "image/jpeg" or "image/png")
            return true;
        if (format is not (null or "application/octet-stream"))
            return false;
        Span<byte> head = stackalloc byte[8];
        var read = document.Read(head);
        document.Position = 0;
        head = head[..read];
        return PwgRasterReader.IsPwgRaster(head)
               || head.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G'])
               || head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]);
    }

    private sealed class QueuedJob(JobInfo info)
    {
        public JobInfo Info { get; set; } = info;
        public Stream? Document { get; set; }
        public string? Format { get; set; }
        public MonoBitmap? Bitmap { get; set; }
        public CancellationTokenSource Cancellation { get; } = new();
    }

    private sealed class PrinterAlarmException(DeviceState state) : Exception($"Printer alarm 0x{state.AlarmByte:X2}")
    {
        public DeviceState State { get; } = state;
    }
}
