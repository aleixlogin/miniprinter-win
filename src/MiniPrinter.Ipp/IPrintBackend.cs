namespace MiniPrinter.Ipp;

/// <summary>IPP job states (RFC 8011 §5.3.7).</summary>
public enum JobState
{
    Pending = 3,
    PendingHeld = 4,
    Processing = 5,
    ProcessingStopped = 6,
    Canceled = 7,
    Aborted = 8,
    Completed = 9,
}

/// <summary>IPP printer states (RFC 8011 §5.4.11).</summary>
public enum PrinterState
{
    Idle = 3,
    Processing = 4,
    Stopped = 5,
}

/// <summary>Which entrance of the service a job came through.</summary>
public enum JobSource
{
    /// <summary>The Windows printer (IPP), the usual way.</summary>
    Windows,

    /// <summary>The tray application (control API): test pages, quick notes, clipboard, templates.</summary>
    Panel,

    /// <summary>The automation API.</summary>
    Api,

    /// <summary>The RAW port 9100.</summary>
    Raw,
}

public sealed record JobInfo
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string UserName { get; init; } = "anonymous";

    /// <summary>The entrance the job came through (not derived from <see cref="UserName"/>, which clients choose).</summary>
    public JobSource Source { get; init; } = JobSource.Windows;

    /// <summary>Who sent it: the Windows user, or the address of the client.</summary>
    public string? Origin { get; init; }
    public JobState State { get; init; } = JobState.Pending;

    /// <summary>job-state-reasons keywords, e.g. "job-printing", "job-completed-successfully".</summary>
    public IReadOnlyList<string> StateReasons { get; init; } = ["none"];

    public string? StateMessage { get; init; }
    public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? Processing { get; init; }
    public DateTimeOffset? Completed { get; init; }
    public int PagesCompleted { get; init; }
    public long SizeBytes { get; init; }

    public bool IsTerminal => State is JobState.Canceled or JobState.Aborted or JobState.Completed;
}

/// <summary>Live state of the printer as exposed through IPP.</summary>
public sealed record PrinterSnapshot(
    PrinterState State,
    IReadOnlyList<string> StateReasons,
    string? StateMessage,
    bool AcceptingJobs,
    int QueuedJobs);

/// <summary>Per-document options from the IPP request.</summary>
public sealed record DocumentOptions
{
    /// <summary>1-based inclusive page ranges (IPP <c>page-ranges</c>); null or empty means all pages.</summary>
    public IReadOnlyList<IppRange>? PageRanges { get; init; }

    public bool Includes(int page) =>
        PageRanges is not { Count: > 0 } ranges || ranges.Any(r => page >= r.Lower && page <= r.Upper);

    /// <summary>Parses "2-3,5" (CLI syntax).</summary>
    public static DocumentOptions ParsePages(string? pages)
    {
        if (string.IsNullOrWhiteSpace(pages))
            return new DocumentOptions();
        var ranges = new List<IppRange>();
        foreach (var part in pages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bounds = part.Split('-', 2, StringSplitOptions.TrimEntries);
            if (!int.TryParse(bounds[0], out var from) || from < 1)
                throw new FormatException($"Invalid page range '{part}'.");
            var to = from;
            if (bounds.Length == 2 && (!int.TryParse(bounds[1], out to) || to < from))
                throw new FormatException($"Invalid page range '{part}'.");
            ranges.Add(new IppRange(from, to));
        }
        return new DocumentOptions { PageRanges = ranges };
    }
}

/// <summary>What the IPP front-end needs from the print service.</summary>
public interface IPrintBackend
{
    PrinterSnapshot GetPrinter();

    /// <summary>Creates a job that will receive its document later (Create-Job).</summary>
    JobInfo CreateJob(string name, string userName);

    /// <summary>
    /// Queues a document for a job. The backend takes ownership of <paramref name="document"/>
    /// (already buffered, positioned at 0). Throws <see cref="NotSupportedException"/> for formats
    /// it cannot print.
    /// </summary>
    JobInfo SubmitDocument(int jobId, Stream document, string? format, bool lastDocument, DocumentOptions? options = null);

    JobInfo? GetJob(int jobId);

    IReadOnlyList<JobInfo> GetJobs(bool completed, int limit);

    /// <summary>Returns false when the job does not exist or already finished.</summary>
    bool CancelJob(int jobId);

    /// <summary>Makes the printer identify itself (e.g. a short paper feed).</summary>
    Task IdentifyAsync(CancellationToken cancellationToken);
}
