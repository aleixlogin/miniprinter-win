using System.Text.Json;
using MiniPrinter.Imaging;
using MiniPrinter.Protocol;

namespace MiniPrinter.Service;

/// <summary>
/// The pages of the latest jobs, kept as 1-bit PNGs in <c>jobs\&lt;id&gt;\</c> so that the panel can show a job again and
/// print it again exactly as it came out. Bounded by the number of jobs, by their total size and by the pages of a job.
/// </summary>
public sealed class JobArchive
{
    public const int MaxPagesPerJob = 40;
    public const long MaxTotalBytes = 50L * 1024 * 1024;

    private readonly string _root;
    private readonly ILogger<JobArchive> _logger;
    private readonly object _gate = new();

    public JobArchive(ServicePaths paths, ILogger<JobArchive> logger)
    {
        _root = Path.Combine(paths.DataDirectory, "jobs");
        _logger = logger;
        Clear();
    }

    public string Root => _root;

    /// <summary>Limit of the total size of what is kept (adjustable for tests).</summary>
    public long MaxBytes { get; init; } = MaxTotalBytes;

    private string JobDirectory(int id) => Path.Combine(_root, id.ToString());

    /// <summary>Job ids start again at 1 each time the service starts, so nothing kept before is about a job of this run.</summary>
    private void Clear()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not clean the job archive");
        }
    }

    /// <summary>Starts keeping a job. The pages are added as they are printed.</summary>
    public void Begin(int id)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(JobDirectory(id));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not start keeping job {Id}", id);
        }
    }

    /// <summary>Keeps a page (the first <see cref="MaxPagesPerJob"/> only). Returns false when it was not kept.</summary>
    public bool AddPage(int id, int number, MonoBitmap page)
    {
        if (number > MaxPagesPerJob)
            return false;
        try
        {
            MonoPng.Save(page, Path.Combine(JobDirectory(id), $"page-{number}.png"));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            _logger.LogDebug(ex, "Could not keep a page of job {Id}", id);
            return false;
        }
    }

    /// <summary>Closes a job: writes what is needed to print it again and trims the archive to its limits.</summary>
    public void End(int id, JobArchiveEntry entry, int keep)
    {
        try
        {
            lock (_gate)
            {
                if (!Directory.Exists(JobDirectory(id)))
                    return;
                if (entry.Pages == 0 || keep <= 0)
                {
                    Directory.Delete(JobDirectory(id), recursive: true);
                    return;
                }
                File.WriteAllText(Path.Combine(JobDirectory(id), "job.json"), JsonSerializer.Serialize(entry));
                Trim(keep);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not close job {Id}", id);
        }
    }

    /// <summary>Drops the oldest jobs beyond <paramref name="keep"/> and, then, until the total fits the size limit.</summary>
    public void Trim(int keep)
    {
        lock (_gate)
        {
            if (!Directory.Exists(_root))
                return;
            var jobs = Directory.EnumerateDirectories(_root)
                .Select(d => (Path: d, Id: int.TryParse(Path.GetFileName(d), out var id) ? id : -1))
                .Where(j => j.Id >= 0)
                .OrderBy(j => j.Id)
                .ToList();
            var surplus = jobs.Count - Math.Max(keep, 0);
            foreach (var old in jobs.Take(Math.Max(surplus, 0)))
                Delete(old.Path);
            jobs = [.. jobs.Skip(Math.Max(surplus, 0))];
            var total = jobs.Sum(j => SizeOf(j.Path));
            foreach (var old in jobs.SkipLast(1))
            {
                if (total <= MaxBytes)
                    break;
                total -= SizeOf(old.Path);
                Delete(old.Path);
            }
        }
    }

    private static long SizeOf(string directory) =>
        new DirectoryInfo(directory).EnumerateFiles().Sum(f => f.Length);

    private void Delete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete {Directory}", directory);
        }
    }

    /// <summary>What was kept of a finished job, or null.</summary>
    public JobArchiveEntry? Find(int id)
    {
        try
        {
            var path = Path.Combine(JobDirectory(id), "job.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<JobArchiveEntry>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool CanReprint(int id) => Find(id) is { Pages: > 0, Truncated: false };

    /// <summary>The path of a kept page (1-based), or null.</summary>
    public string? PagePath(int id, int number)
    {
        var path = Path.Combine(JobDirectory(id), $"page-{number}.png");
        return File.Exists(path) ? path : null;
    }

    /// <summary>The kept pages in order, for printing the job again.</summary>
    public IReadOnlyList<MonoBitmap> LoadPages(int id, JobArchiveEntry entry) =>
        [.. Enumerable.Range(1, entry.Pages).Select(n => MonoPng.Load(Path.Combine(JobDirectory(id), $"page-{n}.png")))];
}

/// <summary>
/// What is needed to print a kept job again. <see cref="Truncated"/> is set when the job had more pages than are kept
/// (it can be shown, not printed again).
/// </summary>
public sealed record JobArchiveEntry(string Name, int Pages, bool IsText, int? Darkness, bool Truncated = false);
