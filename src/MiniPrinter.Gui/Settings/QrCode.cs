using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace MiniPrinter.Gui;

/// <summary>The modules of a QR code (true = dark), without the quiet zone: the window draws it at the size it needs.</summary>
public static class QrCode
{
    public static bool[,] Modules(string text)
    {
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M,
            [EncodeHintType.MARGIN] = 0,
            [EncodeHintType.CHARACTER_SET] = "UTF-8",
        };
        BitMatrix matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0, hints);
        var modules = new bool[matrix.Height, matrix.Width];
        for (var y = 0; y < matrix.Height; y++)
        for (var x = 0; x < matrix.Width; x++)
            modules[y, x] = matrix[x, y];
        return modules;
    }
}
