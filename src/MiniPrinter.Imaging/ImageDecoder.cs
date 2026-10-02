using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MiniPrinter.Imaging;

/// <summary>Turns documents (PWG Raster, JPEG, PNG, …) into grey pages.</summary>
public static class ImageDecoder
{
    public static readonly IReadOnlyList<string> SupportedFormats =
        ["image/pwg-raster", "application/pdf", "image/jpeg", "image/png"];

    /// <summary>
    /// Decodes <paramref name="stream"/>. The format is sniffed from the content, so
    /// <paramref name="contentType"/> may be <c>application/octet-stream</c> or null.
    /// </summary>
    /// <exception cref="NotSupportedException">The document format is not supported.</exception>
    public static IEnumerable<GrayImage> Decode(Stream stream, string? contentType = null, Func<int, bool>? includePage = null)
    {
        var buffered = stream.CanSeek ? stream : CopyToMemory(stream);
        Span<byte> head = stackalloc byte[8];
        var read = buffered.Read(head);
        buffered.Seek(-read, SeekOrigin.Current);

        if (PdfRasterizer.IsPdf(head[..read]) || contentType == "application/pdf")
        {
            using var memory = new MemoryStream();
            buffered.CopyTo(memory);
            return PdfRasterizer.Render(memory.ToArray(), includePage);
        }

        var pages = PwgRasterReader.IsPwgRaster(head[..read]) || contentType == "image/pwg-raster"
            ? new PwgRasterReader(buffered).ReadPages()
            : DecodeBitmap(buffered);
        return includePage is null ? pages : pages.Where((_, index) => includePage(index + 1));
    }

    private static IEnumerable<GrayImage> DecodeBitmap(Stream stream)
    {
        Image<L8> image;
        try
        {
            image = Image.Load<L8>(stream);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            throw new NotSupportedException("Unsupported document format.", ex);
        }

        using (image)
        {
            // Honour EXIF orientation from phone photos.
            image.Mutate(x => x.AutoOrient());
            var dpi = image.Metadata.ResolutionUnits == SixLabors.ImageSharp.Metadata.PixelResolutionUnit.PixelsPerInch
                ? (int?)Math.Round(image.Metadata.HorizontalResolution)
                : null;
            return [ToGray(image, dpi)];
        }
    }

    internal static GrayImage ToGray(Image<L8> image, int? dpi = null)
    {
        var pixels = new byte[image.Width * image.Height];
        image.CopyPixelDataTo(pixels);
        return new GrayImage(image.Width, image.Height, pixels) { Dpi = dpi };
    }

    private static MemoryStream CopyToMemory(Stream stream)
    {
        var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;
        return memory;
    }
}
