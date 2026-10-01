using MiniPrinter.Protocol;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MiniPrinter.Imaging.Tests;

public class PwgRasterTests
{
    private static GrayImage Gradient(int width, int height)
    {
        var image = new GrayImage(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            image[x, y] = (byte)(x * 255 / (width - 1));
        return image;
    }

    [Fact]
    public void Round_trips_multipage_sgray()
    {
        var page1 = Gradient(300, 40);
        var page2 = new GrayImage(300, 10); // all white: exercises long runs
        page2[150, 5] = 0;

        using var stream = new MemoryStream();
        PwgRasterWriter.Write(stream, [page1, page2], dpi: 203);
        stream.Position = 0;

        var pages = new PwgRasterReader(stream).ReadPages().ToList();
        Assert.Equal(2, pages.Count);
        Assert.Equal(page1.Pixels, pages[0].Pixels);
        Assert.Equal(page2.Pixels, pages[1].Pixels);
        Assert.Equal(203, pages[0].Dpi);
    }

    [Fact]
    public void Decodes_line_repeat_and_fill_to_end_of_line()
    {
        using var stream = new MemoryStream();
        stream.Write("RaS2"u8);
        var header = new byte[PwgPageHeader.Size];
        void U32(int offset, int value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(offset), (uint)value);
        U32(276, 203); U32(372, 8); U32(376, 3); U32(384, 8); U32(388, 8); U32(392, 8); U32(400, PwgPageHeader.ColorSpaceSGray);
        stream.Write(header);
        // Line 1 repeated twice: 2 black pixels then "rest is white" (0x80).
        stream.Write([1, 1, 0x00, 0x80]);
        // Line 3: literal of 8 pixels.
        stream.Write([0, 257 - 8, 10, 20, 30, 40, 50, 60, 70, 80]);
        stream.Position = 0;

        var page = new PwgRasterReader(stream).ReadPage()!;
        Assert.Equal(new byte[] { 0, 0, 255, 255, 255, 255, 255, 255 }, page.Row(0).ToArray());
        Assert.Equal(page.Row(0).ToArray(), page.Row(1).ToArray());
        Assert.Equal(new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 }, page.Row(2).ToArray());
    }

    [Fact]
    public void Rejects_non_pwg_data()
    {
        using var stream = new MemoryStream("%PDF-1.7"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => new PwgRasterReader(stream).ReadPage());
    }
}

public class DecoderTests
{
    [Fact]
    public void Decodes_png_and_sniffs_pwg()
    {
        using var png = new MemoryStream();
        using (var image = new Image<L8>(20, 10, new L8(255)))
        {
            image[3, 4] = new L8(0);
            image.SaveAsPng(png);
        }
        png.Position = 0;
        var page = Assert.Single(ImageDecoder.Decode(png, "application/octet-stream"));
        Assert.Equal(20, page.Width);
        Assert.Equal(0, page[3, 4]);

        using var pwg = new MemoryStream();
        PwgRasterWriter.Write(pwg, [new GrayImage(16, 2)]);
        pwg.Position = 0;
        Assert.Single(ImageDecoder.Decode(pwg, null));
    }

    [Fact]
    public void Unknown_format_is_not_supported()
    {
        using var pdf = new MemoryStream("%PDF-1.7 nonsense"u8.ToArray());
        Assert.Throws<NotSupportedException>(() => ImageDecoder.Decode(pdf, "application/pdf").ToList());
    }
}

public class RasterizerTests
{
    [Fact]
    public void Scales_proportionally_to_head_width()
    {
        var scaled = Rasterizer.Scale(new GrayImage(812, 1200), 384);
        Assert.Equal(384, scaled.Width);
        Assert.Equal(567, scaled.Height);
    }

    [Fact]
    public void Trims_blank_rows_keeping_bottom_margin()
    {
        var bitmap = new MonoBitmap(16, 100);
        bitmap[3, 10] = true;
        bitmap[3, 20] = true;
        var trimmed = Rasterizer.TrimBlankRows(bitmap, bottomMargin: 5);
        Assert.Equal(16, trimmed.Height); // rows 10..20 plus 5 blank rows
        Assert.True(trimmed[3, 0]);
        Assert.Equal(0, Rasterizer.TrimBlankRows(new MonoBitmap(16, 10), 5).Height);
    }

