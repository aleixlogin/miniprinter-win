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
    private readonly JobArchive _archive;
    private readonly Channel<int> _pending = Channel.CreateUnbounded<int>();
    private readonly Dictionary<int, QueuedJob> _jobs = [];
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stopping = new();
    private Task _worker = Task.CompletedTask;
    private int _nextId = 1;
    private string? _waitingReason;
    private int _stopped;
    private bool _pageEndSent; // set by the last PrintPageAsync attempt that sent a page end (single worker)

    public JobQueue(PrinterManager printer, SettingsStore settings, ServicePaths paths, JobArchive archive, ILogger<JobQueue> logger)
    {
        _archive = archive;
        settings.Changed += (_, current) => archive.Trim(current.JobHistoryKeep);
        _printer = printer;
        _settings = settings;
        _diagnosticsDir = Path.Combine(paths.DataDirectory, "last-job");
        _logger = logger;
    }

    /// <summary>Raised when any job changes state.</summary>
    public event Action? JobsChanged;

    /// <summary>Reports whether the battery is low (adds <c>other-warning</c> to the IPP state).</summary>
    public Func<bool>? LowBatteryProbe { get; set; }

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
        if (LowBatteryProbe?.Invoke() == true && !reasons.Contains("other-warning"))
            reasons.Add("other-warning");
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

    public JobInfo CreateJob(string name, string userName) => CreateJob(name, userName, JobSource.Windows, userName);

    /// <summary>Creates a job and records the entrance it came through and who sent it.</summary>
    public JobInfo CreateJob(string name, string userName, JobSource source, string? origin)
    {
        QueuedJob job;
        lock (_gate)
        {
            var id = _nextId++;
            job = new QueuedJob(new JobInfo { Id = id, Name = name, UserName = userName, Source = source, Origin = origin, StateReasons = ["job-incoming"] });
            _jobs[id] = job;
            Prune();
        }
        JobsChanged?.Invoke();
        return job.Info;
    }

    public JobInfo SubmitDocument(int jobId, Stream document, string? format, bool lastDocument, DocumentOptions? options = null) =>
        SubmitDocument(jobId, document, format, lastDocument, darkness: null, options);

    /// <summary>Queues a document, optionally overriding the configured darkness for this job.</summary>
    public JobInfo SubmitDocument(int jobId, Stream document, string? format, bool lastDocument, int? darkness, DocumentOptions? options = null)
    {
        if (!IsSupported(document, format))
            throw new NotSupportedException($"Document format {format ?? "unknown"} is not supported.");

        QueuedJob job;
        lock (_gate)
        {
            job = _jobs.GetValueOrDefault(jobId) ?? throw new KeyNotFoundException($"Job {jobId} not found.");
            job.Document = document;
            job.Format = format;
            job.Darkness = darkness;
            job.Options = options;
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

    /// <summary>Queues a raster directly (test prints, rendered text, templates).</summary>
    public JobInfo SubmitBitmap(string name, MonoBitmap page, bool isText = false, string user = "MiniPrinter", int? darkness = null,
        JobSource source = JobSource.Panel, string? origin = null) =>
        SubmitBitmaps(name, [page], isText, user, darkness, source, origin);

    /// <summary>Queues several rasters as one job (copies and label batches): one page each, in order.</summary>
    public JobInfo SubmitBitmaps(string name, IReadOnlyList<MonoBitmap> pages, bool isText = false, string user = "MiniPrinter", int? darkness = null,
        JobSource source = JobSource.Panel, string? origin = null)
    {
        var info = CreateJob(name, user, source, origin ?? user);
        lock (_gate)
        {
            var job = _jobs[info.Id];
            job.Darkness = darkness;
            job.Bitmaps = [.. pages.Select(p => new RasterResult(p, isText))];
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
                FinishArchived(job, JobState.Completed, "job-completed-successfully", "Printed");
            }
            catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
            {
                FinishArchived(job, JobState.Canceled, "job-canceled-by-user", "Canceled");
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                FinishArchived(job, JobState.Aborted, "aborted-by-system", "Service stopped");
                return;
            }
            catch (NotSupportedException ex)
            {
                FinishArchived(job, JobState.Aborted, "document-format-error", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {Id} failed", job.Info.Id);
                FinishArchived(job, JobState.Aborted, "job-aborted-by-system", ex.Message);
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
            Darkness = job.Darkness is { } d ? Math.Clamp(d, 1, 5) : settings.Darkness,
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
        var keep = settings.JobHistoryKeep;
        if (keep > 0)
            _archive.Begin(job.Info.Id);
        IEnumerable<RasterResult> pages = job.Bitmaps is { } bitmaps
            ? bitmaps
            : ImageDecoder.Decode(job.Document!, job.Format, job.Options is { } o ? o.Includes : null)
                .Select(p => Rasterizer.RasterizeWithMode(p, raster));

        // In continuous mode pages are joined into one strip: only the last page sends the page-end
        // sequence. A one-page lookahead tells which page is last without loading the whole document.
        var continuous = settings.ContinuousPages;
        var gapRows = settings.PageGapMm * _printer.Profile.Dpi / 25;
        var printed = 0;
        var index = 0;
        (MonoBitmap Page, PrintOptions Options)? pending = null;
        var pageOpen = false;
        try
        {
            foreach (var (page, pageIsText) in pages)
            {
                ct.ThrowIfCancellationRequested();
                SavePageDiagnostics(++index, page);
                if (page.Height == 0)
                    continue; // blank page after trimming
                if (keep > 0)
                    KeepPage(job, page, pageIsText);
                var isText = settings.PrintMode switch
                {
                    PrintModeChoice.Text => true,
                    PrintModeChoice.Image => false,
                    _ => pageIsText,
                };
                var pageOptions = options with { IsText = isText };
                if (!continuous)
                {
                    await PrintPageAsync(job, page, pageOptions, endsPage: true, ct);
                    Update(job, j => j with { PagesCompleted = ++printed });
                    continue;
                }

                if (pending is { } previous)
                {
                    await PrintPageAsync(job, previous.Page, previous.Options, endsPage: false, ct);
                    pageOpen = true;
                    Update(job, j => j with { PagesCompleted = ++printed });
                }
                pending = (printed + (pending is null ? 0 : 1) == 0 ? page : WithTopGap(page, gapRows), pageOptions);
            }

            if (pending is { } last)
            {
                await PrintPageAsync(job, last.Page, last.Options, endsPage: true, ct);
                pageOpen = false;
                Update(job, j => j with { PagesCompleted = ++printed });
            }
        }
        catch (OperationCanceledException) when (pageOpen && !_pageEndSent)
        {
            await ClosePageAsync(options);
            throw;
        }
    }

    /// <summary>Writes down what was kept and then marks the job finished, so that whoever sees it finished also finds its pages.</summary>
    private void FinishArchived(QueuedJob job, JobState state, string reason, string message)
    {
        EndArchive(job);
        lock (_gate)
            Finish(job, state, reason, message);
    }

    private void KeepPage(QueuedJob job, MonoBitmap page, bool isText)
    {
        job.PagesSeen++;
        job.AnyText |= isText;
        if (job.PagesSeen <= JobArchive.MaxPagesPerJob && _archive.AddPage(job.Info.Id, job.PagesSeen, page))
            job.PagesKept++;
    }

    /// <summary>Writes down what was kept of the job (pages and what is needed to print it again) and trims the archive.</summary>
    private void EndArchive(QueuedJob job)
    {
        var keep = _settings.Current.JobHistoryKeep;
        if (job.PagesSeen == 0 && keep > 0)
        {
            _archive.End(job.Info.Id, new JobArchiveEntry(job.Info.Name, 0, false, job.Darkness), keep);
            return;
        }
        // A job that was cancelled or failed keeps the pages it reached and can be printed again; one with more pages than are kept cannot.
        var complete = job.PagesKept == job.PagesSeen;
        _archive.End(job.Info.Id, new JobArchiveEntry(job.Info.Name, job.PagesKept, job.AnyText, job.Darkness, Truncated: !complete), keep);
    }

    /// <summary>Whether the pages of a finished job were kept and it can be printed again.</summary>
    public bool CanReprint(int id) => _archive.CanReprint(id);

    public string? KeptPagePath(int id, int number)
    {
        if (_archive.PagePath(id, number) is { } kept)
            return kept;
        // With the history off only the last job is kept, for the preview of the panel.
        try
        {
            var first = File.ReadLines(Path.Combine(_diagnosticsDir, "job.txt")).FirstOrDefault();
            var page = Path.Combine(_diagnosticsDir, $"page-{number}.png");
            return first == id.ToString() && File.Exists(page) ? page : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Prints the kept pages of a finished job again as a new job. Returns null when nothing was kept
    /// and throws <see cref="InvalidOperationException"/> when the job is not finished or cannot be printed again.
    /// </summary>
    public JobInfo? Reprint(int id)
    {
        lock (_gate)
        {
            if (_jobs.TryGetValue(id, out var running) && !running.Info.IsTerminal)
                throw new InvalidOperationException("The job has not finished yet.");
        }
        if (_archive.Find(id) is not { } entry || entry.Pages == 0)
            return null;
        if (entry.Truncated)
            throw new InvalidOperationException("Only part of the job was kept, so it cannot be printed again.");
        var pages = _archive.LoadPages(id, entry);
        return SubmitBitmaps($"Reimpresión de {entry.Name}", pages, entry.IsText, "MiniPrinter", entry.Darkness, JobSource.Panel, Environment.UserName);
    }

    /// <summary>Adds <paramref name="rows"/> blank rows above a page (gap between continuous pages).</summary>
    private static MonoBitmap WithTopGap(MonoBitmap page, int rows)
    {
        if (rows <= 0)
            return page;
        var result = new MonoBitmap(page.Width, page.Height + rows);
        for (var y = 0; y < page.Height; y++)
            page.Row(y).CopyTo(result.MutableRow(y + rows));
        return result;
    }

    /// <summary>Best-effort page end after a cancelled continuous job.</summary>
    private async Task ClosePageAsync(PrintOptions options)
    {
        try
        {
            var builder = new PrintJobBuilder(_printer.Profile);
            await _printer.RequireSession().UseAsync(
                (connection, token) => connection.SendAsync(builder.BuildPageEnd(options), _printer.Stream, token),
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is TransportException or TimeoutException or PrinterNotConfiguredException)
        {
            _logger.LogDebug(ex, "Could not close the page after cancelling");
        }
    }

    private async Task PrintPageAsync(QueuedJob job, MonoBitmap page, PrintOptions options, bool endsPage, CancellationToken ct)
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
                _pageEndSent = false;
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
                        // Close the page when it is the last one, and always when interrupted, so the
                        // printer is left in a clean state.
                        if (endsPage || !sent)
                        {
                            await connection.SendAsync(builder.BuildPageEnd(options), _printer.Stream, CancellationToken.None);
                            _pageEndSent = true;
                        }
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
        if (format is "image/pwg-raster" or "image/jpeg" or "image/png" or "application/pdf")
            return true;
        if (format is not (null or "application/octet-stream"))
            return false;
        Span<byte> head = stackalloc byte[8];
        var read = document.Read(head);
        document.Position = 0;
        head = head[..read];
        return PwgRasterReader.IsPwgRaster(head)
               || PdfRasterizer.IsPdf(head)
               || head.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G'])
               || head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]);
    }

    private sealed class QueuedJob(JobInfo info)
    {
        public JobInfo Info { get; set; } = info;
        public Stream? Document { get; set; }
        public string? Format { get; set; }
        public int? Darkness { get; set; }
        public DocumentOptions? Options { get; set; }
        public IReadOnlyList<RasterResult>? Bitmaps { get; set; }
        public int PagesSeen { get; set; }
        public int PagesKept { get; set; }
        public bool AnyText { get; set; }
        public CancellationTokenSource Cancellation { get; } = new();
    }

    private sealed class PrinterAlarmException(DeviceState state) : Exception($"Printer alarm 0x{state.AlarmByte:X2}")
    {
        public DeviceState State { get; } = state;
    }
}
