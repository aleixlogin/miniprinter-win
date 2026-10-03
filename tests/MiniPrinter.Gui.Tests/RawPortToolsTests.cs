using MiniPrinter.Control;
using ZXing;
using ZXing.Common;

namespace MiniPrinter.Gui.Tests;

/// <summary>raw-port-gui: addresses, copy, QR code, the test ticket and the recent clients.</summary>
public class RawPortToolsTests
{
    private sealed class FakeGateway : IRawPortGateway
    {
        public Exception? TestFailure { get; set; }
        public List<RawClientDto> Clients { get; set; } = [];
        public int Tests { get; private set; }

        public Task<JobDto> SendTestAsync()
        {
            Tests++;
            if (TestFailure is not null)
                return Task.FromException<JobDto>(TestFailure);
            return Task.FromResult(new JobDto(42, "Ticket ESC/POS", "raw@127.0.0.1", "Pending", null, DateTimeOffset.Now, null, 0, 0));
        }

        public Task<IReadOnlyList<RawClientDto>> GetClientsAsync() => Task.FromResult<IReadOnlyList<RawClientDto>>(Clients);
    }

    private static RawPortDto Listening(params string[] addresses) => new(true, 9100, true, null, addresses);

    private static RawPortToolsViewModel Create(out FakeGateway gateway)
    {
        gateway = new FakeGateway();
        return new RawPortToolsViewModel { Gateway = gateway };
    }

    // ---- addresses ----------------------------------------------------------------------------------------------------

    [Fact]
    public void On_this_pc_only_the_loopback_address_is_offered()
    {
        var tools = Create(out _);
        tools.Update(Listening("127.0.0.1:9100"), NetworkMode.Local);
        Assert.Equal(["127.0.0.1:9100"], tools.Addresses);
        Assert.Equal("127.0.0.1:9100", tools.BestAddress);
        Assert.True(tools.NeedsNetworkNotice);
    }

    [Fact]
    public void On_the_local_network_the_addresses_of_the_network_come_first_and_loopback_is_left_out()
    {
        var tools = Create(out _);
        tools.Update(Listening("127.0.0.1:9100", "mi-pc:9100", "192.168.1.5:9100", "10.0.0.7:9100"), NetworkMode.Lan);
        Assert.Equal(["mi-pc:9100", "192.168.1.5:9100", "10.0.0.7:9100"], tools.Addresses);
        Assert.True(tools.HasSeveralAddresses);
        Assert.Equal("mi-pc:9100", tools.BestAddress);
        Assert.False(tools.NeedsNetworkNotice);
    }

    [Fact]
    public void With_no_network_address_the_local_network_still_offers_something()
    {
        var tools = Create(out _);
        tools.Update(Listening("127.0.0.1:9100"), NetworkMode.Lan);
        Assert.Equal(["127.0.0.1:9100"], tools.Addresses);
    }

    [Theory]
    [InlineData(false, false, false)] // off
    [InlineData(true, false, false)] // enabled but not listening yet
    [InlineData(true, true, true)] // listening
    public void Nothing_is_offered_until_the_port_is_enabled_and_listening(bool enabled, bool listening, bool offered)
    {
        var tools = Create(out _);
        tools.Update(new RawPortDto(enabled, 9100, listening, null, ["127.0.0.1:9100"]), NetworkMode.Local);
        Assert.Equal(offered, tools.CanCopy);
        Assert.Equal(offered, tools.CanTest);
        Assert.Equal(offered, tools.CopyAddress.CanExecute(null));
        Assert.Equal(offered, tools.ShowQr.CanExecute(null));
        Assert.Equal(offered, tools.SendTest.CanExecute(null));
    }

    [Fact]
    public void Without_a_state_nothing_is_offered()
    {
        var tools = Create(out _);
        tools.Update(null, NetworkMode.Local);
        Assert.False(tools.CanCopy);
        Assert.Empty(tools.Addresses);
    }

    [Fact]
    public void Copy_and_the_qr_code_take_the_address_chosen_or_the_best_one()
    {
        var tools = Create(out _);
        tools.Update(Listening("mi-pc:9100", "192.168.1.5:9100"), NetworkMode.Lan);
        var copied = new List<string>();
        var shown = new List<string>();
        tools.CopyRequested += copied.Add;
        tools.QrRequested += shown.Add;
        tools.CopyAddress.Execute(null);
        tools.CopyAddress.Execute("192.168.1.5:9100");
        tools.ShowQr.Execute(null);
        tools.ShowQr.Execute("192.168.1.5:9100");
        Assert.Equal(["mi-pc:9100", "192.168.1.5:9100"], copied);
        Assert.Equal(["mi-pc:9100", "192.168.1.5:9100"], shown);
    }