    [Theory]
    [InlineData(DitherMode.Atkinson)]
    [InlineData(DitherMode.FloydSteinberg)]
    public void Mid_grey_dithers_to_about_half_black(DitherMode mode)
    {
        var grey = new GrayImage(384, 200, Enumerable.Repeat((byte)128, 384 * 200).ToArray());
        var mono = Rasterizer.Rasterize(grey, new RasterOptions { Dither = mode, TrimBlank = false });
        var black = Enumerable.Range(0, mono.Height).Sum(y => mono.Row(y).Count((byte)1));
        var ratio = black / (double)(384 * 200);
        // Atkinson loses 1/4 of the error, so mid grey lands a little lighter.
        Assert.InRange(ratio, 0.40, 0.55);
    }

    [Fact]
    public void Threshold_mode_is_a_hard_cut()
    {
        var grey = new GrayImage(384, 4, Enumerable.Repeat((byte)127, 384 * 4).ToArray());
        var mono = Rasterizer.Rasterize(grey, new RasterOptions { Dither = DitherMode.Threshold, TrimBlank = false });
        Assert.All(Enumerable.Range(0, 4), y => Assert.False(mono.IsRowBlank(y)));
        Assert.Equal(384 * 4, Enumerable.Range(0, 4).Sum(y => mono.Row(y).Count((byte)1)));
    }

    [Fact]
    public void Auto_mode_uses_threshold_for_line_art_and_atkinson_for_photos()
    {
        var text = new GrayImage(100, 100);
        for (var x = 10; x < 90; x++) text[x, 50] = 0;
        Assert.Equal(DitherMode.Threshold, Rasterizer.ChooseMode(text));

        var photo = new GrayImage(100, 100);
        for (var i = 0; i < photo.Pixels.Length; i++) photo.Pixels[i] = (byte)(i % 256);
        Assert.Equal(DitherMode.Atkinson, Rasterizer.ChooseMode(photo));
    }

    private static GrayImage Page80mm(int inkFrom, int inkTo)
    {
        // 80 mm at 203 dpi = 640 px, like a Notepad page with A4-style margins.
        var page = new GrayImage(640, 50) { Dpi = 203 };
        for (var y = 10; y < 20; y++)
        for (var x = inkFrom; x <= inkTo; x++)
            page[x, y] = 0;
        return page;
    }

    [Fact]
    public void Narrow_content_on_wide_page_prints_at_real_size_left_aligned()
    {
        // 40 mm column (320 px) starting 20 mm (160 px) from the left edge.
        var mono = Rasterizer.Rasterize(Page80mm(160, 479), new RasterOptions { TrimBlank = false });
        Assert.Equal(384, mono.Width);
        Assert.Equal(50, mono.Height); // scale 1.0: real size
        var firstInk = Enumerable.Range(0, 384).First(x => mono[x, 15]);
        Assert.InRange(firstInk, 6, 10); // 1 mm padding (8 px)
        Assert.True(mono[firstInk + 319, 15]);
    }

    [Fact]
    public void Wide_content_is_shrunk_just_enough()
    {
        // Content spans 70 mm (560 px) → shrink to fit 384 px.
        var mono = Rasterizer.Rasterize(Page80mm(40, 599), new RasterOptions { TrimBlank = false });
        Assert.Equal(384, mono.Width);
        Assert.InRange(mono.Height, 32, 34); // 50 * 384 / 576
        Assert.True(mono[10, 10]);
        Assert.True(mono[373, 10]);
    }

    [Fact]
    public void Fitting_can_be_disabled()
    {
        var mono = Rasterizer.Rasterize(Page80mm(160, 479), new RasterOptions { TrimBlank = false, FitContentWidth = false });
        Assert.Equal(30, mono.Height); // whole 640 px page scaled to 384
    }

    [Fact]
    public void Brightness_lightens_the_image()
    {
        var grey = new GrayImage(10, 1, Enumerable.Repeat((byte)100, 10).ToArray());
        Rasterizer.Adjust(grey, brightness: 50, contrast: 0);
        Assert.True(grey[0, 0] > 200);
    }
}
