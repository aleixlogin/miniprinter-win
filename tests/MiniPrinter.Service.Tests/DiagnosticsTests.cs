using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MiniPrinter.Control;
using MiniPrinter.Escpos;
using MiniPrinter.Imaging;
using MiniPrinter.Protocol;
using MiniPrinter.Service;

namespace MiniPrinter.Service.Tests;

/// <summary>diagnostics-panel: the checks of the service, each with the state a test gives it.</summary>
public class DiagnosticsServiceTests
{
    private static StatusDto Healthy() => new()
    {
        Link = "Connected",
        Printer = new PrinterSelection { Name = "X5h", Address = "AA" },
        LastSeen = DateTimeOffset.UtcNow,
    };

    private sealed class Probes
    {
        public StatusDto Status = Healthy();
        public WindowsQueueDto Queue = new(true, "X5h Thermal Printer", "Idle", null, null);
        public Func<CancellationToken, Task<bool>> Ipp = _ => Task.FromResult(true);
        public ServiceSettings Settings = new();
        public RawPortDto Raw = new(false, 9100, false, null, []);
        public bool? Firewall = true;
        public FontSupport Fonts = new(true, true);
        public Func<CancellationToken, Task<WindowsQueueDto>>? QueueProbe;

        public DiagnosticsService Build(TimeSpan? timeout = null) => new(new DiagnosticProbes(
            () => Status, QueueProbe ?? (_ => Task.FromResult(Queue)), Ipp, () => Settings, () => Raw, () => Firewall, () => Fonts), timeout);
    }

    private static async Task<DiagnosticCheckDto> Check(Probes probes, string id, TimeSpan? timeout = null) =>
        (await probes.Build(timeout).RunAsync(CancellationToken.None)).Single(c => c.Id == id);

    [Fact]
    public async Task A_healthy_service_passes_every_check()
    {
        var results = await new Probes().Build().RunAsync(CancellationToken.None);
        Assert.Equal([DiagnosticIds.Link, DiagnosticIds.Paper, DiagnosticIds.Queue, DiagnosticIds.Ipp, DiagnosticIds.Raw, DiagnosticIds.Fonts],
            results.Select(r => r.Id));
        Assert.All(results, r => Assert.Equal(DiagnosticStatus.Ok, r.Status));
    }

    [Theory]
    [InlineData("Connected", DiagnosticStatus.Ok)]
    [InlineData("Connecting", DiagnosticStatus.Warn)]
    [InlineData("Error", DiagnosticStatus.Fail)]
    [InlineData("Disconnected", DiagnosticStatus.Ok)] // it connects by itself when something is printed
    public async Task The_link_follows_the_state_of_the_connection(string link, string expected)
    {
        var probes = new Probes { Status = Healthy() with { Link = link, LastError = "sin enlace" } };
        var check = await Check(probes, DiagnosticIds.Link);
        Assert.Equal(expected, check.Status);
        if (link == "Error")
            Assert.Equal("sin enlace", check.Detail);
    }

    [Fact]
    public async Task Without_a_printer_the_link_fails_and_the_paper_is_unknown()
    {
        var probes = new Probes { Status = Healthy() with { Printer = null } };
        Assert.Equal(DiagnosticStatus.Fail, (await Check(probes, DiagnosticIds.Link)).Status);
        Assert.Equal(DiagnosticStatus.Unknown, (await Check(probes, DiagnosticIds.Paper)).Status);
    }

    [Fact]
    public async Task An_alarm_is_a_warning_and_no_data_yet_is_unknown_not_a_failure()
    {
        var alarm = new Probes { Status = Healthy() with { Alarms = ["OutOfPaper"] } };
        var check = await Check(alarm, DiagnosticIds.Paper);
        Assert.Equal(DiagnosticStatus.Warn, check.Status);
        Assert.Equal("OutOfPaper", check.Detail);

        var neverSeen = new Probes { Status = Healthy() with { Link = "Disconnected", LastSeen = null } };
        Assert.Equal(DiagnosticStatus.Unknown, (await Check(neverSeen, DiagnosticIds.Paper)).Status);
    }

    [Fact]
    public async Task The_windows_printer_is_ok_missing_or_being_recreated()
    {
        Assert.Equal(DiagnosticStatus.Ok, (await Check(new Probes(), DiagnosticIds.Queue)).Status);
        Assert.Equal(DiagnosticStatus.Fail, (await Check(new Probes { Queue = new(false, "X5h", "Idle", null, null) }, DiagnosticIds.Queue)).Status);
        Assert.Equal(DiagnosticStatus.Warn, (await Check(new Probes { Queue = new(false, "X5h", "Running", "Preparando…", "start") }, DiagnosticIds.Queue)).Status);
    }

