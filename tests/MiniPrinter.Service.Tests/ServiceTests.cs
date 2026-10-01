using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MiniPrinter.Control;
using MiniPrinter.Imaging;
using MiniPrinter.Ipp;
using MiniPrinter.Protocol;
using MiniPrinter.Service;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MiniPrinter.Service.Tests;

internal static class TestEnv
{
    public static string TempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "miniprinter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static PrinterSelection Simulated => new()
    {
        Name = "X5h-E07A",
        Address = "7A:E0:0C:1D:87:AE",
        ProfileKey = "d1",
        Transport = TransportChoice.Simulated,
    };

    public static byte[] Png(int width, int height)
    {
        using var image = new Image<L8>(width, height, new L8(255));
        for (var y = height / 2 - 10; y < height / 2 + 10; y++)
        for (var x = 0; x < width; x++)
            image[x, y] = new L8(0);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    public static async Task WaitUntil(Func<bool> condition, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("condition not reached");
            await Task.Delay(25);
        }
    }
}

public sealed class JobQueueTests : IAsyncLifetime
{
    private SettingsStore _settings = null!;
    private PrinterManager _printer = null!;
    private JobQueue _queue = null!;

    public async Task InitializeAsync()
    {
        _settings = new SettingsStore(new ServicePaths(TestEnv.TempDirectory()), NullLogger<SettingsStore>.Instance);
        _settings.Update(s => s with { Printer = TestEnv.Simulated, JobRetryMinutes = 1 });
        _printer = new PrinterManager(_settings, NullLogger<PrinterManager>.Instance);
        _queue = new JobQueue(_printer, _settings, new ServicePaths(TestEnv.TempDirectory()), NullLogger<JobQueue>.Instance) { RetryInterval = TimeSpan.FromMilliseconds(50) };
        await _queue.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _queue.DisposeAsync();
        await _printer.DisposeAsync();
    }

    private JobInfo Submit(byte[] document, string? format = "image/png")
    {
        var job = _queue.CreateJob("doc", "tester");
        return _queue.SubmitDocument(job.Id, new MemoryStream(document), format, lastDocument: true);
    }

    [Fact]
    public async Task Prints_png_through_the_d1_recipe()
    {
        var job = Submit(TestEnv.Png(800, 400));
        await TestEnv.WaitUntil(() => _queue.GetJob(job.Id)!.IsTerminal);

        Assert.Equal(JobState.Completed, _queue.GetJob(job.Id)!.State);
        Assert.Equal(1, _queue.GetJob(job.Id)!.PagesCompleted);
        var frames = new FrameDecoder().Feed(_printer.Simulated!.AllWrittenBytes);
        Assert.Contains(frames, f => f.Command == Opcode.Darkness);
        Assert.Contains(frames, f => f.Command is Opcode.RleRow or Opcode.RasterRow);
        Assert.Equal(2, frames.Count(f => f.Command == Opcode.FeedPaper));
    }

    [Fact]
    public async Task Holds_job_while_printer_reports_alarm_then_prints()
    {
        await _printer.RefreshAsync(CancellationToken.None);
        _printer.Simulated!.AlarmByte = 0x01; // out of paper
        var job = Submit(TestEnv.Png(384, 100));

        await TestEnv.WaitUntil(() => _queue.GetJob(job.Id)!.State == JobState.ProcessingStopped);
        var snapshot = _queue.GetPrinter();
        Assert.Equal(PrinterState.Stopped, snapshot.State);
        Assert.Contains("Printer needs attention", _queue.GetJob(job.Id)!.StateMessage);

        _printer.Simulated.AlarmByte = 0x00;
        await TestEnv.WaitUntil(() => _queue.GetJob(job.Id)!.IsTerminal);
        Assert.Equal(JobState.Completed, _queue.GetJob(job.Id)!.State);
    }