    // ---- the test ticket -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_test_ticket_reports_the_job_and_refreshes_the_clients()
    {
        var tools = Create(out var gateway);
        tools.Update(Listening("127.0.0.1:9100"), NetworkMode.Local);
        gateway.Clients = [new RawClientDto(DateTimeOffset.Now, "127.0.0.1", "ESC/POS", 900, 1, 42, RawClientResult.Queued)];
        await tools.SendTest.ExecuteAsync();
        Assert.Equal(1, gateway.Tests);
        Assert.False(tools.TestFailed);
        Assert.Contains("trabajo 42", tools.TestResult);
        Assert.Single(tools.Clients);
    }

    [Fact]
    public async Task A_test_that_fails_says_why_and_keeps_going()
    {
        var tools = Create(out var gateway);
        tools.Update(Listening("127.0.0.1:9100"), NetworkMode.Local);
        gateway.TestFailure = new InvalidOperationException("El puerto 9100 está ocupado con otras conexiones.");
        await tools.SendTest.ExecuteAsync();
        Assert.True(tools.TestFailed);
        Assert.Equal("El puerto 9100 está ocupado con otras conexiones.", tools.TestResult);
        Assert.True(tools.SendTest.CanExecute(null));
    }

    // ---- clients -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(RawClientResult.Queued, "Aceptado", true)]
    [InlineData(RawClientResult.Rejected, "Rechazado: contenido no reconocido", false)]
    [InlineData(RawClientResult.ClosedByLimit, "Cortado por superar un límite", false)]
    [InlineData(RawClientResult.RefusedLimit, "Rechazado: demasiadas conexiones", false)]
    [InlineData(RawClientResult.Empty, "Sin nada que imprimir", false)]
    [InlineData(RawClientResult.Error, "Error de conexión", false)]
    [InlineData("nuevo", "nuevo", false)]
    public void Each_result_reads_in_the_interface_language(string result, string text, bool succeeded)
    {
        var row = RawClientRow.From(new RawClientDto(DateTimeOffset.Now, "192.168.0.30", "PNG", 2048, 0, 7, result));
        Assert.Equal(text, row.Result);
        Assert.Equal(succeeded, row.Succeeded);
        Assert.Equal("192.168.0.30", row.Address);
        Assert.Equal("trabajo 7", row.Detail);
    }

    [Theory]
    [InlineData(1, "1 ticket")]
    [InlineData(3, "3 tickets")]
    public void Tickets_are_counted(int tickets, string expected) =>
        Assert.Equal(expected, RawClientRow.From(new RawClientDto(DateTimeOffset.Now, "10.0.0.2", "ESC/POS", 10, tickets, 1, RawClientResult.Queued)).Detail);

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(5 * 1024 * 1024, "5 MB")]
    public void Sizes_are_short(long bytes, string expected) => Assert.Equal(expected, RawClientRow.FormatSize(bytes));

    [Fact]
    public async Task The_list_of_clients_is_replaced_on_every_refresh_and_a_failing_service_leaves_it_as_it_was()
    {
        var tools = Create(out var gateway);
        gateway.Clients =
        [
            new RawClientDto(DateTimeOffset.Now, "10.0.0.2", "PNG", 100, 0, 3, RawClientResult.Queued),
            new RawClientDto(DateTimeOffset.Now.AddMinutes(-1), "10.0.0.3", "PDF", 100, 0, 2, RawClientResult.Queued),
        ];
        await tools.Refresh.ExecuteAsync();
        Assert.Equal(2, tools.Clients.Count);
        Assert.True(tools.HasClients);

        gateway.Clients = [];
        await tools.Refresh.ExecuteAsync();
        Assert.Empty(tools.Clients);
        Assert.False(tools.HasClients);
    }

    [Fact]
    public void The_section_hands_the_state_of_the_port_to_its_tools()
    {
        var section = new RawPortSectionViewModel();
        section.ShowStatus(Listening("127.0.0.1:9100", "192.168.1.5:9100"), NetworkMode.Lan);
        Assert.Equal(["192.168.1.5:9100"], section.Tools.Addresses);
        section.ShowStatus(new RawPortDto(false, 9100, false, null, []));
        Assert.False(section.Tools.CanCopy);
    }

    // ---- QR code -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("192.168.1.5:9100")]
    [InlineData("mi-pc:9100")]
    public void The_qr_code_reads_back_as_the_address(string text)
    {
        var modules = QrCode.Modules(text);
        Assert.Equal(modules.GetLength(0), modules.GetLength(1));

        // Draw it like the window does (with its quiet zone) and read it with a QR reader.
        const int scale = 8, quiet = 4;
        var size = (modules.GetLength(0) + 2 * quiet) * scale;
        var pixels = new byte[size * size];
        Array.Fill(pixels, (byte)255);
        for (var y = 0; y < modules.GetLength(0); y++)
        for (var x = 0; x < modules.GetLength(1); x++)
            if (modules[y, x])
                for (var dy = 0; dy < scale; dy++)
                for (var dx = 0; dx < scale; dx++)
                    pixels[((y + quiet) * scale + dy) * size + (x + quiet) * scale + dx] = 0;
        var result = new ZXing.QrCode.QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(new PlanarYUVLuminanceSource(pixels, size, size, 0, 0, size, size, false))));
        Assert.Equal(text, result?.Text);
    }
}
