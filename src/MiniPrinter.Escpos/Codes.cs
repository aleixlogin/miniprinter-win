using MiniPrinter.Imaging;
using MiniPrinter.Protocol;
using ZXing;
using ZXing.Common;
using ZXing.QrCode.Internal;

namespace MiniPrinter.Escpos;

/// <summary>QR codes and barcodes drawn from the ESC/POS parameters (module size, height, text position).</summary>
internal static class Codes
{
    public const int MinQrModule = 3;

    /// <summary>
    /// A QR code bitmap (with a quiet zone above and below), or <c>null</c> when it cannot fit in
    /// <paramref name="width"/> dots at 3 dots per module or the data is too long for a QR code.
    /// </summary>
    public static MonoBitmap? Qr(string data, int module, int ecc, int width)
    {
        var level = ecc switch
        {
            48 => ErrorCorrectionLevel.L,
            50 => ErrorCorrectionLevel.Q,
            51 => ErrorCorrectionLevel.H,
            _ => ErrorCorrectionLevel.M,
        };
        QRCode code;
        try
        {
            code = Encoder.encode(data, level, new Dictionary<EncodeHintType, object> { [EncodeHintType.CHARACTER_SET] = "UTF-8" });
        }
        catch (WriterException)
        {
            return null;
        }
        var matrix = code.Matrix;
        var modules = matrix.Width;
        module = Math.Clamp(module, MinQrModule, 16);
        while (modules * module > width && module > MinQrModule)
            module--;
        if (modules * module > width)
            return null;

        var size = modules * module;
        var bitmap = new MonoBitmap(size, size + 4 * module);
        for (var y = 0; y < modules; y++)
        for (var x = 0; x < modules; x++)
        {
            if (matrix[x, y] != 1)
                continue;
            for (var dy = 0; dy < module; dy++)
            for (var dx = 0; dx < module; dx++)
                bitmap[x * module + dx, 2 * module + y * module + dy] = true;
        }
        return bitmap;
    }

    /// <summary>
    /// A 2D symbol other than QR: PDF417 (<c>cn</c> 48), Aztec (53) or Data Matrix (54), with a quiet zone of
    /// two modules. Null when the data is not encodable or the symbol does not fit in <paramref name="width"/> at 2 dots per module.
    /// </summary>
    public static MonoBitmap? Symbol(int cn, string data, int module, int width)
    {
        BitMatrix matrix;
        try
        {
            var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.CHARACTER_SET] = "UTF-8", [EncodeHintType.MARGIN] = 0 };
            matrix = cn switch
            {
                48 => new ZXing.PDF417.PDF417Writer().encode(data, BarcodeFormat.PDF_417, 0, 0, hints),
                53 => new ZXing.Aztec.AztecWriter().encode(data, BarcodeFormat.AZTEC, 0, 0, hints),
                54 => new ZXing.Datamatrix.DataMatrixWriter().encode(data, BarcodeFormat.DATA_MATRIX, 0, 0, hints),
                _ => throw new ArgumentOutOfRangeException(nameof(cn)),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or WriterException or System.FormatException)
        {
            return null;
        }
        // Trim the empty border the writers may leave, so the quiet zone is ours.
        int x0 = matrix.Width, y0 = matrix.Height, x1 = -1, y1 = -1;
        for (var y = 0; y < matrix.Height; y++)
        for (var x = 0; x < matrix.Width; x++)
            if (matrix[x, y])
            {
                x0 = Math.Min(x0, x); x1 = Math.Max(x1, x);
                y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
            }
        if (x1 < 0)
            return null;
        var columns = x1 - x0 + 1;
        var rows = y1 - y0 + 1;
        // PDF417 is built of rows three modules tall (the writers give one pixel per row).
        var rowScale = cn == 48 ? 3 : 1;

