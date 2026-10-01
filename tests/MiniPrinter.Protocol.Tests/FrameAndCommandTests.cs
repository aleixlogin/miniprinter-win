namespace MiniPrinter.Protocol.Tests;

public class FrameAndCommandTests
{
    [Theory]
    [InlineData("E02E", 0x89)]
    [InlineData("3000", 0xF9)]
    [InlineData("33", 0x99)]
    [InlineData("000E28", 0x0E)]                 // real X5h A3 reply
    [InlineData("780003332E302E354400", 0x6F)]   // real X5h A8 reply
    [InlineData("", 0x00)]
    public void Crc8_matches_captured_vectors(string payloadHex, int expected)
    {
        Assert.Equal((byte)expected, Crc8.Compute(Convert.FromHexString(payloadHex)));
    }

    [Fact]
    public void Command_packets_match_reference_bytes()
    {
        Assert.Equal("5178A40001003399FF", Hex(Commands.Darkness(3)));
        Assert.Equal("5178A4000100358BFF", Hex(Commands.Darkness(5)));
        Assert.Equal("5178AF000200881367FF", Hex(Commands.Energy(5000)));
        Assert.Equal("5178AF000200401F06FF", Hex(Commands.Energy(8000)));
        Assert.Equal("5178AF000200E02E89FF", Hex(Commands.Energy(12000)));
        Assert.Empty(Commands.Energy(0));
        Assert.Equal("5178BE0001000000FF", Hex(Commands.PrintMode(false)));
        Assert.Equal("5178BE0001000107FF", Hex(Commands.PrintMode(true)));
        Assert.Equal("5178BD0001000A36FF", Hex(Commands.Speed(10)));
        Assert.Equal("5178BD0001000C24FF", Hex(Commands.Speed(12)));
        Assert.Equal("5178A10002003000F9FF", Hex(Commands.FeedPaper(48)));
        Assert.Equal("5178A10002003000F9FF", Hex(Commands.PaperStep(200)));
        Assert.Equal("5178A10002004800F3FF", Hex(Commands.PaperStep(300)));
        Assert.Equal("5178A30001000000FF", Hex(Commands.GetDeviceState()));
        Assert.Equal("5178A80001000000FF", Hex(Commands.GetDeviceInfo()));
        Assert.Equal("5178BB0001000107FF", Hex(Commands.GetDeviceId()));
        Assert.Equal("5178A00002003000F9FF", Hex(Commands.RetractPaper(48)));
    }

    [Fact]
    public void Darkness_is_clamped_to_1_5()
    {
        Assert.Equal(Hex(Commands.Darkness(1)), Hex(Commands.Darkness(-4)));
        Assert.Equal(Hex(Commands.Darkness(5)), Hex(Commands.Darkness(99)));
    }

    [Fact]
    public void Frame_header_carries_flags_and_little_endian_length()
    {
        var packet = Frame.Encode(0x42, new byte[300], Frame.FlagsFromPrinter);
        Assert.Equal(0x01, packet[3]);
        Assert.Equal(300 & 0xFF, packet[4]);
        Assert.Equal(300 >> 8, packet[5]);
        Assert.Equal(Frame.Overhead + 300, packet.Length);
        Assert.Equal(0xFF, packet[^1]);
    }

    internal static string Hex(byte[] bytes) => Convert.ToHexString(bytes);
}
