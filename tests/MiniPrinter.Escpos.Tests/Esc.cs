using System.Text;
using MiniPrinter.Escpos;
using MiniPrinter.Imaging;
using MiniPrinter.Protocol;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MiniPrinter.Escpos.Tests;

/// <summary>Builds ESC/POS byte streams the way python-escpos and similar libraries write them.</summary>
public sealed class Esc
{
    private readonly List<byte> _bytes = [];

    public byte[] Bytes => _bytes.ToArray();

    public Esc Raw(params int[] bytes)
    {
        foreach (var b in bytes) _bytes.Add((byte)b);
        return this;
    }

    public Esc Text(string text, Encoding? encoding = null)
    {
        _bytes.AddRange((encoding ?? Encoding.ASCII).GetBytes(text));
        return this;
    }

    public Esc Line(string text, Encoding? encoding = null) => Text(text, encoding).Raw(0x0A);

    public Esc Init() => Raw(0x1B, '@');
    public Esc Align(int n) => Raw(0x1B, 'a', n);
    public Esc Bold(bool on) => Raw(0x1B, 'E', on ? 1 : 0);
    public Esc Underline(int n) => Raw(0x1B, '-', n);
    public Esc Size(int widthMultiple, int heightMultiple) => Raw(0x1D, '!', ((widthMultiple - 1) << 4) | (heightMultiple - 1));
    public Esc Inverse(bool on) => Raw(0x1D, 'B', on ? 1 : 0);
    public Esc FontB(bool on) => Raw(0x1B, 'M', on ? 1 : 0);
    public Esc Cut() => Raw(0x1D, 'V', 66, 3);
    public Esc Feed(int lines) => Raw(0x1B, 'd', lines);

