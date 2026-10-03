using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MiniPrinter.Control;
using MiniPrinter.Protocol;
using MiniPrinter.Service;

namespace MiniPrinter.Service.Tests;

/// <summary>job-history: the pages of the latest jobs are kept, bounded, and can be shown and printed again.</summary>
public sealed class JobArchiveTests
{
    private static JobArchive NewArchive(out string dir, long? maxBytes = null)
    {
        dir = TestEnv.TempDirectory();
        var paths = new ServicePaths(dir);
        return maxBytes is { } max
            ? new JobArchive(paths, NullLogger<JobArchive>.Instance) { MaxBytes = max }
            : new JobArchive(paths, NullLogger<JobArchive>.Instance);
    }

    private static MonoBitmap Page(int seed, int height = 60)
    {
        var bitmap = new MonoBitmap(384, height);
        var random = new Random(seed);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < 384; x++)
            bitmap.MutableRow(y)[x] = (byte)random.Next(2);
        return bitmap;
    }

    private static void Keep(JobArchive archive, int id, int keep, int pages = 1)
    {
        archive.Begin(id);
        for (var n = 1; n <= pages; n++)
            archive.AddPage(id, n, Page(id * 100 + n));
        archive.End(id, new JobArchiveEntry($"job {id}", pages, true, 3), keep);
    }

    [Fact]
    public void It_keeps_only_the_latest_jobs()
    {
        var archive = NewArchive(out _);
        for (var id = 1; id <= 5; id++)
            Keep(archive, id, keep: 3);
        Assert.Equal([3, 4, 5], Enumerable.Range(1, 5).Where(archive.CanReprint));
    }

    [Fact]
    public void Keeping_none_keeps_nothing()
    {
        var archive = NewArchive(out _);
        Keep(archive, 1, keep: 0);
        Assert.False(archive.CanReprint(1));
        Assert.Null(archive.PagePath(1, 1));
    }

    [Fact]
    public void It_drops_the_oldest_when_the_total_size_is_over_the_limit()
    {
        var archive = NewArchive(out _, maxBytes: 20_000);
        for (var id = 1; id <= 6; id++)
            Keep(archive, id, keep: 50);
        var kept = Enumerable.Range(1, 6).Where(archive.CanReprint).ToList();
        Assert.NotEmpty(kept);
        Assert.True(kept.Count < 6);
        Assert.Contains(6, kept);
        Assert.Equal(Enumerable.Range(kept.Min(), 7 - kept.Min()), kept); // the oldest went first
    }

    [Fact]
    public void It_does_not_keep_more_than_forty_pages_of_a_job()
    {
        var archive = NewArchive(out _);
        archive.Begin(1);
        Assert.True(archive.AddPage(1, JobArchive.MaxPagesPerJob, Page(1, 8)));
        Assert.False(archive.AddPage(1, JobArchive.MaxPagesPerJob + 1, Page(1, 8)));
        Assert.Null(archive.PagePath(1, JobArchive.MaxPagesPerJob + 1));
    }

    [Fact]
    public void A_job_that_was_cut_short_is_shown_but_cannot_be_printed_again()
    {
        var archive = NewArchive(out _);
        archive.Begin(1);
        archive.AddPage(1, 1, Page(1));
        archive.End(1, new JobArchiveEntry("corto", 1, false, null, Truncated: true), keep: 5);
        Assert.NotNull(archive.PagePath(1, 1));
        Assert.False(archive.CanReprint(1));
    }

    [Fact]
    public void What_was_kept_by_an_earlier_run_is_cleaned_at_startup()
    {
        var archive = NewArchive(out var dir);
        Keep(archive, 1, keep: 5);
        Assert.True(archive.CanReprint(1));
        var restarted = new JobArchive(new ServicePaths(dir), NullLogger<JobArchive>.Instance);
        Assert.False(restarted.CanReprint(1));
        Assert.False(Directory.Exists(Path.Combine(dir, "jobs", "1")));
    }

    [Fact]
    public void Pages_read_back_are_the_pages_that_were_kept()
    {
        var archive = NewArchive(out _);
        var page = Page(7);
        archive.Begin(1);
        archive.AddPage(1, 1, page);
        archive.End(1, new JobArchiveEntry("uno", 1, true, 4), keep: 5);
        var back = archive.LoadPages(1, archive.Find(1)!).Single();
        Assert.Equal(page.Height, back.Height);
        for (var y = 0; y < page.Height; y++)
            Assert.True(page.Row(y).SequenceEqual(back.Row(y)), $"row {y}");
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(10, 10)]
    [InlineData(500, 50)]
    public void The_number_of_jobs_kept_is_validated(int given, int expected) =>
        Assert.Equal(expected, SettingsStore.Validate(new ServiceSettings { JobHistoryKeep = given }).JobHistoryKeep);
}

