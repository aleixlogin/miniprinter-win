using MiniPrinter.Protocol.Catalog;

namespace MiniPrinter.Protocol;

/// <summary>Per-job print options.</summary>
public sealed record PrintOptions
{
    /// <summary>Darkness 1..5; also selects the energy band of the profile.</summary>
    public int Darkness { get; init; } = 3;

    /// <summary>Text mode uses the profile's text energy/speed and tells the printer it is text.</summary>
    public bool IsText { get; init; }

    /// <summary>Feed padding sent with <c>0xBD</c> around the end-of-page paper steps.</summary>
    public int FeedPadding { get; init; } = 12;

    /// <summary>Overrides the profile's number of end-of-page paper steps.</summary>
    public int? PostPrintFeedCount { get; init; }

    /// <summary>Overrides the profile's energy for the chosen darkness.</summary>
    public int? Energy { get; init; }
}

/// <summary>
/// Builds the byte stream for a print job using the "tiny" recipe (default packet variant),
/// ported from TiMini-Print:
/// <code>
/// A4 darkness · AF energy · BE mode · BD speed
///   rows (BF or A2), plus BD speed after every 200 rows
/// BD padding · A1 step × N · BD padding · A3 00     (only when the page ends)
/// </code>
/// </summary>
public sealed class PrintJobBuilder
{
    public const int SpeedRefreshInterval = 200;

    private readonly PrinterProfile _profile;

    public PrintJobBuilder(PrinterProfile profile)
    {
        _profile = profile;
    }

    /// <summary>
    /// Builds one page. Pass <paramref name="endsPage"/> = false for intermediate chunks of a
    /// continuous document so the end-of-page paper steps are only emitted once.
    /// </summary>
    public byte[] BuildPage(MonoBitmap page, PrintOptions? options = null, bool endsPage = true)
    {
        options ??= new PrintOptions();
        if (page.Width != _profile.WidthPx)
            throw new ArgumentException($"Page width {page.Width} does not match the profile width {_profile.WidthPx}.", nameof(page));

        var speed = _profile.Speed.Select(options.IsText);
        var energy = options.Energy ?? _profile.Energy.Select(options.IsText, options.Darkness);

        using var output = new MemoryStream(page.Height * (_profile.WidthBytes + Frame.Overhead) + 64);
        Write(output, Commands.Darkness(options.Darkness));
        Write(output, Commands.Energy(energy));
        Write(output, Commands.PrintMode(options.IsText));
        Write(output, Commands.Speed(speed));

        for (var y = 0; y < page.Height; y++)
        {
            Write(output, RowEncoder.EncodeRow(page.Row(y), _profile.Encoding));
            if ((y + 1) % SpeedRefreshInterval == 0)
                Write(output, Commands.Speed(speed));
        }

        if (endsPage)
            Write(output, BuildPageEnd(options));
        return output.ToArray();
    }

    /// <summary>Builds several pages back to back, each one self-contained.</summary>
    public byte[] BuildDocument(IEnumerable<MonoBitmap> pages, PrintOptions? options = null)
    {
        using var output = new MemoryStream();
        foreach (var page in pages)
            Write(output, BuildPage(page, options));
        return output.ToArray();
    }

    /// <summary>End-of-page sequence; also used to close a cancelled job cleanly.</summary>
    public byte[] BuildPageEnd(PrintOptions? options = null)
    {
        options ??= new PrintOptions();
        using var output = new MemoryStream();
        Write(output, Commands.Speed(options.FeedPadding));
        var steps = Math.Max(0, options.PostPrintFeedCount ?? _profile.PostPrintFeedCount);
        for (var i = 0; i < steps; i++)
            Write(output, Commands.PaperStep(_profile.Dpi));
        Write(output, Commands.Speed(options.FeedPadding));
        Write(output, Commands.GetDeviceState());
        return output.ToArray();
    }

    private static void Write(Stream stream, byte[] bytes) => stream.Write(bytes);
}