    [Fact]
    public async Task The_ipp_listener_is_checked_by_connecting_to_it()
    {
        Assert.Equal(DiagnosticStatus.Fail, (await Check(new Probes { Ipp = _ => Task.FromResult(false) }, DiagnosticIds.Ipp)).Status);
    }

    [Fact]
    public async Task The_raw_port_off_is_not_a_problem()
    {
        var check = await Check(new Probes(), DiagnosticIds.Raw);
        Assert.Equal(DiagnosticStatus.Ok, check.Status);
        Assert.Equal("Desactivado", check.Detail);
    }

    [Theory]
    [InlineData(NetworkMode.Local, true, null, DiagnosticStatus.Ok)]
    [InlineData(NetworkMode.Lan, true, true, DiagnosticStatus.Ok)]
    [InlineData(NetworkMode.Lan, true, false, DiagnosticStatus.Warn)] // the rule of the firewall is missing
    [InlineData(NetworkMode.Lan, true, null, DiagnosticStatus.Unknown)] // the firewall could not be asked
    public async Task The_raw_port_on_needs_its_firewall_rule_on_the_local_network(NetworkMode mode, bool listening, bool? rule, string expected)
    {
        var probes = new Probes
        {
            Settings = new ServiceSettings { RawPortEnabled = true, NetworkMode = mode },
            Raw = new RawPortDto(true, 9100, listening, null, ["192.168.1.5:9100"]),
            Firewall = rule,
        };
        Assert.Equal(expected, (await Check(probes, DiagnosticIds.Raw)).Status);
    }

    [Fact]
    public async Task A_raw_port_that_could_not_open_fails_with_the_reason()
    {
        var probes = new Probes
        {
            Settings = new ServiceSettings { RawPortEnabled = true },
            Raw = new RawPortDto(true, 9100, false, "El puerto 9100 está en uso", []),
        };
        var check = await Check(probes, DiagnosticIds.Raw);
        Assert.Equal(DiagnosticStatus.Fail, check.Status);
        Assert.Equal("El puerto 9100 está en uso", check.Detail);
    }

    [Fact]
    public async Task Missing_fonts_are_named()
    {
        var check = await Check(new Probes { Fonts = new FontSupport(false, true) }, DiagnosticIds.Fonts);
        Assert.Equal(DiagnosticStatus.Warn, check.Status);
        Assert.Contains("japonés", check.Detail);
        Assert.DoesNotContain("tailandés", check.Detail);
    }

    [Fact]
    public async Task A_check_that_does_not_finish_is_unknown_and_does_not_hold_up_the_others()
    {
        var probes = new Probes { QueueProbe = async ct => { await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None); return new(true, "x", "Idle", null, null); } };
        var started = DateTime.UtcNow;
        var results = await probes.Build(TimeSpan.FromMilliseconds(200)).RunAsync(CancellationToken.None);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        Assert.Equal(DiagnosticStatus.Unknown, results.Single(r => r.Id == DiagnosticIds.Queue).Status);
        Assert.Equal(DiagnosticStatus.Ok, results.Single(r => r.Id == DiagnosticIds.Link).Status);
    }

    [Fact]
    public async Task A_check_that_throws_is_unknown_with_the_message()
    {
        var probes = new Probes { QueueProbe = _ => throw new InvalidOperationException("PowerShell no responde") };
        var check = await Check(probes, DiagnosticIds.Queue);
        Assert.Equal(DiagnosticStatus.Unknown, check.Status);
        Assert.Equal("PowerShell no responde", check.Detail);
    }
}

