using MiniPrinter.Protocol;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;

namespace MiniPrinter.Imaging.Tests;

public class TemplateTests
{
    private static Dictionary<string, string> F(params (string Key, string Value)[] fields) =>
        fields.ToDictionary(f => f.Key, f => f.Value);

    /// <summary>Decodes a printed raster with ZXing, as a phone camera would.</summary>
    private static Result? Decode(MonoBitmap bitmap, BarcodeFormat format)
    {
        var luminance = new byte[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            luminance[y * bitmap.Width + x] = bitmap[x, y] ? (byte)0 : (byte)255;
        var reader = new BarcodeReaderGeneric
        {
            Options = { PossibleFormats = [format], TryHarder = true, PureBarcode = false },
        };
        return reader.Decode(new RGBLuminanceSource(luminance, bitmap.Width, bitmap.Height, RGBLuminanceSource.BitmapFormat.Gray8));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("Página ñandú: ¿funciona?")]
    public void Qr_is_readable_and_uses_big_modules(string data)
    {
        var bitmap = TemplateRenderer.Render("qr", F(("data", data), ("caption", "Escanéame")));
        Assert.Equal(384, bitmap.Width);
        Assert.Equal(data, Decode(bitmap, BarcodeFormat.QR_CODE)?.Text);
    }

    [Fact]
    public void Qr_too_long_for_the_paper_is_rejected()
    {
        var error = Assert.Throws<TemplateException>(() => TemplateRenderer.Render("qr", F(("data", new string('x', 1000)))));
        Assert.Equal("El contenido es demasiado largo para el ancho del papel.", error.Message);
    }

    [Fact]
    public void Code128_is_readable()
    {
        var bitmap = TemplateRenderer.Render("barcode", F(("data", "MINI-0042")));
        Assert.Equal("MINI-0042", Decode(bitmap, BarcodeFormat.CODE_128)?.Text);
    }

    [Fact]
    public void Ean13_is_readable_and_check_digit_is_validated()
    {
        var bitmap = TemplateRenderer.Render("barcode", F(("data", "590123412345"), ("format", "ean13")));
        Assert.Equal("5901234123457", Decode(bitmap, BarcodeFormat.EAN_13)?.Text);

        var error = Assert.Throws<TemplateException>(() =>
            TemplateRenderer.Render("barcode", F(("data", "5901234123450"), ("format", "ean13"))));
        Assert.Contains("debería ser 7", error.Message);
    }

    [Fact]
    public void Todo_has_one_checkbox_per_item()
    {
        var bitmap = TemplateRenderer.Render("todo", F(("title", "Compra"), ("items", "Pan\nLeche\n\n")));
        // Column through the checkboxes' left border: count separate vertical runs (one per box).
        var x = TextRenderer.MarginPx + 1;
        var runs = 0;
        for (var y = 1; y < bitmap.Height; y++)
            if (bitmap[x, y] && !bitmap[x, y - 1]) runs++;
        Assert.Equal(2, runs);
    }

    [Fact]
    public void Label_and_sticker_render()
    {
        Assert.True(TemplateRenderer.Render("label", F(("title", "FRÁGIL"), ("line1", "Este lado arriba"))).Height > 60);

        using var png = new MemoryStream();
        using (var image = new Image<L8>(200, 100, new L8(0)))
            image.SaveAsPng(png);
        var sticker = TemplateRenderer.Render("sticker", F(("image", Convert.ToBase64String(png.ToArray())), ("caption", "Hola")));
        Assert.Equal(384, sticker.Width);
        Assert.True(sticker.Height > 192); // 200x100 image scaled to 384 wide, plus caption
    }

    [Theory]
    [InlineData("qr", "data")]
    [InlineData("label", "title")]
    [InlineData("todo", "items")]
    public void Missing_required_field_is_reported(string template, string field)
    {
        var error = Assert.Throws<TemplateException>(() => TemplateRenderer.Render(template, F()));
        Assert.Contains($"'{field}'", error.Message);
    }

    [Fact]
    public void Unknown_template_lists_the_available_ones()
    {
        var error = Assert.Throws<TemplateException>(() => TemplateRenderer.Render("no-existe", F()));
        Assert.Contains("qr, barcode, todo, label, sticker", error.Message);
    }
}
