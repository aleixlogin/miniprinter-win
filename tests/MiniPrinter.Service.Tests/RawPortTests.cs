using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MiniPrinter.Control;
using MiniPrinter.Service;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace MiniPrinter.Service.Tests;

/// <summary>raw-print-port: the RAW/JetDirect listener against real TCP clients.</summary>
public sealed class RawPortTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _dataDir = null!;
    private int _ippPort;
    private int _rawPort;
    private RawPortHost _host = null!;

    public async Task InitializeAsync()
    {
        _dataDir = TestEnv.TempDirectory();
        _ippPort = TestEnv.FreePort();
        _rawPort = TestEnv.FreePort();
        File.WriteAllText(Path.Combine(_dataDir, "settings.json"), JsonSerializer.Serialize(
            new ServiceSettings { IppPort = _ippPort, RawPort = _rawPort, RawPortEnabled = true, Printer = TestEnv.Simulated }, ControlDefaults.Json));
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("DataDirectory", _dataDir);
            b.UseSetting("RawPort:SilenceSeconds", "0.4");
            b.UseSetting("RawPort:IdleSeconds", "1.5");
            b.UseEnvironment("Development");
        });
        _ = _factory.Server; // start
        _host = _factory.Services.GetRequiredService<RawPortHost>();
        await _host.WaitForCurrentAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        using var client = Client();
        await client.ConnectAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private ControlClient Client() =>
        new(File.ReadAllText(Path.Combine(_dataDir, "control.token")), handler: _factory.Server.CreateHandler());

    private async Task<TcpClient> ConnectAsync()
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _rawPort);
        return tcp;
    }

    private async Task SendAsync(byte[] data, bool close = true)
    {
        using var tcp = await ConnectAsync();
        var stream = tcp.GetStream();
        await stream.WriteAsync(data);
        if (close)
            tcp.Client.Shutdown(SocketShutdown.Send);
        // Wait for the server to finish reading (it closes after queueing).
        await ReadToEndAsync(stream, TimeSpan.FromSeconds(5));
    }

    private static async Task<byte[]> ReadToEndAsync(NetworkStream stream, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var buffer = new byte[256];
        var received = new List<byte>();
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, cts.Token)) > 0)
                received.AddRange(buffer.AsSpan(0, read).ToArray());
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        return received.ToArray();
    }

    private async Task<IReadOnlyList<JobDto>> JobsAsync(int expected, string? name = null)
    {
        using var client = Client();
        IReadOnlyList<JobDto> jobs = [];
        await TestEnv.WaitUntilAsync(async () =>
        {
            jobs = [.. (await client.GetJobsAsync()).Where(j => name is null || j.Name == name)];
            return jobs.Count >= expected && jobs.All(j => j.State == "Completed");
        });
        return jobs;
    }

    private static byte[] Jpeg()
    {
        using var image = new Image<L8>(200, 100, new L8(0));
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream, new JpegEncoder());
        return stream.ToArray();
    }

    private static byte[] Ticket(string text) => [0x1B, (byte)'@', .. Encoding.ASCII.GetBytes(text), 0x0A, 0x1D, (byte)'V', 66, 0];

    // ---- enabling, scope and status ------------------------------------------------------------------

    [Fact]
    public void The_port_is_off_by_default_and_old_settings_files_change_nothing()
    {
        var defaults = SettingsStore.Validate(JsonSerializer.Deserialize<ServiceSettings>("""{"darkness":4,"ippPort":8631}""", ControlDefaults.Json)!);
        Assert.False(defaults.RawPortEnabled);
        Assert.Equal(9100, defaults.RawPort);
        Assert.Equal(9100, SettingsStore.Validate(new ServiceSettings { RawPort = 80 }).RawPort);
        Assert.Equal(9100, SettingsStore.Validate(new ServiceSettings { RawPort = 70000 }).RawPort);
        Assert.Equal(1024, SettingsStore.Validate(new ServiceSettings { RawPort = 1024 }).RawPort);
    }

    [Fact]
    public async Task The_status_reports_the_listening_port()
    {
        using var client = Client();
        var raw = (await client.GetStatusAsync()).RawPort!;
        Assert.True(raw.Enabled);
        Assert.True(raw.Listening);
        Assert.Equal(_rawPort, raw.Port);
        Assert.Null(raw.Error);
        Assert.Contains($"127.0.0.1:{_rawPort}", raw.Addresses);
    }

    [Fact]
    public async Task Disabling_and_enabling_the_port_opens_and_closes_it_without_restarting_the_service()
    {
        using var client = Client();
        var settings = await client.GetSettingsAsync();
        await client.SaveSettingsAsync(settings with { RawPortEnabled = false });
        await _host.WaitForCurrentAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        await Assert.ThrowsAsync<SocketException>(async () => { using var tcp = await ConnectAsync(); });
        Assert.False((await client.GetStatusAsync()).RawPort!.Listening);

        await client.SaveSettingsAsync(settings with { RawPortEnabled = true });
        await _host.WaitForCurrentAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        using var again = await ConnectAsync();
        Assert.True(again.Connected);
    }

    [Fact]
    public async Task A_busy_port_is_reported_and_the_rest_of_the_service_keeps_working()
    {
        var busy = TestEnv.FreePort();
        using var occupant = new TcpListener(IPAddress.Loopback, busy);
        occupant.Start();
        using var client = Client();
        await client.SaveSettingsAsync((await client.GetSettingsAsync()) with { RawPort = busy });
        await _host.WaitForCurrentAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        var raw = (await client.GetStatusAsync()).RawPort!;
        Assert.False(raw.Listening);
        Assert.Contains("ocupado", raw.Error);
        Assert.Equal("Connected", (await client.GetStatusAsync()).Link);
    }

    [Fact]
    public async Task An_invalid_port_is_rejected_by_the_api()
    {
        using var client = Client();
        var error = await Assert.ThrowsAsync<ControlApiException>(async () =>
            await client.SaveSettingsAsync((await client.GetSettingsAsync()) with { RawPort = 80 }));
        Assert.Equal(400, error.StatusCode);
        Assert.Contains("1024", error.Message);
    }

    // ---- content ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_png_sent_raw_prints_as_a_document_from_raw_at_the_client_address()
    {
        await SendAsync(TestEnv.Png(384, 120));
        var job = Assert.Single(await JobsAsync(1, "Raw PNG"));
        Assert.Equal("raw@127.0.0.1", job.User);
        Assert.Equal(1, job.Pages);
    }

    [Fact]
    public async Task A_jpeg_sent_raw_prints_too()
    {
        await SendAsync(Jpeg());
        Assert.Single(await JobsAsync(1, "Raw JPEG"));
    }

    [Fact]
    public async Task Plain_utf8_text_is_printed_with_the_text_renderer()
    {
        await SendAsync(Encoding.UTF8.GetBytes("Nota de prueba ñandú\nSegunda línea\n"));
        var job = Assert.Single(await JobsAsync(1, "Nota de prueba ñandú"));
        Assert.Equal("raw@127.0.0.1", job.User);
    }

    [Fact]
    public async Task An_escpos_ticket_is_one_job_per_cut()
    {
        var stream = new List<byte>();
        stream.AddRange(Ticket("UNO"));
        stream.AddRange(Ticket("DOS"));
        stream.AddRange(Ticket("TRES"));
        await SendAsync(stream.ToArray());
        var jobs = await JobsAsync(3, "Ticket ESC/POS");
        Assert.Equal(3, jobs.Count);
        Assert.All(jobs, j => Assert.Equal("raw@127.0.0.1", j.User));
    }

    [Fact]
    public async Task An_escpos_ticket_without_a_cut_ends_when_the_client_closes()
    {
        await SendAsync([0x1B, (byte)'@', .. "sin corte\n"u8.ToArray()]);
        Assert.Single(await JobsAsync(1, "Ticket ESC/POS"));
    }

    [Fact]
    public async Task Silence_ends_the_ticket_even_if_the_connection_stays_open()
    {
        using var tcp = await ConnectAsync();
        await tcp.GetStream().WriteAsync(new byte[] { 0x1B, (byte)'@' }.Concat("silencio\n"u8.ToArray()).ToArray());
        Assert.Single(await JobsAsync(1, "Ticket ESC/POS"));   // the client never closed nor cut
        Assert.True(tcp.Connected);
    }

    [Fact]
    public async Task A_status_request_is_answered_on_the_same_connection()
    {
        using var tcp = await ConnectAsync();
        await tcp.GetStream().WriteAsync(new byte[] { 0x10, 0x04, 4 });
        var reply = new byte[1];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.Equal(1, await tcp.GetStream().ReadAsync(reply, cts.Token));
        Assert.Equal(0x12, reply[0]);   // online, paper present

        // out of paper → 0x72, as the status of the (simulated) printer changes
        using var client = Client();
        var printer = _factory.Services.GetRequiredService<PrinterManager>();
        await printer.RefreshAsync(CancellationToken.None);
        printer.Simulated!.AlarmByte = 0x01;
        await printer.RefreshAsync(CancellationToken.None);
        await tcp.GetStream().WriteAsync(new byte[] { 0x10, 0x04, 4 });
        Assert.Equal(1, await tcp.GetStream().ReadAsync(reply, cts.Token));
        Assert.Equal(0x72, reply[0]);
        printer.Simulated.AlarmByte = 0x00;
    }

    [Fact]
    public async Task Unknown_binary_content_is_rejected_without_a_job()
    {
        await SendAsync([0x00, 0x01, 0x02, 0x03, 0xFE, 0xFF, 0x80, 0x81]);
        await Task.Delay(300);
        using var client = Client();
        Assert.Empty(await client.GetJobsAsync());
    }

    // ---- limits ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_fifth_simultaneous_connection_is_closed()
    {
        var open = new List<TcpClient>();
        try
        {
            for (var i = 0; i < RawPortHost.MaxConnections; i++)
            {
                var tcp = await ConnectAsync();
                open.Add(tcp);
            }
            await Task.Delay(200);      // let the server take them
            using var fifth = await ConnectAsync();
            var read = await fifth.GetStream().ReadAsync(new byte[1], new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);
            Assert.Equal(0, read);      // closed by the server
        }
        finally
        {
            foreach (var tcp in open) tcp.Dispose();
        }
    }

    [Fact]
    public async Task A_connection_that_sends_nothing_is_closed_after_the_idle_time()
    {
        using var tcp = await ConnectAsync();
        var read = await tcp.GetStream().ReadAsync(new byte[1], new CancellationTokenSource(TimeSpan.FromSeconds(6)).Token);
        Assert.Equal(0, read);
    }

    [Fact]
    public async Task More_than_16_mb_closes_the_connection_and_prints_nothing()
    {
        using var tcp = await ConnectAsync();
        var chunk = new byte[1024 * 1024];
        chunk[0] = 0x89; chunk[1] = (byte)'P'; chunk[2] = (byte)'N'; chunk[3] = (byte)'G';
        try
        {
            for (var i = 0; i < RawPortHost.MaxBytes / chunk.Length + 4; i++)
                await tcp.GetStream().WriteAsync(chunk);
        }
        catch (IOException)
        {
            // the server closed while we were still sending
        }
        await Task.Delay(300);
        using var client = Client();
        Assert.Empty(await client.GetJobsAsync());
    }

    [Fact]
    public async Task Jobs_wait_for_paper_like_any_other_job()
    {
        var printer = _factory.Services.GetRequiredService<PrinterManager>();
        await printer.RefreshAsync(CancellationToken.None);
        printer.Simulated!.AlarmByte = 0x01;
        await SendAsync(Ticket("sin papel"));
        using var client = Client();
        await Task.Delay(500);
        Assert.DoesNotContain(await client.GetJobsAsync(), j => j.State == "Completed");
        printer.Simulated.AlarmByte = 0x00;
        Assert.Single(await JobsAsync(1, "Ticket ESC/POS"));
    }

    [Fact]
    public async Task A_connection_that_asks_for_absurd_amounts_of_paper_is_closed()
    {
        using var tcp = await ConnectAsync();
        var stream = tcp.GetStream();
        var text = new byte[64 * 1024];
        Array.Fill(text, (byte)(char)87);
        text[^1] = 0x0A;
        await stream.WriteAsync(new byte[] { 0x1B, (byte)'@', 0x1D, (byte)'!', 0x77 });
        try
        {
            for (var i = 0; i < 40; i++)           // 2.5 MB of 8×8 characters
                await stream.WriteAsync(text);
        }
        catch (IOException)
        {
            // closed while sending: expected
        }
        try
        {
            // closed by the server: a clean end or a reset (it closes with unread data)
            Assert.Equal(0, await stream.ReadAsync(new byte[1], new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token));
        }
        catch (IOException)
        {
        }
        using var client = Client();
        await Task.Delay(500);
        var jobs = await client.GetJobsAsync();
        Assert.InRange(jobs.Count, 1, 12);          // the paper it got is capped (160000 dots = at most 10 full tickets)
    }


    // ---- detection ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D }, RawContent.Png)]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, RawContent.Jpeg)]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, RawContent.Pdf)]
    [InlineData(new byte[] { 0x52, 0x61, 0x53, 0x32, 0, 0 }, RawContent.PwgRaster)]
    [InlineData(new byte[] { 0x52, 0x61, 0x53, 0x74, 0, 0 }, RawContent.PwgRaster)]
    [InlineData(new byte[] { 0x1B, 0x40 }, RawContent.EscPos)]
    [InlineData(new byte[] { 0x1D, 0x21, 0x11 }, RawContent.EscPos)]
    [InlineData(new byte[] { 0x10, 0x04, 0x01 }, RawContent.EscPos)]
    [InlineData(new byte[] { 0x48, 0x6F, 0x6C, 0x61, 0x0A, 0x1B, 0x61, 0x01 }, RawContent.EscPos)]   // text first, commands later
    [InlineData(new byte[] { 0x48, 0x6F, 0x6C, 0x61, 0x0A }, RawContent.PlainText)]
    [InlineData(new byte[] { 0xC3, 0xB1, 0x61, 0x0D, 0x0A }, RawContent.PlainText)]               // "ña"
    [InlineData(new byte[] { 0x00, 0x01, 0xFE, 0xFF }, RawContent.Unknown)]
    [InlineData(new byte[] { 0x48, 0xFF, 0xFE }, RawContent.Unknown)]                              // not UTF-8
    public void Content_is_recognised_from_the_first_bytes(byte[] head, RawContent expected) =>
        Assert.Equal(expected, RawPortHost.Detect(head, final: true));

    [Fact]
    public void A_short_chunk_that_could_start_a_signature_waits_for_more_bytes()
    {
        Assert.Equal(RawContent.NeedMore, RawPortHost.Detect([0x89, 0x50], final: false));
        Assert.Equal(RawContent.NeedMore, RawPortHost.Detect([0x25, 0x50, 0x44], final: false));
        Assert.Equal(RawContent.NeedMore, RawPortHost.Detect([], final: false));
        Assert.Equal(RawContent.PlainText, RawPortHost.Detect("Hola"u8, final: false));
    }
}