/// <summary>diagnostics-panel, raw-port-gui and template-gallery over a running service.</summary>
public sealed class DiagnosticsApiTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _dataDir = null!;
    private int _ippPort, _rawPort;

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
        _ = _factory.Server;
        await _factory.Services.GetRequiredService<RawPortHost>().WaitForCurrentAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        await TestEnv.WaitForPortAsync(_ippPort);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private ControlClient Client() =>
        new(File.ReadAllText(Path.Combine(_dataDir, "control.token")), handler: _factory.Server.CreateHandler());

    private async Task SendRawAsync(byte[] data)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _rawPort);
        var stream = tcp.GetStream();
        await stream.WriteAsync(data);
        tcp.Client.Shutdown(SocketShutdown.Send);
        await stream.ReadAsync(new byte[16]);
    }

    // ---- diagnostics -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_service_answers_with_its_checks()
    {
        using var client = Client();
        var checks = await client.GetDiagnosticsAsync();
        Assert.Contains(checks, c => c.Id == DiagnosticIds.Ipp && c.Status == DiagnosticStatus.Ok);
        Assert.Contains(checks, c => c.Id == DiagnosticIds.Raw && c.Status == DiagnosticStatus.Ok);
        Assert.Contains(checks, c => c.Id == DiagnosticIds.Queue);
        Assert.All(checks, c => Assert.Contains(c.Status, new[] { "ok", "warn", "fail", "unknown" }));
    }

    // ---- clients of the raw port ---------------------------------------------------------------------------------------

    [Fact]
    public void The_log_of_clients_keeps_only_the_latest_fifty_newest_first()
    {
        var log = new RawClientLog(RawPortHost.MaxRememberedClients);
        for (var i = 1; i <= 60; i++)
            log.Add(new RawClientDto(DateTimeOffset.UnixEpoch.AddSeconds(i), "10.0.0.1", "PNG", i, 0, i, RawClientResult.Queued));
        var all = log.Snapshot();
        Assert.Equal(50, all.Count);
        Assert.Equal(60, all[0].Bytes);
        Assert.Equal(11, all[^1].Bytes);
    }

    [Fact]
    public async Task A_ticket_is_listed_with_its_address_kind_and_result()
    {
        using var client = Client();
        await SendRawAsync([0x1B, (byte)'@', (byte)'H', (byte)'o', (byte)'l', (byte)'a', 0x0A, 0x1D, (byte)'V', 66, 0]);
        RawClientDto? entry = null;
        await TestEnv.WaitUntilAsync(async () =>
        {
            entry = (await client.GetRawClientsAsync()).FirstOrDefault();
            return entry is not null;
        });
        Assert.Equal("127.0.0.1", entry!.Address);
        Assert.Equal("ESC/POS", entry.Kind);
        Assert.Equal(1, entry.Tickets);
        Assert.Equal(RawClientResult.Queued, entry.Result);
        Assert.True(entry.Bytes > 0);
        Assert.NotNull(entry.JobId);
    }

    [Fact]
    public async Task Content_that_is_not_recognised_is_listed_as_rejected()
    {
        using var client = Client();
        await SendRawAsync(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 });
        RawClientDto? entry = null;
        await TestEnv.WaitUntilAsync(async () =>
        {
            entry = (await client.GetRawClientsAsync()).FirstOrDefault(e => e.Result == RawClientResult.Rejected);
            return entry is not null;
        });
        Assert.Equal("unknown", entry!.Kind);
        Assert.Null(entry.JobId);
    }

    [Fact]
    public async Task A_connection_that_sends_nothing_is_not_listed()
    {
        using var client = Client();
        using (var tcp = new TcpClient())
            await tcp.ConnectAsync(IPAddress.Loopback, _rawPort);
        await Task.Delay(300);
        Assert.Empty(await client.GetRawClientsAsync());
    }

    // ---- the test ticket -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_test_ticket_goes_through_the_port_and_becomes_a_raw_job()
    {
        using var client = Client();
        var job = await client.SendRawTestAsync();
        Assert.Equal("Raw", job.Source);
        Assert.Equal("127.0.0.1", job.Origin);
        await TestEnv.WaitUntilAsync(async () => (await client.GetJobsAsync()).Single(j => j.Id == job.Id).State == "Completed");
        Assert.Contains(await client.GetRawClientsAsync(), e => e.Kind == "ESC/POS" && e.JobId == job.Id);
    }

    [Fact]
    public async Task The_test_ticket_says_why_when_the_port_is_off()
    {
        using var client = Client();
        await client.SaveSettingsAsync((await client.GetSettingsAsync()) with { RawPortEnabled = false });
        var error = await Assert.ThrowsAsync<ControlApiException>(() => client.SendRawTestAsync());
        Assert.Equal(409, error.StatusCode);
        Assert.Contains("desactivada", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_test_ticket_is_valid_escpos_with_a_qr_and_a_visible_body(bool japanese)
    {
        var interpreter = new EscposInterpreter(() => null, _ => { });
        var bytes = RawPortTest.BuildTicket(japanese);
        interpreter.Feed(bytes);
        interpreter.EndTicket();
        var ticket = Assert.Single(interpreter.DrainTickets());
        Assert.True(ticket.Height > 300, $"the ticket is too short: {ticket.Height} rows");
        Assert.Contains(bytes.Length > 0 ? bytes : [], b => b == 0x1D); // has GS commands (QR, cut)
        var ink = Enumerable.Range(0, ticket.Height).Count(y => !ticket.IsRowBlank(y));
        Assert.True(ink > 100);
    }

    // ---- thumbnails ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_built_in_template_has_a_thumbnail_of_the_width_asked_for()
    {
        using var client = Client();
        var png = await client.GetTemplateThumbnailAsync("qr", 160);
        Assert.NotNull(png);
        Assert.Equal(0x89, png![0]);
        using var image = SixLabors.ImageSharp.Image.Load(png);
        Assert.Equal(160, image.Width);
        Assert.True(image.Height > 20);

        using var wide = SixLabors.ImageSharp.Image.Load((await client.GetTemplateThumbnailAsync("qr", 5000))!);
        Assert.Equal(TemplateThumbnails.MaxWidth, wide.Width);
        using var narrow = SixLabors.ImageSharp.Image.Load((await client.GetTemplateThumbnailAsync("qr", 5))!);
        Assert.Equal(TemplateThumbnails.MinWidth, narrow.Width);
    }

    [Fact]
    public async Task Every_built_in_template_can_be_drawn_as_a_thumbnail()
    {
        using var client = Client();
        foreach (var template in await client.GetTemplatesAsync())
            Assert.NotNull(await client.GetTemplateThumbnailAsync(template.Name));
    }

    [Fact]
    public async Task An_unknown_template_has_no_thumbnail()
    {
        using var client = Client();
        Assert.Null(await client.GetTemplateThumbnailAsync("no-existe"));
    }

    [Fact]
    public async Task A_thumbnail_is_kept_until_the_template_changes_and_forgotten_when_it_is_deleted()
    {
        using var client = Client();
        var thumbnails = _factory.Services.GetRequiredService<TemplateThumbnails>();
        const string v1 = """{ "name": "tarjeta", "title": "Tarjeta", "blocks": [ { "type": "text", "value": "Uno" } ] }""";
        const string v2 = """{ "name": "tarjeta", "title": "Tarjeta", "blocks": [ { "type": "text", "value": "Uno" }, { "type": "text", "value": "Dos" }, { "type": "text", "value": "Tres" } ] }""";
        await client.SaveTemplateAsync("tarjeta", v1);
        var first = await client.GetTemplateThumbnailAsync("tarjeta");
        Assert.Same(thumbnails.Get("tarjeta"), thumbnails.Get("tarjeta")); // cached

        await client.SaveTemplateAsync("tarjeta", v2);
        var second = await client.GetTemplateThumbnailAsync("tarjeta");
        Assert.NotEqual(first, second);

        await client.DeleteTemplateAsync("tarjeta");
        Assert.Null(await client.GetTemplateThumbnailAsync("tarjeta"));
    }

    [Fact]
    public async Task A_template_that_needs_an_image_still_gets_a_thumbnail()
    {
        using var client = Client();
        const string json = """{ "name": "logo", "title": "Con logo", "fields": [ { "name": "foto", "kind": "image", "required": true } ], "blocks": [ { "type": "image", "source": "{{foto}}" } ] }""";
        await client.SaveTemplateAsync("logo", json);
        Assert.NotNull(await client.GetTemplateThumbnailAsync("logo"));
    }

    [Fact]
    public async Task A_number_field_that_feeds_a_ranged_property_gets_an_example_inside_the_range()
    {
        // "Altura" must be 4–50 and the size of the text 6–48: an example of 1 would make the picture fail.
        using var client = Client();
        const string json = """
            { "name": "alto", "title": "Alto", "fields": [ { "name": "h", "kind": "number", "required": true }, { "name": "t", "kind": "number", "required": true } ],
              "blocks": [ { "type": "barcode", "data": "123456789012", "format": "code128", "height": "{{h}}" }, { "type": "text", "value": "Hola", "size": "{{t}}" } ] }
            """;
        await client.SaveTemplateAsync("alto", json);
        var png = await client.GetTemplateThumbnailAsync("alto");
        Assert.NotNull(png);
        using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.L8>(png!);
        var dark = 0;
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
            if (image[x, y].PackedValue < 128)
                dark++;
        Assert.True(dark > 200, "the thumbnail shows the real template, not the grey box of a failed drawing");
    }
}
