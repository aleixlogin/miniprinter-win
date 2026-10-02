using MiniPrinter.Protocol;

namespace MiniPrinter.Transport.Tests;

public class PersistentSessionTests
{
    private static SessionOptions Options(bool persistent = true) => new()
    {
        IdleTimeout = TimeSpan.FromMilliseconds(200),
        PollInterval = TimeSpan.FromSeconds(30),
        QueryTimeout = TimeSpan.FromMilliseconds(500),
        RetryBaseDelay = TimeSpan.FromMilliseconds(10),
        ConnectAttempts = 1,
        Persistent = persistent,
        HeartbeatInterval = TimeSpan.FromMilliseconds(150),
        ReconnectBaseDelay = TimeSpan.FromMilliseconds(100),
        ReconnectMaxDelay = TimeSpan.FromMilliseconds(400),
    };

    private static int Heartbeats(FakePrinterTransport fake) =>
        new FrameDecoder().Feed(fake.AllWrittenBytes).Count(f => f.Command == Opcode.GetDeviceState);

    private static async Task WaitUntil(Func<bool> condition, int seconds = 5)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("condition not reached");
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Connects_by_itself_and_never_idle_disconnects()
    {
        var fake = new FakePrinterTransport();
        await using var session = new PrinterSession(() => fake, "sim", Options());

        await WaitUntil(() => session.Status.Link == LinkState.Connected);
        await Task.Delay(1000); // five times the idle timeout
        Assert.Equal(LinkState.Connected, session.Status.Link);
        Assert.True(session.Status.Persistent);
        Assert.True(Heartbeats(fake) >= 4, $"only {Heartbeats(fake)} heartbeats");
    }

    [Fact]
    public async Task Heartbeat_never_interleaves_with_a_job()
    {
        var fake = new FakePrinterTransport();
        await using var session = new PrinterSession(() => fake, "sim", Options());
        await WaitUntil(() => session.Status.Link == LinkState.Connected);

        var before = 0;
        var after = 0;
        await session.UseAsync(async (connection, ct) =>
        {
            before = Heartbeats(fake);
            await Task.Delay(700, ct); // several heartbeat intervals while the job holds the link
            after = Heartbeats(fake);
        }, CancellationToken.None);

        Assert.Equal(before, after);
        await WaitUntil(() => Heartbeats(fake) > after); // resumes once the job is done
    }

    [Fact]
    public async Task Reconnects_after_the_link_drops_with_growing_delay()
    {
        var fake = new FakePrinterTransport();
        await using var session = new PrinterSession(() => fake, "sim", Options());
        await WaitUntil(() => session.Status.Link == LinkState.Connected);

        fake.ConnectError = new TransportException(TransportErrorKind.Unavailable, "printer off");
        fake.SimulateDrop();
        await WaitUntil(() => session.Status.Reconnecting);
        Assert.Equal(LinkState.Connecting, session.Status.Link);
        Assert.Equal(TransportErrorKind.Unavailable, session.Status.LastErrorKind);

        // The printer comes back: the session reconnects without anyone printing.
        fake.ConnectError = null;
        await WaitUntil(() => session.Status.Link == LinkState.Connected);
        Assert.False(session.Status.Reconnecting);
        Assert.True(fake.ConnectCount >= 2);
    }

    [Fact]
    public async Task Turning_it_off_during_reconnection_stops_the_attempts()
    {
        var fake = new FakePrinterTransport { ConnectError = new TransportException(TransportErrorKind.Busy, "in use") };
        var attempts = 0;
        await using var session = new PrinterSession(() => { attempts++; return fake; }, "sim", Options());
        await WaitUntil(() => session.Status.Reconnecting);

        session.SetPersistent(false);
        var stopped = attempts;
        await Task.Delay(800);
        Assert.Equal(stopped, attempts);
        Assert.False(session.Status.Persistent);
        Assert.False(session.Status.Reconnecting);
    }

    [Fact]
    public async Task On_demand_mode_still_idle_disconnects_when_turned_off()
    {
        var fake = new FakePrinterTransport();
        await using var session = new PrinterSession(() => fake, "sim", Options());
        await WaitUntil(() => session.Status.Link == LinkState.Connected);

        session.SetPersistent(false);
        await WaitUntil(() => session.Status.Link == LinkState.Disconnected);
    }

    [Fact]
    public async Task Battery_sampling_is_independent()
    {
        var fake = new FakePrinterTransport();
        await using var session = new PrinterSession(() => fake, "sim", Options());
        await WaitUntil(() => session.Status.Link == LinkState.Connected);

        session.KeepAlive(DateTimeOffset.UtcNow.AddMilliseconds(400), TimeSpan.FromMilliseconds(100));
        Assert.NotNull(session.Status.KeepAliveUntil);
        await WaitUntil(() => session.Status.KeepAliveUntil is null);

        await Task.Delay(500); // longer than the idle timeout
        Assert.Equal(LinkState.Connected, session.Status.Link);
        Assert.True(session.Status.Persistent);
    }
}