/// <summary>job-history: the endpoints of the control API over a service with the simulated printer.</summary>
public sealed class JobHistoryApiTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _dataDir = null!;

    public Task InitializeAsync()
    {
        _dataDir = TestEnv.TempDirectory();
        File.WriteAllText(Path.Combine(_dataDir, "settings.json"), JsonSerializer.Serialize(
            new ServiceSettings { IppPort = TestEnv.FreePort(), Printer = TestEnv.Simulated }, ControlDefaults.Json));
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("DataDirectory", _dataDir);
            b.UseEnvironment("Development");
        });
        _ = _factory.Server;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private ControlClient Client() =>
        new(File.ReadAllText(Path.Combine(_dataDir, "control.token")), handler: _factory.Server.CreateHandler());

    private static async Task<JobDto> CompletedAsync(ControlClient client, int id)
    {
        JobDto? job = null;
        await TestEnv.WaitUntilAsync(async () =>
        {
            job = (await client.GetJobsAsync()).SingleOrDefault(j => j.Id == id);
            return job?.State == "Completed";
        });
        return job!;
    }

    [Fact]
    public async Task A_finished_job_can_be_shown_and_printed_again()
    {
        using var client = Client();
        var original = await CompletedAsync(client, (await client.PrintTextAsync("Ticket de ejemplo")).Id);
        Assert.True(original.CanReprint);

        var page = await client.GetJobPageAsync(original.Id, 1);
        Assert.NotNull(page);
        Assert.Equal(0x89, page![0]); // PNG
        Assert.Null(await client.GetJobPageAsync(original.Id, 99));

        var again = await client.ReprintJobAsync(original.Id);
        Assert.StartsWith("Reimpresión de ", again.Name);
        Assert.Equal("Panel", again.Source);
        Assert.NotEqual(original.Id, again.Id);
        Assert.True((await CompletedAsync(client, again.Id)).Pages >= 1);
    }

    [Fact]
    public async Task The_last_job_pages_endpoint_keeps_working()
    {
        using var client = Client();
        await CompletedAsync(client, (await client.PrintTextAsync("Ultimo")).Id);
        Assert.NotNull(await client.GetLastJobPageAsync(1));
    }

    [Fact]
    public async Task Unknown_jobs_have_no_pages_and_cannot_be_printed_again()
    {
        using var client = Client();
        Assert.Null(await client.GetJobPageAsync(4242, 1));
        var error = await Assert.ThrowsAsync<ControlApiException>(() => client.ReprintJobAsync(4242));
        Assert.Equal(404, error.StatusCode);
    }

    [Fact]
    public async Task With_zero_jobs_kept_nothing_can_be_printed_again()
    {
        using var client = Client();
        await client.SaveSettingsAsync((await client.GetSettingsAsync()) with { JobHistoryKeep = 0 });
        var first = await CompletedAsync(client, (await client.PrintTextAsync("Privado")).Id);
        Assert.False(first.CanReprint);
        Assert.NotNull(await client.GetJobPageAsync(first.Id, 1)); // the last job is always kept, for the preview
        var error = await Assert.ThrowsAsync<ControlApiException>(() => client.ReprintJobAsync(first.Id));
        Assert.Equal(404, error.StatusCode);

        var second = await CompletedAsync(client, (await client.PrintTextAsync("Otro")).Id);
        Assert.NotNull(await client.GetJobPageAsync(second.Id, 1));
        Assert.Null(await client.GetJobPageAsync(first.Id, 1)); // only the last one
    }

    [Fact]
    public void A_job_that_has_not_finished_cannot_be_printed_again()
    {
        var queue = _factory.Services.GetRequiredService<JobQueue>();
        var job = queue.CreateJob("sin documento", "tester"); // created, and its document never arrives
        Assert.Throws<InvalidOperationException>(() => queue.Reprint(job.Id));
        queue.CancelJob(job.Id);
    }

    [Fact]
    public async Task A_job_that_was_cancelled_before_printing_cannot_be_printed_again()
    {
        using var client = Client();
        var queue = _factory.Services.GetRequiredService<JobQueue>();
        var job = queue.CreateJob("cancelado", "tester");
        queue.CancelJob(job.Id);
        var error = await Assert.ThrowsAsync<ControlApiException>(() => client.ReprintJobAsync(job.Id));
        Assert.Equal(404, error.StatusCode);
    }
}
