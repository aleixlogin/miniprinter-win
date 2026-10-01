using MiniPrinter.Protocol;

namespace MiniPrinter.Transport.Tests;

public class PrinterConnectionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task Send_splits_into_chunks_that_reassemble_in_order()
    {
        var fake = new FakePrinterTransport();
        await using var connection = new PrinterConnection(fake);
        await connection.ConnectAsync(CancellationToken.None);

        var data = Enumerable.Range(0, 500).Select(i => (byte)i).ToArray();
        await connection.SendAsync(data, new StreamSettings(ChunkSize: 180, DelayMs: 0), CancellationToken.None);

        Assert.Equal([180, 180, 140], fake.Writes.Select(w => w.Length));
        Assert.Equal(data, fake.AllWrittenBytes);
    }

    [Fact]
    public async Task Queries_return_decoded_replies()
    {
        var fake = new FakePrinterTransport();
        await using var connection = new PrinterConnection(fake);
        await connection.ConnectAsync(CancellationToken.None);

        var state = await connection.GetStateAsync(Timeout, CancellationToken.None);
        var info = await connection.GetInfoAsync(Timeout, CancellationToken.None);
        var id = await connection.GetIdAsync(Timeout, CancellationToken.None);

        Assert.True(state.IsReady);
        Assert.Equal(0x28, state.BatteryLevel);
        Assert.Equal("3.0.5", info.Firmware);
        Assert.False(id.IsProgrammed);
    }

    [Fact]
    public async Task Query_times_out_when_printer_is_silent()
    {
        var fake = new FakePrinterTransport { AnswerQueries = false };
        await using var connection = new PrinterConnection(fake);
        await connection.ConnectAsync(CancellationToken.None);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            connection.GetStateAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None));
    }

    [Fact]
    public async Task Pause_blocks_writes_until_resume_without_losing_data()
    {
        var fake = new FakePrinterTransport();
        var resumeAfterPause = new TaskCompletionSource();
        fake.OnWrite = (f, written) =>
        {
            if (f.Writes.Count == 2)
            {
                f.InjectPause();
                _ = Task.Run(async () =>
                {
                    await Task.Delay(300);
                    resumeAfterPause.SetResult();
                    f.InjectResume();
                });
            }
        };
        await using var connection = new PrinterConnection(fake);
        await connection.ConnectAsync(CancellationToken.None);
        var paused = new TaskCompletionSource();
        connection.MessageReceived += m => { if (m is FlowControl { Paused: true }) paused.TrySetResult(); };

        var data = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();
        // A small delay between chunks gives the pause notification time to arrive.
        var send = connection.SendAsync(data, new StreamSettings(100, DelayMs: 20), CancellationToken.None);

        await paused.Task.WaitAsync(Timeout);
        var writesWhilePaused = fake.Writes.Count;
        Assert.False(resumeAfterPause.Task.IsCompleted);
        await Task.Delay(150);
        Assert.Equal(writesWhilePaused, fake.Writes.Count);

        await send.WaitAsync(Timeout);
        Assert.Equal(data, fake.AllWrittenBytes);
    }

    [Fact]
    public async Task Pause_without_resume_fails_after_timeout()
    {
        var fake = new FakePrinterTransport();
        fake.OnWrite = (f, written) => { if (f.Writes.Count == 1) f.InjectPause(); };
        await using var connection = new PrinterConnection(fake);
        await connection.ConnectAsync(CancellationToken.None);

        var settings = new StreamSettings(10, DelayMs: 20) { PauseTimeout = TimeSpan.FromMilliseconds(200) };
        var error = await Assert.ThrowsAsync<TransportException>(() =>
            connection.SendAsync(new byte[100], settings, CancellationToken.None));
        Assert.Equal(TransportErrorKind.Unavailable, error.Kind);
    }

    [Fact]
    public async Task Cancellation_stops_between_chunks()
    {
        var fake = new FakePrinterTransport();
        using var cts = new CancellationTokenSource();
        fake.OnWrite = (f, written) => { if (f.Writes.Count == 3) cts.Cancel(); };
        await using var connection = new PrinterConnection(fake);
        await connection.ConnectAsync(CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            connection.SendAsync(new byte[1000], new StreamSettings(100, 0), cts.Token));
        Assert.Equal(3, fake.Writes.Count);
    }
}
