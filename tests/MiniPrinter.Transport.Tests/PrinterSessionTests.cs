namespace MiniPrinter.Transport.Tests;

public class PrinterSessionTests
{
    private static SessionOptions Fast(TimeSpan? idle = null, TimeSpan? poll = null) => new()
    {
        IdleTimeout = idle ?? TimeSpan.FromSeconds(30),
        PollInterval = poll ?? TimeSpan.FromSeconds(30),
        QueryTimeout = TimeSpan.FromMilliseconds(500),
        RetryBaseDelay = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public async Task Connects_on_demand_and_reads_state_and_firmware()
    {
        var fake = new FakePrinterTransport();
        await using var session = new PrinterSession(() => fake, "sim", Fast());
        Assert.Equal(LinkState.Disconnected, session.Status.Link);

        var status = await session.RefreshAsync(CancellationToken.None);

        Assert.Equal(LinkState.Connected, status.Link);
        Assert.True(status.State!.IsReady);
        Assert.Equal("3.0.5", status.Firmware);
        Assert.Equal(1, fake.ConnectCount);
    }

    [Fact]
    public async Task Retries_unavailable_printer_then_reports_error()
    {
        var attempts = 0;
        await using var session = new PrinterSession(() =>
        {
            attempts++;
            return new FakePrinterTransport { ConnectError = new TransportException(TransportErrorKind.Unavailable, "off") };
        }, "sim", Fast());

        await Assert.ThrowsAsync<TransportException>(() => session.RefreshAsync(CancellationToken.None));
        Assert.Equal(3, attempts);
        Assert.Equal(LinkState.Error, session.Status.Link);
        Assert.Equal(TransportErrorKind.Unavailable, session.Status.LastErrorKind);
    }

    [Fact]
    public async Task Busy_printer_is_not_retried()
    {
        var attempts = 0;
        await using var session = new PrinterSession(() =>
        {
            attempts++;
            return new FakePrinterTransport { ConnectError = new TransportException(TransportErrorKind.Busy, "in use") };
        }, "sim", Fast());

        await Assert.ThrowsAsync<TransportException>(() => session.RefreshAsync(CancellationToken.None));
        Assert.Equal(1, attempts);
        Assert.Equal(TransportErrorKind.Busy, session.Status.LastErrorKind);
    }

    [Fact]
    public async Task Disconnects_after_idle_timeout_and_reconnects_on_next_use()
    {
        var fake = new FakePrinterTransport();
        await using var session = new PrinterSession(() => fake, "sim", Fast(idle: TimeSpan.FromMilliseconds(200)));
        await session.RefreshAsync(CancellationToken.None);

        await WaitUntil(() => session.Status.Link == LinkState.Disconnected);
        Assert.False(fake.IsConnected);

        await session.RefreshAsync(CancellationToken.None);
        Assert.Equal(2, fake.ConnectCount);
    }

    [Fact]
    public async Task Polls_state_while_connected()
    {
        var fake = new FakePrinterTransport();
        await using var session = new PrinterSession(() => fake, "sim",
            Fast(idle: TimeSpan.FromSeconds(10), poll: TimeSpan.FromMilliseconds(150)));
        await session.RefreshAsync(CancellationToken.None);

        fake.AlarmByte = 0x01;
        await WaitUntil(() => session.Status.State is { IsReady: false });
        Assert.Equal(MiniPrinter.Protocol.PrinterAlarms.OutOfPaper, session.Status.State!.Alarms);
    }

    [Fact]
    public async Task Keep_alive_prevents_idle_disconnect_and_polls()
    {
        var fake = new FakePrinterTransport();
        await using var session = new PrinterSession(() => fake, "sim", Fast(idle: TimeSpan.FromMilliseconds(200)));
        await session.RefreshAsync(CancellationToken.None);
        var polls = 0;
        session.MessageReceived += m => { if (m is MiniPrinter.Protocol.DeviceState) Interlocked.Increment(ref polls); };

        session.KeepAlive(DateTimeOffset.UtcNow.AddSeconds(2), TimeSpan.FromMilliseconds(150));
        await Task.Delay(1200);
        Assert.Equal(LinkState.Connected, session.Status.Link);
        Assert.True(polls >= 3, $"only {polls} polls");

        // After the keep-alive period, the idle timeout applies again.
        await WaitUntil(() => session.Status.Link == LinkState.Disconnected);
        Assert.Null(session.Status.KeepAliveUntil);
    }

    [Fact]
    public async Task Keep_alive_connects_a_disconnected_session()
    {
        var fake = new FakePrinterTransport();
        await using var session = new PrinterSession(() => fake, "sim", Fast());
        session.KeepAlive(DateTimeOffset.UtcNow.AddSeconds(3), TimeSpan.FromMilliseconds(100));
        await WaitUntil(() => session.Status.Link == LinkState.Connected);
        Assert.Equal(1, fake.ConnectCount);
    }

    [Fact]
    public async Task Use_is_exclusive()
    {
        var fake = new FakePrinterTransport();
        await using var session = new PrinterSession(() => fake, "sim", Fast());
        var inside = 0;
        var maxInside = 0;
        var tasks = Enumerable.Range(0, 5).Select(_ => session.UseAsync(async (c, ct) =>
        {
            var now = Interlocked.Increment(ref inside);
            maxInside = Math.Max(maxInside, now);
            await Task.Delay(20, ct);
            Interlocked.Decrement(ref inside);
        }, CancellationToken.None));
        await Task.WhenAll(tasks);
        Assert.Equal(1, maxInside);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("condition not reached");
            await Task.Delay(20);
        }
    }
}