    [Fact]
    public async Task Cancelling_a_held_job_finishes_it_as_canceled()
    {
        await _printer.RefreshAsync(CancellationToken.None);
        _printer.Simulated!.AlarmByte = 0x02;
        var job = Submit(TestEnv.Png(384, 100));
        await TestEnv.WaitUntil(() => _queue.GetJob(job.Id)!.State == JobState.ProcessingStopped);

        Assert.True(_queue.CancelJob(job.Id));
        await TestEnv.WaitUntil(() => _queue.GetJob(job.Id)!.IsTerminal);
        Assert.Equal(JobState.Canceled, _queue.GetJob(job.Id)!.State);
    }

    [Fact]
    public async Task Cancelling_mid_print_closes_the_page()
    {
        var fake = (await GetFakeAsync());
        var job = _queue.CreateJob("long", "tester");
        var cancelled = false;
        fake.OnWrite = (f, written) =>
        {
            if (!cancelled && f.Writes.Count > 20)
            {
                cancelled = true;
                _queue.CancelJob(job.Id);
            }
        };
        using var pwg = new MemoryStream();
        PwgRasterWriter.Write(pwg, [new GrayImage(384, 3000, Enumerable.Range(0, 384 * 3000).Select(i => (byte)(i % 7 == 0 ? 0 : 255)).ToArray())]);
        _queue.SubmitDocument(job.Id, new MemoryStream(pwg.ToArray()), "image/pwg-raster", true);

        await TestEnv.WaitUntil(() => _queue.GetJob(job.Id)!.IsTerminal);
        Assert.Equal(JobState.Canceled, _queue.GetJob(job.Id)!.State);
        var frames = new FrameDecoder().Feed(fake.AllWrittenBytes);
        var rows = frames.Count(f => f.Command is Opcode.RleRow or Opcode.RasterRow);
        Assert.InRange(rows, 1, 2999);
        Assert.Equal(Opcode.GetDeviceState, frames[^1].Command); // page end sequence was sent
    }

    [Fact]
    public void Unsupported_document_is_rejected_up_front()
    {
        var job = _queue.CreateJob("pdf", "tester");
        Assert.Throws<NotSupportedException>(() =>
            _queue.SubmitDocument(job.Id, new MemoryStream("%PDF-1.7"u8.ToArray()), "application/octet-stream", true));
        Assert.Throws<NotSupportedException>(() =>
            _queue.SubmitDocument(job.Id, new MemoryStream([1]), "application/pdf", true));
    }

    [Fact]
    public async Task Jobs_run_one_at_a_time_in_order()
    {
        var a = Submit(TestEnv.Png(384, 300));
        var b = Submit(TestEnv.Png(384, 300));
        Assert.True(_queue.GetJob(b.Id)!.State == JobState.Pending);
        await TestEnv.WaitUntil(() => _queue.GetJob(b.Id)!.IsTerminal);
        Assert.True(_queue.GetJob(a.Id)!.Completed <= _queue.GetJob(b.Id)!.Processing);
    }

    private async Task<MiniPrinter.Transport.FakePrinterTransport> GetFakeAsync()
    {
        await _printer.RefreshAsync(CancellationToken.None);
        return _printer.Simulated!;
    }
}

