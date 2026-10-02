using MiniPrinter.Imaging;
using MiniPrinter.Protocol;

namespace MiniPrinter.Cli;

internal static class PrintFile
{
    public static IReadOnlyList<RasterResult> Rasterize(string path, int width, string? dither, bool isText, string? pages = null)
    {
        if (!File.Exists(path))
            throw new CliException($"File not found: {path}");

        var options = new RasterOptions
        {
            WidthPx = width,
            Dither = (dither ?? (isText ? "threshold" : "auto")).ToLowerInvariant() switch
            {
                "auto" => DitherMode.Auto,
                "atkinson" => DitherMode.Atkinson,
                "floyd" or "floyd-steinberg" => DitherMode.FloydSteinberg,
                "threshold" or "none" => DitherMode.Threshold,
                var other => throw new CliException($"Unknown dither mode '{other}'."),
            },
        };

        using var stream = File.OpenRead(path);
        try
        {
            var selection = MiniPrinter.Ipp.DocumentOptions.ParsePages(pages);
            return ImageDecoder.Decode(stream, includePage: selection.Includes)
                .Select(page => Rasterizer.RasterizeWithMode(page, options)).ToList();
        }
        catch (FormatException ex)
        {
            throw new CliException(ex.Message);
        }
        catch (NotSupportedException ex)
        {
            throw new CliException($"{Path.GetFileName(path)}: {ex.Message}");
        }
    }
}