    public Esc Qr(string data, int module = 6, int ecc = 49)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        Raw(0x1D, '(', 'k', 4, 0, 49, 65, 50, 0);                // model 2
        Raw(0x1D, '(', 'k', 3, 0, 49, 67, module);                // module size
        Raw(0x1D, '(', 'k', 3, 0, 49, 69, ecc);                   // error correction
        var length = bytes.Length + 3;
        Raw(0x1D, '(', 'k', length & 0xFF, length >> 8, 49, 80, 48);
        _bytes.AddRange(bytes);                                    // store
        return Raw(0x1D, '(', 'k', 3, 0, 49, 81, 48);              // print
    }

    /// <summary>PDF417 (48), Aztec (53) or Data Matrix (54) with the GS ( k functions 67, 80 and 81.</summary>
    public Esc Symbol(int cn, string data, int module = 4)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        Raw(0x1D, '(', 'k', 3, 0, cn, 67, module);
        var length = bytes.Length + 3;
        Raw(0x1D, '(', 'k', length & 0xFF, length >> 8, cn, 80, 48);
        _bytes.AddRange(bytes);
        return Raw(0x1D, '(', 'k', 3, 0, cn, 81, 48);
    }


    /// <summary>GS k m n data (function B), as python-escpos writes it.</summary>
    public Esc Barcode(int m, string data, int height = 80, int width = 2, int textPosition = 2)
    {
        Raw(0x1D, 'h', height, 0x1D, 'w', width, 0x1D, 'H', textPosition);
        Raw(0x1D, 'k', m, data.Length);
        return Text(data);
    }

    /// <summary>GS v 0 raster image from a monochrome bitmap.</summary>
    public Esc Raster(MonoBitmap image, int mode = 0)
    {
        var stride = (image.Width + 7) / 8;
        Raw(0x1D, 'v', '0', mode, stride & 0xFF, stride >> 8, image.Height & 0xFF, image.Height >> 8);
        for (var y = 0; y < image.Height; y++)
        for (var xb = 0; xb < stride; xb++)
        {
            var b = 0;
            for (var bit = 0; bit < 8; bit++)
            {
                var x = xb * 8 + bit;
                if (x < image.Width && image[x, y]) b |= 0x80 >> bit;
            }
            _bytes.Add((byte)b);
        }
        return this;
    }

    // ---- running and inspecting -----------------------------------------------------------------

    public static IReadOnlyList<MonoBitmap> Run(byte[] data, Func<PrinterCondition?>? status = null, int chunk = 0, int kanjiCodePage = 932)
    {
        var interpreter = new EscposInterpreter(status, kanjiCodePage: kanjiCodePage);
        if (chunk <= 0)
        {
            interpreter.Feed(data);
        }
        else
        {
            for (var i = 0; i < data.Length; i += chunk)
                interpreter.Feed(data.AsSpan(i, Math.Min(chunk, data.Length - i)));
        }
        interpreter.EndTicket();
        return interpreter.DrainTickets();
    }

    public IReadOnlyList<MonoBitmap> Run(Func<PrinterCondition?>? status = null) => Run(Bytes, status);

    public MonoBitmap Single() => Assert.Single(Run());

    /// <summary>First and last columns that have ink within rows [top, top+height).</summary>
    public static (int Left, int Right) InkColumns(MonoBitmap bitmap, int top = 0, int? height = null)
    {
        int left = int.MaxValue, right = -1;
        for (var y = top; y < Math.Min(bitmap.Height, top + (height ?? bitmap.Height)); y++)
        for (var x = 0; x < bitmap.Width; x++)
            if (bitmap[x, y])
            {
                left = Math.Min(left, x);
                right = Math.Max(right, x);
            }
        return (left, right);
    }

    public static int InkCount(MonoBitmap bitmap, int left = 0, int top = 0, int? width = null, int? height = null)
    {
        var count = 0;
        for (var y = top; y < Math.Min(bitmap.Height, top + (height ?? bitmap.Height)); y++)
        for (var x = left; x < Math.Min(bitmap.Width, left + (width ?? bitmap.Width)); x++)
            if (bitmap[x, y]) count++;
        return count;
    }

    /// <summary>Writes the ticket as a PNG under the given folder when ESCPOS_DUMP names one (for looking at it).</summary>
    public static void Dump(MonoBitmap bitmap, string name)
    {
        if (Environment.GetEnvironmentVariable("ESCPOS_DUMP") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            MonoPng.Save(bitmap, Path.Combine(dir, name + ".png"));
        }
    }


    /// <summary>Every barcode or QR code ZXing finds in the ticket.</summary>
    public static IReadOnlyList<string> DecodeAll(MonoBitmap bitmap)
    {
        var padded = new Image<L8>(bitmap.Width + 80, bitmap.Height + 80, new L8(255));
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            if (bitmap[x, y]) padded[x + 40, y + 40] = new L8(0);
        var bytes = new byte[padded.Width * padded.Height];
        padded.CopyPixelDataTo(bytes);
        var source = new ZXing.RGBLuminanceSource(bytes, padded.Width, padded.Height, ZXing.RGBLuminanceSource.BitmapFormat.Gray8);
        var reader = new ZXing.BarcodeReaderGeneric { Options = { TryHarder = true } };
        return reader.DecodeMultiple(source)?.Select(r => r.Text).ToList() ?? [];
    }

    public static string? Decode(MonoBitmap bitmap)
    {
        var padded = new Image<L8>(bitmap.Width + 80, bitmap.Height + 80, new L8(255));
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            if (bitmap[x, y]) padded[x + 40, y + 40] = new L8(0);
        var bytes = new byte[padded.Width * padded.Height];
        padded.CopyPixelDataTo(bytes);
        var source = new ZXing.RGBLuminanceSource(bytes, padded.Width, padded.Height, ZXing.RGBLuminanceSource.BitmapFormat.Gray8);
        var reader = new ZXing.BarcodeReaderGeneric { Options = { TryHarder = true, PureBarcode = false } };
        return reader.Decode(source)?.Text;
    }
}