public sealed class ControlApiTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _dataDir = null!;
    private int _ippPort;

    public Task InitializeAsync()
    {
        _dataDir = TestEnv.TempDirectory();
        _ippPort = TestEnv.FreePort();
        File.WriteAllText(Path.Combine(_dataDir, "settings.json"),
            JsonSerializer.Serialize(new ServiceSettings { IppPort = _ippPort }, ControlDefaults.Json));
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("DataDirectory", _dataDir);
            b.UseEnvironment("Development");
        });
        _ = _factory.Server; // start
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private ControlClient Client(string? token = null) =>
        new(token ?? File.ReadAllText(Path.Combine(_dataDir, "control.token")), handler: _factory.Server.CreateHandler());

    [Fact]
    public async Task Rejects_requests_without_token()
    {
        using var client = Client("wrong");
        var error = await Assert.ThrowsAsync<ControlApiException>(() => client.GetStatusAsync());
        Assert.Equal(401, error.StatusCode);
    }

    [Fact]
    public async Task Reports_unconfigured_printer_and_conflict_on_connect()
    {
        using var client = Client();
        var status = await client.GetStatusAsync();
        Assert.Null(status.Printer);
        Assert.Contains($"http://127.0.0.1:{_ippPort}/ipp/print", status.IppUrls);
        var error = await Assert.ThrowsAsync<ControlApiException>(() => client.ConnectAsync());
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task Select_connect_test_print_and_settings_round_trip()
    {
        using var client = Client();
        await client.SelectPrinterAsync(TestEnv.Simulated);
        var status = await client.ConnectAsync();
        Assert.Equal("Connected", status.Link);
        Assert.True(status.Ready);
        Assert.Equal("3.0.5", status.Firmware);
        Assert.Equal(0x28, status.BatteryLevel);
        Assert.Equal(0x0E, status.PaperSensor);

        var job = await client.TestPrintAsync();
        await TestEnv.WaitUntil(() => client.GetJobsAsync().Result.Single(j => j.Id == job.Id).State == "Completed");
        var preview = await client.GetLastJobPageAsync(1);
        Assert.NotNull(preview);
        Assert.Equal(0x89, preview[0]); // PNG signature

        var settings = await client.GetSettingsAsync();
        var saved = await client.SaveSettingsAsync(settings with { Darkness = 9, Dither = DitherChoice.Threshold });
        Assert.Equal(5, saved.Darkness); // clamped
        Assert.Equal(DitherChoice.Threshold, (await client.GetSettingsAsync()).Dither);
    }

    [Fact]
    public async Task Saving_stale_settings_keeps_the_selected_printer()
    {
        using var client = Client();
        var stale = await client.GetSettingsAsync();          // loaded before a printer was chosen
        Assert.Null(stale.Printer);
        await client.SelectPrinterAsync(TestEnv.Simulated);

        var saved = await client.SaveSettingsAsync(stale with { Darkness = 4 });

        Assert.Equal("X5h-E07A", saved.Printer?.Name);
        Assert.Equal(4, saved.Darkness);
        Assert.Equal("X5h-E07A", (await client.GetStatusAsync()).Printer?.Name);
    }

    [Fact]
    public async Task Ipp_print_job_reaches_the_simulated_printer()
    {
        using var client = Client();
        await client.SelectPrinterAsync(TestEnv.Simulated);

        var request = new IppMessage { Code = IppOperation.PrintJob, RequestId = 1 };
        request.GetOrAddGroup(IppGroupTag.Operation)
            .Add("attributes-charset", IppValue.Charset("utf-8"))
            .Add("attributes-natural-language", IppValue.Language("en"))
            .Add("printer-uri", IppValue.Uri($"ipp://127.0.0.1:{_ippPort}/ipp/print"))
            .Add("job-name", IppValue.Name("from-ipp"))
            .Add("document-format", IppValue.Mime("image/png"));
        using var http = new HttpClient();
        var body = new ByteArrayContent([.. IppCodec.Encode(request), .. TestEnv.Png(384, 200)]);
        body.Headers.ContentType = new("application/ipp");
        var response = await http.PostAsync($"http://127.0.0.1:{_ippPort}/ipp/print", body);
        var ipp = await IppCodec.ReadAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(IppStatus.SuccessfulOk, ipp.Code);

        await TestEnv.WaitUntil(() => client.GetJobsAsync().Result.Any(j => j.Name == "from-ipp" && j.State == "Completed"));
    }

    [Fact]
    public async Task Events_stream_pushes_status()
    {
        using var client = Client();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var updates = client.WatchStatusAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await updates.MoveNextAsync());
        Assert.Equal("Disconnected", updates.Current.Link);

        await client.SelectPrinterAsync(TestEnv.Simulated);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (updates.Current.Printer is null && DateTime.UtcNow < deadline)
            Assert.True(await updates.MoveNextAsync());
        Assert.Equal("X5h-E07A", updates.Current.Printer!.Name);
    }
}
