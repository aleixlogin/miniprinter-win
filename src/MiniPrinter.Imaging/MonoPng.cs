using MiniPrinter.Protocol;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MiniPrinter.Imaging;

/// <summary>Saves 1-bit rasters as PNG (diagnostics and previews).</summary>
public static class MonoPng
{
    public static void Save(MonoBitmap bitmap, Stream output)
    {
        using var image = new Image<L8>(bitmap.Width, Math.Max(1, bitmap.Height), new L8(255));
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            if (bitmap[x, y]) image[x, y] = new L8(0);
        image.SaveAsPng(output);
    }

    public static void Save(MonoBitmap bitmap, string path)
    {
        using var file = File.Create(path);
        Save(bitmap, file);
    }

    /// <summary>Reads back a PNG saved with <see cref="Save(MonoBitmap, string)"/>: dark pixels are ink.</summary>
    public static MonoBitmap Load(string path)
    {
        using var image = Image.Load<L8>(path);
        var bitmap = new MonoBitmap(image.Width, image.Height);
        for (var y = 0; y < image.Height; y++)
        {
            var row = bitmap.MutableRow(y);
            for (var x = 0; x < image.Width; x++)
                row[x] = image[x, y].PackedValue < 128 ? (byte)1 : (byte)0;
        }
        return bitmap;
    }
}