        module = Math.Clamp(module, 2, 16);
        while (columns * module + 4 * module > width && module > 2)
            module--;
        if (columns * module + 4 * module > width)
            return null;
        var bitmap = new MonoBitmap(columns * module + 4 * module, rows * rowScale * module + 4 * module);
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < columns; x++)
        {
            if (!matrix[x0 + x, y0 + y])
                continue;
            for (var dy = 0; dy < rowScale * module; dy++)
            for (var dx = 0; dx < module; dx++)
                bitmap[2 * module + x * module + dx, 2 * module + y * rowScale * module + dy] = true;
        }
        return bitmap;
    }


    private static bool Digits(string text, int min, int max) =>
        text.Length >= min && text.Length <= max && text.All(char.IsAsciiDigit);

    /// <summary>
    /// The bars of one barcode (<paramref name="humanReadable"/> receives the text to print under or
    /// over it), or <c>null</c> when the data is not valid for the format or does not fit.
    /// </summary>
    public static MonoBitmap? Barcode(string format, string data, int barWidth, int height, int width, out string humanReadable)
    {
        humanReadable = data;
        BitMatrix matrix;
        try
        {
            switch (format)
            {
                case "upca":
                    if (!Digits(data, 11, 12)) return null;
                    humanReadable = data[..11] + TemplateRenderer.UpcaCheckDigit(data[..11]);
                    if (data.Length == 12 && data[11] != humanReadable[11]) return null;
                    matrix = new ZXing.OneD.UPCAWriter().encode(humanReadable, BarcodeFormat.UPC_A, 0, 0);
                    break;
                case "upce":
                    matrix = new ZXing.OneD.UPCEWriter().encode(data, BarcodeFormat.UPC_E, 0, 0);
                    break;
                case "ean13":
                    if (!Digits(data, 12, 13)) return null;
                    humanReadable = data[..12] + TemplateRenderer.Ean13CheckDigit(data[..12]);
                    if (data.Length == 13 && data[12] != humanReadable[12]) return null;
                    matrix = new ZXing.OneD.EAN13Writer().encode(humanReadable, BarcodeFormat.EAN_13, 0, 0);
                    break;
                case "ean8":
                    matrix = new ZXing.OneD.EAN8Writer().encode(data, BarcodeFormat.EAN_8, 0, 0);
                    break;
                case "code39":
                    humanReadable = data.ToUpperInvariant();
                    matrix = new ZXing.OneD.Code39Writer().encode(humanReadable, BarcodeFormat.CODE_39, 0, 0);
                    break;
                case "itf":
                    matrix = new ZXing.OneD.ITFWriter().encode(data, BarcodeFormat.ITF, 0, 0);
                    break;
                case "codabar":
                    matrix = new ZXing.OneD.CodaBarWriter().encode(data, BarcodeFormat.CODABAR, 0, 0);
                    break;
                case "code93":
                    matrix = new ZXing.OneD.Code93Writer().encode(data, BarcodeFormat.CODE_93, 0, 0);
                    break;
                default:
                    // "{A", "{B" and "{C" select the Code 128 set; the writer picks the best sets by itself.
                    if (data.Length > 2 && data[0] == '{' && data[1] is 'A' or 'B' or 'C')
                        data = data[2..];
                    humanReadable = data;
                    matrix = new ZXing.OneD.Code128Writer().encode(data, BarcodeFormat.CODE_128, 0, 0);
                    break;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or System.FormatException or WriterException)
        {
            return null;
        }

        var modules = matrix.Width;
        var bar = Math.Clamp(barWidth, 1, 6);
        while (modules * bar > width && bar > 1)
            bar--;
        if (modules * bar > width)
            return null;
        var bitmap = new MonoBitmap(modules * bar, Math.Max(1, height));
        for (var x = 0; x < modules; x++)
        {
            if (!matrix[x, 0])
                continue;
            for (var dx = 0; dx < bar; dx++)
            for (var y = 0; y < bitmap.Height; y++)
                bitmap[x * bar + dx, y] = true;
        }
        return bitmap;
    }

    /// <summary>The format name of <c>GS k</c> (A format 0–6, B format 65–73).</summary>
    public static string? FormatOf(int m) => m switch
    {
        0 or 65 => "upca",
        1 or 66 => "upce",
        2 or 67 => "ean13",
        3 or 68 => "ean8",
        4 or 69 => "code39",
        5 or 70 => "itf",
        6 or 71 => "codabar",
        72 => "code93",
        73 => "code128",
        _ => null,
    };
}
