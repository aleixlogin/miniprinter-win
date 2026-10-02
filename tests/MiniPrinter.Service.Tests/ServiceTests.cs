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

public class TelemetryTests
{
    private static DeviceState State(byte alarm, byte sensor, byte battery) =>
        (DeviceState)PrinterMessage.Parse(new Frame(Opcode.GetDeviceState, Frame.FlagsFromPrinter, [alarm, sensor, battery]));

    [Fact]
    public void Records_rows_and_exports_with_single_header()
    {
        var now = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(2));
        var log = new TelemetryLog(new ServicePaths(TestEnv.TempDirectory()), NullLogger<TelemetryLog>.Instance, clock: () => now);
        log.Record(State(0, 14, 40), MiniPrinter.Transport.LinkState.Connected);
        now = now.AddDays(1);
        log.Record(State(1, 27, 39), MiniPrinter.Transport.LinkState.Connected);

        var lines = log.Export().Trim().Split(Environment.NewLine);
        Assert.Equal(TelemetryLog.Header, lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.EndsWith(",Connected,0,14,40", lines[1]);
        Assert.EndsWith(",Connected,1,27,39", lines[2]);
        Assert.Equal(2, Directory.GetFiles(log.Directory, "status-*.csv").Length);
    }

    [Fact]
    public void Rotates_large_files_and_deletes_old_ones()
    {
        var dir = TestEnv.TempDirectory();
        var telemetry = Path.Combine(dir, "telemetry");
        Directory.CreateDirectory(telemetry);
        File.WriteAllText(Path.Combine(telemetry, "status-20260801.csv"), TelemetryLog.Header);   // > 30 days old
        var now = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
        var log = new TelemetryLog(new ServicePaths(dir), NullLogger<TelemetryLog>.Instance, clock: () => now) { MaxFileBytes = 200 };

        Assert.False(File.Exists(Path.Combine(telemetry, "status-20260801.csv")));
        for (var i = 0; i < 20; i++)
            log.Record(State(0, 14, 40), MiniPrinter.Transport.LinkState.Connected);
        Assert.True(Directory.GetFiles(telemetry, "status-20261002*.csv").Length > 1);
        Assert.Equal(21, log.Export().Trim().Split(Environment.NewLine).Length);
    }

    [Theory]
    [InlineData(33, 0)] [InlineData(30, 0)] [InlineData(35, 25)] [InlineData(37, 50)] [InlineData(39, 70)] [InlineData(42, 100)] [InlineData(45, 100)]
    public void Decivolts_curve(int raw, int percent) => Assert.Equal(percent, BatteryInterpreter.FromDecivolts(raw));

    [Fact]
    public void Interpretation_depends_on_unit()
    {
        Assert.Null(BatteryInterpreter.Percent(39, BatteryUnit.Unknown));
        Assert.Equal(39, BatteryInterpreter.Percent(39, BatteryUnit.Percent));
        Assert.Equal("39 (valor en bruto)", BatteryInterpreter.Describe(39, BatteryUnit.Unknown));
        Assert.StartsWith("3,9 V", BatteryInterpreter.Describe(39, BatteryUnit.Decivolts).Replace('.', ','));
    }

    [Fact]
    public void Low_battery_warns_once_and_rearms_with_hysteresis()
    {
        var tracker = new LowBatteryTracker();
        Assert.False(tracker.Update(21, 20));
        Assert.True(tracker.Update(19, 20));
        Assert.False(tracker.Update(18, 20));   // only once per discharge
        Assert.False(tracker.Update(22, 20));   // still low (needs 25)
        Assert.True(tracker.IsLow);
        Assert.False(tracker.Update(25, 20));
        Assert.False(tracker.IsLow);
        Assert.True(tracker.Update(10, 20));    // re-armed
    }
}

