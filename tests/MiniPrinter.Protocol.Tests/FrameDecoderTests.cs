namespace MiniPrinter.Protocol.Tests;

public class FrameDecoderTests
{
    // Real replies captured from the X5h-E07A over SPP on 2026-10-01.
    private static readonly byte[] StateReply = Convert.FromHexString("5178A3010300000E280EFF");
    private static readonly byte[] InfoReply = Convert.FromHexString("5178A8010A00780003332E302E3544006FFF");
    private static readonly byte[] IdReply = Convert.FromHexString("5178BB01060000000000000000FF");

    [Fact]
    public void Decodes_real_X5h_replies()
    {
        var frames = new FrameDecoder().Feed([.. StateReply, .. InfoReply, .. IdReply]);
        Assert.Equal(3, frames.Count);

        var state = Assert.IsType<DeviceState>(PrinterMessage.Parse(frames[0]));
        Assert.Equal(Frame.FlagsFromPrinter, state.Frame.Flags);
        Assert.True(state.IsReady);
        Assert.Equal(PrinterAlarms.None, state.Alarms);
        Assert.Equal(0x0E, state.PaperSensor);
        Assert.Equal(0x28, state.BatteryLevel);

        var info = Assert.IsType<DeviceInfo>(PrinterMessage.Parse(frames[1]));
        Assert.Equal("3.0.5", info.Firmware);

        var id = Assert.IsType<DeviceId>(PrinterMessage.Parse(frames[2]));
        Assert.False(id.IsProgrammed);
    }

    [Fact]
    public void Handles_packets_split_across_reads_and_leading_garbage()
    {
        var decoder = new FrameDecoder();
        byte[] stream = [0x00, 0x13, 0x51, .. InfoReply];
        var frames = new List<Frame>();
        foreach (var b in stream)
            frames.AddRange(decoder.Feed([b]));
        var frame = Assert.Single(frames);
        Assert.Equal(Opcode.GetDeviceInfo, frame.Command);
    }

    [Fact]
    public void Rejects_bad_crc_and_resynchronises()
    {
        var corrupt = (byte[])StateReply.Clone();
        corrupt[^2] ^= 0xFF;
        var decoder = new FrameDecoder();
        var frame = Assert.Single(decoder.Feed([.. corrupt, .. IdReply]));
        Assert.Equal(Opcode.GetDeviceId, frame.Command);
        Assert.Equal(1, decoder.RejectedCount);
    }

    [Theory]
    [InlineData("5178AE0101001070FF", true)]
    [InlineData("5178AE0101000000FF", false)]
    public void Parses_flow_control(string hex, bool paused)
    {
        var frame = Assert.Single(new FrameDecoder().Feed(Convert.FromHexString(hex)));
        var flow = Assert.IsType<FlowControl>(PrinterMessage.Parse(frame));
        Assert.Equal(paused, flow.Paused);
    }

    [Fact]
    public void Out_of_paper_reply_from_the_X5h()
    {
        // Captured with the cover open / no paper: alarm 0x01, sensor 0x1B.
        var frame = Assert.Single(new FrameDecoder().Feed(Frame.Encode(Opcode.GetDeviceState, [0x01, 0x1B, 0x27], Frame.FlagsFromPrinter)));
        var state = Assert.IsType<DeviceState>(PrinterMessage.Parse(frame));
        Assert.False(state.IsReady);
        Assert.Equal(PrinterAlarms.OutOfPaper, state.Alarms);
        Assert.Equal(27, state.PaperSensor);
        Assert.Equal(39, state.BatteryLevel);
    }
}
