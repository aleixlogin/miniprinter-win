using System.Diagnostics;
using MiniPrinter.Escpos;
using MiniPrinter.Protocol;

namespace MiniPrinter.Escpos.Tests;

/// <summary>
/// The RAW port has no authentication, so the interpreter must survive anything a client sends: no
/// exception, no hang, bounded memory. Fixed seeds keep failures reproducible.
/// </summary>
public class FuzzTests
{
    private static readonly byte[] Prefixes = [0x1B, 0x1D, 0x10, 0x1C, 0x0A, 0x09, 0x00];
    private static readonly byte[] Interesting = [0, 1, 2, 3, 8, 24, 32, 33, 48, 49, 50, 65, 66, 67, 73, 255, (byte)'*', (byte)'k', (byte)'v', (byte)'(', (byte)'!', (byte)'V'];

    private static byte[] Random(Random random, int length, double commandDensity)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            var roll = random.NextDouble();
            bytes[i] = roll < commandDensity ? Prefixes[random.Next(Prefixes.Length)]
                : roll < commandDensity * 3 ? Interesting[random.Next(Interesting.Length)]
                : (byte)random.Next(256);
        }
        return bytes;
    }

    /// <summary>Feeds in random chunks, ends the ticket and returns what came out; fails the test on a hang (a generous bound: CI runners are slow).</summary>
    private static IReadOnlyList<MonoBitmap> Interpret(byte[] data, Random random, out EscposInterpreter interpreter, int maxMs = 30_000)
    {
        interpreter = new EscposInterpreter(() => new PrinterCondition(PaperOut: true));
        var tickets = new List<MonoBitmap>();
        var watch = Stopwatch.StartNew();
        try
        {
            for (var i = 0; i < data.Length;)
            {
                var chunk = Math.Min(data.Length - i, random.Next(1, 600));
                interpreter.Feed(data.AsSpan(i, chunk));
                interpreter.DrainResponses();
                tickets.AddRange(interpreter.DrainTickets());
                i += chunk;
            }
            interpreter.EndTicket();
            tickets.AddRange(interpreter.DrainTickets());
        }
        catch (Exception ex)
        {
            // Say which stream broke it, so that a failure on another machine can be reproduced.
            throw new Xunit.Sdk.XunitException($"{ex.GetType().Name}: {ex.Message}\nstream ({data.Length} bytes): {Convert.ToHexString(data)}\n{ex.StackTrace}", ex);
        }
        Assert.True(watch.ElapsedMilliseconds < maxMs, $"took {watch.ElapsedMilliseconds} ms for {data.Length} bytes");
        return tickets;
    }

    private static void AssertSane(IReadOnlyList<MonoBitmap> tickets)
    {
        Assert.All(tickets, t =>
        {
            Assert.Equal(384, t.Width);
            // A ticket closes when it reaches MaxTicketRows, so it can end up one block (an image of up to MaxTicketRows) longer.
            Assert.InRange(t.Height, 1, 2 * EscposInterpreter.MaxTicketRows + 1000);
        });
        Assert.True(tickets.Sum(t => (long)t.Height) <= EscposInterpreter.MaxTotalRows + 2 * EscposInterpreter.MaxTicketRows + 1000);
    }

    [Theory]
    [InlineData(1, 0.02)]
    [InlineData(2, 0.10)]
    [InlineData(3, 0.30)]
    [InlineData(4, 0.00)]
    public void Random_streams_never_throw_or_hang(int seed, double density)
    {
        var random = new Random(seed);
        for (var i = 0; i < 150; i++)
        {
            var tickets = Interpret(Random(random, random.Next(1, 3000), density), random, out _);
            AssertSane(tickets);
        }
    }

    [Fact]
    public void Truncated_and_mutated_real_fixtures_never_throw()
    {
        var random = new Random(42);
        var fixtures = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(Source())!, "Fixtures"), "*.bin").Select(File.ReadAllBytes).ToList();
        Assert.NotEmpty(fixtures);
        foreach (var original in fixtures)
        for (var i = 0; i < 60; i++)
        {
            var data = original.ToArray();
            data = random.Next(3) switch
            {
                0 => data[..random.Next(data.Length + 1)],                          // cut anywhere
                1 => Mutate(data, random, random.Next(1, 12)),                       // flip bytes
                _ => Mutate(data[..random.Next(data.Length + 1)], random, 3),
            };
            AssertSane(Interpret(data, random, out _));
        }
    }

    private static string Source([System.Runtime.CompilerServices.CallerFilePath] string? self = null) => self!;

    private static byte[] Mutate(byte[] data, Random random, int count)
    {
        for (var i = 0; i < count && data.Length > 0; i++)
            data[random.Next(data.Length)] = (byte)random.Next(256);
        return data;
    }

    [Fact]
    public void A_raster_image_declaring_billions_of_bytes_waits_without_overflowing_or_allocating()
    {
        // GS v 0 m xL=255 xH=255 yL=255 yH=255: 65535 × 65535 bytes. Used to overflow the int length computation.
        var interpreter = new EscposInterpreter();
        interpreter.Feed([0x1D, (byte)'v', (byte)'0', 0, 0xFF, 0xFF, 0xFF, 0xFF]);
        interpreter.Feed(new byte[100_000]);
        interpreter.Feed([(byte)'A', 0x0A]);                    // swallowed as image data: the command is still incomplete
        interpreter.EndTicket();
        Assert.Empty(interpreter.DrainTickets());
    }

    [Fact]
    public void A_wide_image_is_cropped_while_it_is_read_not_after()
    {
        // 4000 dots wide (500 bytes) × 2000 rows: only 384 columns are kept.
        var data = new List<byte> { 0x1D, (byte)'v', (byte)'0', 0, 0xF4, 0x01, 0xD0, 0x07 };
        data.AddRange(Enumerable.Repeat((byte)0xAA, 500 * 2000));
        var tickets = Esc.Run(data.ToArray());
        var ticket = Assert.Single(tickets);
        Assert.Equal((384, 2000), (ticket.Width, ticket.Height));
    }

    [Fact]
    public void Millions_of_feeds_cost_no_memory_and_little_time()
    {
        var watch = Stopwatch.StartNew();
        var interpreter = new EscposInterpreter();
        var chunk = new byte[16 * 1024];
        for (var i = 0; i < chunk.Length; i += 3)
            (chunk[i], chunk[i + 1 < chunk.Length ? i + 1 : i], chunk[i + 2 < chunk.Length ? i + 2 : i]) = (0x1B, (byte)'d', 255);
        for (var i = 0; i < 1000; i++)                          // 16 MB of ESC d 255
        {
            interpreter.Feed(chunk);
            Assert.Empty(interpreter.DrainTickets());           // blank paper is never a ticket
        }
        interpreter.EndTicket();
        Assert.Empty(interpreter.DrainTickets());
        Assert.True(watch.ElapsedMilliseconds < 20_000, $"took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Huge_characters_in_bulk_stop_at_the_row_budget()
    {
        // GS ! 0x77 (8×8) then 3 MB of text: every character is 96 × 192 dots.
        var watch = Stopwatch.StartNew();
        var interpreter = new EscposInterpreter();
        interpreter.Feed([0x1D, (byte)'!', 0x77]);
        var text = new byte[64 * 1024];
        Array.Fill(text, (byte)'W');
        var rows = 0L;
        for (var i = 0; i < 48; i++)
        {
            interpreter.Feed(text);
            foreach (var ticket in interpreter.DrainTickets())
                rows += ticket.Height;
        }
        interpreter.EndTicket();
        foreach (var ticket in interpreter.DrainTickets())
            rows += ticket.Height;
        Assert.True(interpreter.Overflowed);
        Assert.True(rows <= EscposInterpreter.MaxTotalRows + 2 * EscposInterpreter.MaxTicketRows + 1000);
        Assert.True(watch.ElapsedMilliseconds < 30_000, $"took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void After_the_budget_the_interpreter_ignores_everything_but_status_requests()
    {
        var interpreter = new EscposInterpreter();
        interpreter.Feed([0x1D, (byte)'!', 0x77]);
        var text = new byte[64 * 1024];
        Array.Fill(text, (byte)(char)87);
        text[^1] = 0x0A;                                  // the line feed makes the run render
        for (var i = 0; i < 48 && !interpreter.Overflowed; i++)
            interpreter.Feed(text);
        Assert.True(interpreter.Overflowed);
        interpreter.DrainTickets();
        interpreter.Feed([0x10, 0x04, 1]);
        Assert.Equal([(byte)0x12], interpreter.DrainResponses());
        interpreter.Feed([(byte)'A', 0x0A, 0x1D, (byte)'V', 66, 0]);
        interpreter.EndTicket();
        Assert.Empty(interpreter.DrainTickets());
    }
}