public class SettingsTests
{
    [Fact]
    public void Old_settings_file_loads_with_new_defaults()
    {
        var dir = TestEnv.TempDirectory();
        // settings.json as written by version 0.1.0 (before printing-enhancements).
        File.WriteAllText(Path.Combine(dir, "settings.json"), """
            {"printer":{"name":"X5h-E07A","address":"7A:E0:0C:1D:87:AE","profileKey":"d1","transport":"Rfcomm","port":null},
             "darkness":3,"dither":"Auto","feedPadding":12,"extraFeedSteps":0,"idleTimeoutSeconds":60,
             "networkMode":"Lan","ippPort":8631,"printerName":"X5h Thermal Printer","jobRetryMinutes":10}
            """);
        var settings = new SettingsStore(new ServicePaths(dir), NullLogger<SettingsStore>.Instance).Current;

        Assert.Equal("X5h-E07A", settings.Printer?.Name);
        Assert.Equal(NetworkMode.Lan, settings.NetworkMode);
        Assert.Equal(PrintModeChoice.Auto, settings.PrintMode);
        Assert.False(settings.ContinuousPages);
        Assert.Equal(4, settings.PageGapMm);
        Assert.Equal(BatteryUnit.Unknown, settings.BatteryUnit);
        Assert.False(settings.AutomationApiEnabled);
        Assert.Equal(10, settings.TextSizePt);
        Assert.False(settings.KeepAlive);
        Assert.Equal(30, settings.KeepAliveIntervalSeconds);
    }

