using Docnet.Core;
using Docnet.Core.Exceptions;
using Docnet.Core.Models;

namespace MiniPrinter.Imaging;

/// <summary>The PDF could not be opened (damaged or password protected).</summary>
public sealed class InvalidPdfException(string message, Exception? inner = null) : NotSupportedException(message, inner);

/// <summary>
/// Renders PDF pages to grey images at the printer resolution with PDFium (Docnet.Core), one page
/// at a time. PDFium is not thread-safe, so rendering is serialised.
/// </summary>
public static class PdfRasterizer
{
    public const int Dpi = 203;
    private static readonly object Gate = new();

    public static bool IsPdf(ReadOnlySpan<byte> start) => start.StartsWith("%PDF-"u8);

    public static int PageCount(byte[] pdf)
    {
        lock (Gate)
        {
            using var reader = Open(pdf);
            return reader.GetPageCount();
        }
    }

    /// <summary>
    /// Lazily renders the pages for which <paramref name="includePage"/> (1-based) is true; excluded
    /// pages are not rendered.
    /// </summary>
    public static IEnumerable<GrayImage> Render(byte[] pdf, Func<int, bool>? includePage = null)
    {
        var count = PageCount(pdf);
        for (var index = 0; index < count; index++)
        {
            if (includePage is not null && !includePage(index + 1))
                continue;
            yield return RenderPage(pdf, index);
        }
    }

    private static GrayImage RenderPage(byte[] pdf, int index)
    {
        lock (Gate)
        {
            using var reader = Open(pdf);
            using var page = reader.GetPageReader(index);
            var width = page.GetPageWidth();
            var height = page.GetPageHeight();
            var bgra = page.GetImage(RenderFlags.RenderAnnotations);
            var gray = new byte[width * height];
            for (var i = 0; i < gray.Length; i++)
            {
                var b = bgra[i * 4];
                var g = bgra[i * 4 + 1];
                var r = bgra[i * 4 + 2];
                var a = bgra[i * 4 + 3];
                var luminance = (r * 299 + g * 587 + b * 114) / 1000;
                // PDFium leaves the page background transparent: composite over white paper.
                gray[i] = (byte)((luminance * a + 255 * (255 - a)) / 255);
            }
            return new GrayImage(width, height, gray) { Dpi = Dpi };
        }
    }

    private static Docnet.Core.Readers.IDocReader Open(byte[] pdf)
    {
        try
        {
            return DocLib.Instance.GetDocReader(pdf, new PageDimensions(Dpi / 72.0));
        }
        catch (DocnetLoadDocumentException ex)
        {
            var message = ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
                ? "PDF protegido con contraseña"
                : "PDF dañado o no válido";
            throw new InvalidPdfException(message, ex);
        }
        catch (DocnetException ex)
        {
            throw new InvalidPdfException("PDF dañado o no válido", ex);
        }
    }
}
