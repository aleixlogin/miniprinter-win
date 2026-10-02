namespace MiniPrinter.Imaging.Tests;

public class PdfTests
{
    [Fact]
    public void Renders_pages_at_203_dpi()
    {
        var pages = PdfRasterizer.Render(MiniPdf.Create(1)).ToList();
        var page = Assert.Single(pages);
        Assert.Equal(203, page.Dpi);
        Assert.InRange(page.Width, 1675, 1680);   // 595 pt * 203 / 72
        Assert.InRange(page.Height, 2370, 2376);  // 842 pt * 203 / 72
        // The black bar is drawn near the top (PDF y=700..760 → image y≈230..400) and the page is white elsewhere.
        Assert.Equal(255, page[10, 10]);
        Assert.True(page[300, 300] < 50);
    }

    [Fact]
    public void Multi_page_document_and_page_selection()
    {
        var pdf = MiniPdf.Create(3);
        Assert.Equal(3, PdfRasterizer.PageCount(pdf));
        var selected = PdfRasterizer.Render(pdf, p => p != 2).ToList();
        Assert.Equal(2, selected.Count);

        // Bar width encodes the page number: page 3's bar is wider than page 1's.
        static int BarWidth(GrayImage page) => Enumerable.Range(0, page.Width).Count(x => page[x, 300] < 128);
        Assert.True(BarWidth(selected[1]) > BarWidth(selected[0]) * 2);
    }

    [Fact]
    public void Decoder_sniffs_pdf_and_pipeline_fits_it_to_the_head()
    {
        using var stream = new MemoryStream(MiniPdf.Create(2));
        var pages = ImageDecoder.Decode(stream, "application/octet-stream").ToList();
        Assert.Equal(2, pages.Count);
        var mono = Rasterizer.Rasterize(pages[0], new RasterOptions());
        Assert.Equal(384, mono.Width);
        Assert.True(mono.Height > 0);
    }

    [Fact]
    public void Damaged_pdf_raises_a_clear_error()
    {
        var error = Assert.Throws<InvalidPdfException>(() => PdfRasterizer.Render(MiniPdf.Damaged()).ToList());
        Assert.Equal("PDF dañado o no válido", error.Message);
        Assert.IsAssignableFrom<NotSupportedException>(error);
    }
}