    [Fact]
    public void New_settings_are_clamped()
    {
        var v = SettingsStore.Validate(new ServiceSettings { PageGapMm = 999, LowBatteryPercent = 1, TextSizePt = 200, TextFont = " " });
        Assert.Equal(50, v.PageGapMm);
        Assert.Equal(5, v.LowBatteryPercent);
        Assert.Equal(48, v.TextSizePt);
        Assert.Equal("Segoe UI", v.TextFont);
        Assert.Equal(10, SettingsStore.Validate(new ServiceSettings { KeepAliveIntervalSeconds = 2 }).KeepAliveIntervalSeconds);
        Assert.Equal(300, SettingsStore.Validate(new ServiceSettings { KeepAliveIntervalSeconds = 9999 }).KeepAliveIntervalSeconds);
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

    private static byte[] PhotoPng()
    {
        using var image = new Image<L8>(384, 200);
        for (var y = 0; y < 200; y++)
        for (var x = 0; x < 384; x++)
            image[x, y] = new L8((byte)((x + y) * 255 / 583));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private (bool Text, int Energy)[] PageModes()
    {
        var frames = new FrameDecoder().Feed(_printer.Simulated!.AllWrittenBytes);
        var modes = new List<(bool, int)>();
        int energy = 0;
        foreach (var f in frames)
        {
            if (f.Command == Opcode.Energy) energy = f.Payload[0] | (f.Payload[1] << 8);
            if (f.Command == Opcode.PrintMode) modes.Add((f.Payload[0] == 1, energy));
        }
        return modes.ToArray();
    }

    [Fact]
    public async Task Text_page_uses_text_mode_and_photo_uses_image_mode()
    {
        var text = Submit(TestEnv.Png(384, 100));
        var photo = Submit(PhotoPng());
        await TestEnv.WaitUntil(() => _queue.GetJob(photo.Id)!.IsTerminal && _queue.GetJob(text.Id)!.IsTerminal);

        Assert.Equal([(true, 8000), (false, 5000)], PageModes());
    }

    [Fact]
    public async Task Mixed_document_chooses_mode_per_page()
    {
        using var pwg = new MemoryStream();
        var textPage = new GrayImage(384, 60);
        for (var x = 0; x < 300; x++) textPage[x, 30] = 0;
        var photoPage = new GrayImage(384, 60, Enumerable.Range(0, 384 * 60).Select(i => (byte)(i % 384 * 255 / 383)).ToArray());
        PwgRasterWriter.Write(pwg, [textPage, photoPage]);
        var job = _queue.CreateJob("mixed", "tester");
        _queue.SubmitDocument(job.Id, new MemoryStream(pwg.ToArray()), "image/pwg-raster", true);
        await TestEnv.WaitUntil(() => _queue.GetJob(job.Id)!.IsTerminal);

        Assert.Equal([(true, 8000), (false, 5000)], PageModes());
    }

    [Theory]
    [InlineData(PrintModeChoice.Image, false, 5000)]
    [InlineData(PrintModeChoice.Text, true, 8000)]
    public async Task Forced_mode_applies_to_every_page(PrintModeChoice mode, bool text, int energy)
    {
        _settings.Update(s => s with { PrintMode = mode });
        var a = Submit(TestEnv.Png(384, 100));
        var b = Submit(PhotoPng());
        await TestEnv.WaitUntil(() => _queue.GetJob(a.Id)!.IsTerminal && _queue.GetJob(b.Id)!.IsTerminal);

        Assert.Equal([(text, energy), (text, energy)], PageModes());
    }

    private static byte[] ThreePagePwg(int rowsPerPage = 80)
    {
        using var pwg = new MemoryStream();
        var pages = Enumerable.Range(0, 3).Select(_ =>
        {
            var page = new GrayImage(384, rowsPerPage);
            for (var x = 0; x < 300; x++) { page[x, 10] = 0; page[x, rowsPerPage - 10] = 0; }
            return page;
        }).ToList();
        PwgRasterWriter.Write(pwg, pages);
        return pwg.ToArray();
    }

    private int CountFeeds() =>
        new FrameDecoder().Feed(_printer.Simulated!.AllWrittenBytes).Count(f => f.Command == Opcode.FeedPaper);

    [Theory]
    [InlineData(false, 6)]   // 3 pages × 2 paper steps
    [InlineData(true, 2)]    // one page end for the whole strip
    public async Task Continuous_pages_send_a_single_page_end(bool continuous, int feeds)
    {
        _settings.Update(s => s with { ContinuousPages = continuous });
        var job = _queue.CreateJob("3 pages", "tester");
        _queue.SubmitDocument(job.Id, new MemoryStream(ThreePagePwg()), "image/pwg-raster", true);
        await TestEnv.WaitUntil(() => _queue.GetJob(job.Id)!.IsTerminal);

        Assert.Equal(JobState.Completed, _queue.GetJob(job.Id)!.State);
        Assert.Equal(3, _queue.GetJob(job.Id)!.PagesCompleted);
        Assert.Equal(feeds, CountFeeds());
        Assert.Equal(Opcode.GetDeviceState, new FrameDecoder().Feed(_printer.Simulated!.AllWrittenBytes)[^1].Command);
    }

    [Fact]
    public async Task Continuous_pages_are_separated_by_the_gap()
    {
        _settings.Update(s => s with { ContinuousPages = true, PageGapMm = 4 });
        var job = _queue.CreateJob("gap", "tester");
        _queue.SubmitDocument(job.Id, new MemoryStream(ThreePagePwg()), "image/pwg-raster", true);
        await TestEnv.WaitUntil(() => _queue.GetJob(job.Id)!.IsTerminal);

        var rows = new FrameDecoder().Feed(_printer.Simulated!.AllWrittenBytes)
            .Count(f => f.Command is Opcode.RleRow or Opcode.RasterRow);
        // Each trimmed page is 61 rows (+8 margin rows) and pages 2 and 3 get a 32-row (4 mm) gap.
        Assert.Equal(3 * 69 + 2 * 32, rows);
    }

    [Fact]
    public async Task Cancelling_a_continuous_job_closes_the_page()
    {
        await _printer.RefreshAsync(CancellationToken.None);
        _settings.Update(s => s with { ContinuousPages = true });
        var fake = _printer.Simulated!;
        var job = _queue.CreateJob("cancel strip", "tester");
        var cancelled = false;
        fake.OnWrite = (f, written) =>
        {
            // Cancel once the first page has been fully sent (second page header seen).
            var headers = new FrameDecoder().Feed(f.AllWrittenBytes).Count(x => x.Command == Opcode.Darkness);
            if (!cancelled && headers >= 2) { cancelled = true; _queue.CancelJob(job.Id); }
        };
        _queue.SubmitDocument(job.Id, new MemoryStream(ThreePagePwg(1500)), "image/pwg-raster", true);
        await TestEnv.WaitUntil(() => _queue.GetJob(job.Id)!.IsTerminal);

        Assert.Equal(JobState.Canceled, _queue.GetJob(job.Id)!.State);
        Assert.Equal(2, CountFeeds()); // exactly one page end
        Assert.Equal(Opcode.GetDeviceState, new FrameDecoder().Feed(fake.AllWrittenBytes)[^1].Command);
    }

    [Fact]
    public async Task Pdf_prints_and_damaged_pdf_does_not_block_the_queue()
    {
        var damaged = _queue.CreateJob("roto.pdf", "tester");
        _queue.SubmitDocument(damaged.Id, new MemoryStream(MiniPrinter.Imaging.Tests.MiniPdf.Damaged()), "application/pdf", true);
        var good = _queue.CreateJob("bueno.pdf", "tester");
        _queue.SubmitDocument(good.Id, new MemoryStream(MiniPrinter.Imaging.Tests.MiniPdf.Create(3)), "application/octet-stream", true,
            new DocumentOptions { PageRanges = [new IppRange(2, 3)] });
        await TestEnv.WaitUntil(() => _queue.GetJob(good.Id)!.IsTerminal);

        Assert.Equal(JobState.Aborted, _queue.GetJob(damaged.Id)!.State);
        Assert.Equal("PDF dañado o no válido", _queue.GetJob(damaged.Id)!.StateMessage);
        Assert.Equal(JobState.Completed, _queue.GetJob(good.Id)!.State);
        Assert.Equal(2, _queue.GetJob(good.Id)!.PagesCompleted);   // page-ranges 2-3
    }

    [Fact]
    public void Unsupported_document_is_rejected_up_front()
    {
        var job = _queue.CreateJob("pdf", "tester");
        Assert.Throws<NotSupportedException>(() =>
            _queue.SubmitDocument(job.Id, new MemoryStream("PKzip"u8.ToArray()), "application/octet-stream", true));
        Assert.Throws<NotSupportedException>(() =>
            _queue.SubmitDocument(job.Id, new MemoryStream([1]), "application/postscript", true));
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
    public async Task Sampling_export_and_low_battery_warning()
    {
        using var client = Client();
        await client.SelectPrinterAsync(TestEnv.Simulated);
        var status = await client.StartSamplingAsync(TimeSpan.FromMinutes(30));
        Assert.NotNull(status.SamplingUntil);
        await client.ConnectAsync();

        var csv = await client.ExportTelemetryAsync();
        Assert.StartsWith(TelemetryLog.Header, csv);
        Assert.Contains(",0,14,40", csv);
        await client.StopSamplingAsync();
        Assert.Null((await client.GetStatusAsync()).SamplingUntil);

        // Simulated battery byte is 40: with unit Percent and threshold 50 it is low.
        var settings = await client.GetSettingsAsync();
        await client.SaveSettingsAsync(settings with { BatteryUnit = BatteryUnit.Percent, LowBatteryPercent = 50 });
        await client.ConnectAsync();
        var low = await client.GetStatusAsync();
        Assert.Equal(40, low.BatteryPercent);
        Assert.True(low.LowBattery);
        Assert.Contains("other-warning", PrinterReasons());
    }

    private IEnumerable<string> PrinterReasons()
    {
        var request = new IppMessage { Code = IppOperation.GetPrinterAttributes, RequestId = 1 };
        request.GetOrAddGroup(IppGroupTag.Operation)
            .Add("attributes-charset", IppValue.Charset("utf-8"))
            .Add("attributes-natural-language", IppValue.Language("en"))
            .Add("printer-uri", IppValue.Uri($"ipp://127.0.0.1:{_ippPort}/ipp/print"));
        using var http = new HttpClient();
        var body = new ByteArrayContent(IppCodec.Encode(request));
        body.Headers.ContentType = new("application/ipp");
        var response = http.PostAsync($"http://127.0.0.1:{_ippPort}/ipp/print", body).Result;
        var ipp = IppCodec.ReadAsync(response.Content.ReadAsStream()).Result;
        return ipp.Group(IppGroupTag.Printer)!["printer-state-reasons"]!.Values.Select(v => v.AsString());
    }

    [Fact]
    public async Task Print_text_file_and_preview_through_control_api()
    {
        using var client = Client();
        await client.SelectPrinterAsync(TestEnv.Simulated);

        var preview = await client.PreviewTextAsync("Comprar leche");
        Assert.Equal(0x89, preview[0]);

        var text = await client.PrintTextAsync("Comprar leche\nPan", sizePt: 14);
        Assert.Equal("Comprar leche", text.Name);
        var png = await client.PrintFileAsync(TestEnv.Png(384, 100), "foto.png");
        var txt = await client.PrintFileAsync(System.Text.Encoding.UTF8.GetBytes("Nota en un TXT ñ"), "nota.txt");
        Assert.Equal("nota.txt", txt.Name);
        await TestEnv.WaitUntil(() => client.GetJobsAsync().Result.Count(j => j.State == "Completed") == 3);

        var error = await Assert.ThrowsAsync<ControlApiException>(() => client.PrintTextAsync("   "));
        Assert.Equal(400, error.StatusCode);
        var unsupported = await Assert.ThrowsAsync<ControlApiException>(() => client.PrintFileAsync([1, 2, 3], "x.bin"));
        Assert.Equal(400, unsupported.StatusCode);
    }

    [Fact]
    public async Task Templates_list_preview_and_print()
    {
        using var client = Client();
        await client.SelectPrinterAsync(TestEnv.Simulated);

        var templates = await client.GetTemplatesAsync();
        Assert.Equal(["qr", "barcode", "todo", "label", "sticker"], templates.Select(t => t.Name));
        Assert.Contains(templates.Single(t => t.Name == "qr").Fields, f => f.Name == "data" && f.Required);

        var fields = new Dictionary<string, string> { ["data"] = "https://example.com" };
        Assert.Equal(0x89, (await client.PreviewTemplateAsync("qr", fields))[0]);
        var job = await client.PrintTemplateAsync("qr", fields);
        Assert.Equal("Código QR", job.Name);
        await TestEnv.WaitUntil(() => client.GetJobsAsync().Result.Single(j => j.Id == job.Id).State == "Completed");

        var unknown = await Assert.ThrowsAsync<ControlApiException>(() => client.PrintTemplateAsync("no-existe", fields));
        Assert.Equal(400, unknown.StatusCode);
        Assert.Contains("qr, barcode", unknown.Message);
        var missing = await Assert.ThrowsAsync<ControlApiException>(() => client.PrintTemplateAsync("label", new Dictionary<string, string>()));
        Assert.Contains("'title'", missing.Message);
    }

    private HttpClient Api(string? token)
    {
        var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_ippPort}/api/v1/") };
        if (token is not null)
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static StringContent JsonBody(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task Automation_api_is_disabled_by_default_and_requires_token()
    {
        using var client = Client();
        await client.SelectPrinterAsync(TestEnv.Simulated);
        using var anonymous = Api(null);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsync("print/text", JsonBody("{\"text\":\"Hola\"}"))).StatusCode);

        var settings = await client.GetSettingsAsync();
        await client.SaveSettingsAsync(settings with { AutomationApiEnabled = true });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("print/text", JsonBody("{\"text\":\"Hola\"}"))).StatusCode);
        using var wrong = Api("not-the-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.PostAsync("print/text", JsonBody("{\"text\":\"Hola\"}"))).StatusCode);

        var info = await client.GetAutomationAsync();
        Assert.True(info.Enabled);
        using var api = Api(info.Token);
        var response = await api.PostAsync("print/text", JsonBody("{\"text\":\"Hola desde la API\",\"fontSize\":14}"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var jobId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("jobId").GetInt32();
        await TestEnv.WaitUntil(() =>
            JsonDocument.Parse(api.GetStringAsync($"jobs/{jobId}").Result).RootElement.GetProperty("state").GetString() == "completed");

        // Regenerating the token invalidates the old one immediately.
        var regenerated = await client.RegenerateAutomationTokenAsync();
        Assert.NotEqual(info.Token, regenerated.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.PostAsync("print/text", JsonBody("{\"text\":\"x\"}"))).StatusCode);
    }

    [Fact]
    public async Task Automation_api_prints_image_qr_template_and_validates()
    {
        using var client = Client();
        await client.SelectPrinterAsync(TestEnv.Simulated);
        var settings = await client.GetSettingsAsync();
        await client.SaveSettingsAsync(settings with { AutomationApiEnabled = true });
        using var api = Api((await client.GetAutomationAsync()).Token);

        var png = new ByteArrayContent(TestEnv.Png(384, 100));
        png.Headers.ContentType = new("image/png");
        Assert.Equal(HttpStatusCode.Accepted, (await api.PostAsync("print/image", png)).StatusCode);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(TestEnv.Png(200, 80));
        file.Headers.ContentType = new("image/png");
        form.Add(file, "file", "foto.png");
        Assert.Equal(HttpStatusCode.Accepted, (await api.PostAsync("print/image", form)).StatusCode);

        Assert.Equal(HttpStatusCode.Accepted, (await api.PostAsync("print/qr", JsonBody("{\"data\":\"https://example.com\"}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await api.PostAsync("print/template/todo", JsonBody("{\"title\":\"Compra\",\"items\":\"Pan\\nLeche\"}"))).StatusCode);

        var unknown = await api.PostAsync("print/template/no-existe", JsonBody("{}"));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("qr, barcode, todo, label, sticker", await unknown.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsync("print/text", JsonBody("{bad json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsync("print/text", JsonBody($"{{\"text\":\"{new string('a', 20_001)}\"}}"))).StatusCode);

        // Control endpoints never exist on the IPP/automation listener.
        using var raw = new HttpClient();
        foreach (var path in new[] { "/api/settings", "/api/status", "/api/printer", "/api/automation" })
            Assert.Equal(HttpStatusCode.NotFound, (await raw.GetAsync($"http://127.0.0.1:{_ippPort}{path}")).StatusCode);
        await TestEnv.WaitUntil(() => client.GetJobsAsync().Result.Count(j => j.State == "Completed") == 4);
    }

    [Fact]
    public async Task Changing_the_printer_keeps_a_battery_sampling_in_progress()
    {
        using var client = Client();
        await client.SelectPrinterAsync(TestEnv.Simulated);
        var started = await client.StartSamplingAsync(TimeSpan.FromMinutes(45));
        Assert.NotNull(started.SamplingUntil);

        // Re-selecting the printer (e.g. another transport) recreates the session.
        await client.SelectPrinterAsync(TestEnv.Simulated with { Name = "X5h-E07A (otra)" });
        await TestEnv.WaitUntil(() => client.GetStatusAsync().Result.Printer?.Name == "X5h-E07A (otra)");
        var status = await client.GetStatusAsync();
        Assert.NotNull(status.SamplingUntil);
        Assert.Equal(started.SamplingUntil!.Value.ToUnixTimeSeconds(), status.SamplingUntil!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Keep_alive_setting_connects_and_is_reported()
    {
        using var client = Client();
        await client.SelectPrinterAsync(TestEnv.Simulated);
        Assert.False((await client.GetStatusAsync()).KeepAlive);

        var settings = await client.GetSettingsAsync();
        await client.SaveSettingsAsync(settings with { KeepAlive = true, KeepAliveIntervalSeconds = 10 });
        await TestEnv.WaitUntil(() => client.GetStatusAsync().Result is { KeepAlive: true, Link: "Connected" });

        // Turning it off keeps the session (no reconnect) but returns to on-demand mode.
        await client.SaveSettingsAsync((await client.GetSettingsAsync()) with { KeepAlive = false });
        var status = await client.GetStatusAsync();
        Assert.False(status.KeepAlive);
        Assert.Equal("X5h-E07A", status.Printer?.Name);
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
