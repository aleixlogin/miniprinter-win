using MiniPrinter.Protocol.Catalog;

namespace MiniPrinter.Protocol.Tests;

public class RowEncoderTests
{
    private static byte[] Row(Func<int, bool> black, int width = 384) =>
        Enumerable.Range(0, width).Select(x => black(x) ? (byte)1 : (byte)0).ToArray();

    [Fact]
    public void Blank_row_is_a_single_white_run_split_at_127()
    {
        Assert.Equal("7F7F7F03", Convert.ToHexString(RowEncoder.EncodeRle(Row(_ => false))));
        Assert.Equal("5178BF0004007F7F7F03A8FF", Convert.ToHexString(RowEncoder.EncodeRow(Row(_ => false), RowEncoding.Rle)));
    }

    [Fact]
    public void Solid_black_row_uses_color_bit()
    {
        Assert.Equal("FFFFFF83", Convert.ToHexString(RowEncoder.EncodeRle(Row(_ => true))));
    }

    [Fact]
    public void Row_with_black_emits_trailing_white_run()
    {
        // 328 white, 1 black, 1 white, 1 black, 53 white (row taken from the GT01 capture).
        var row = Row(x => x is 328 or 330);
        Assert.Equal("7F7F4A81018135", Convert.ToHexString(RowEncoder.EncodeRle(row)));
    }

    [Fact]
    public void Packed_row_puts_leftmost_pixel_in_bit_0()
    {
        var packed = RowEncoder.Pack(Row(x => x is 0 or 7 or 8 or 383));
        Assert.Equal(48, packed.Length);
        Assert.Equal(0x81, packed[0]);
        Assert.Equal(0x01, packed[1]);
        Assert.Equal(0x80, packed[47]);
    }

    [Fact]
    public void Noisy_row_falls_back_to_A2()
    {
        var packet = RowEncoder.EncodeRow(Row(x => x % 2 == 1), RowEncoding.Rle);
        Assert.Equal(Opcode.RasterRow, packet[2]);
        Assert.Equal(Frame.Overhead + 48, packet.Length);
        Assert.Equal(0xAA, packet[Frame.HeaderLength]);
    }

    [Fact]
    public void Raw_encoding_never_uses_rle()
    {
        Assert.Equal(Opcode.RasterRow, RowEncoder.EncodeRow(Row(_ => false), RowEncoding.Raw)[2]);
    }
}
