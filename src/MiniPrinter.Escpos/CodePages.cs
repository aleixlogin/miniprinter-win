using System.Text;

namespace MiniPrinter.Escpos;

/// <summary>The character tables selectable with <c>ESC t n</c> and the UTF-8 detection of a text run.</summary>
internal static class CodePages
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    static CodePages() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// Table number of <c>ESC t</c> → code page, following the default profile of python-escpos (a
    /// superset of Epson's table). Unknown or unavailable tables behave as CP437.
    /// </summary>
    public static int CodePageOf(int table) => table switch
    {
        1 => 932,      // CP932: half-width katakana and Shift-JIS
        2 => 850,
        3 => 860,
        4 => 863,
        5 => 865,
        11 => 851,
        13 => 857,
        14 => 737,
        15 => 28597,   // ISO 8859-7 (Greek, has the euro sign at 0xA4)
        16 => 1252,
        17 => 866,
        18 => 852,
        19 => 858,
        21 => 874,
        32 => 720,
        33 => 775,
        34 => 855,
        35 => 861,
        36 => 862,
        37 => 864,
        38 => 869,
        39 => 28592,
        40 => 28605,
        45 => 1250,
        46 => 1251,
        47 => 1253,
        48 => 1254,
        49 => 1255,
        50 => 1256,
        51 => 1257,
        52 => 1258,
        _ => 437,
    };

    private static Encoding EncodingOf(int table)
    {
        try
        {
            return Encoding.GetEncoding(CodePageOf(table));
        }
        catch (ArgumentException)
        {
            return Encoding.GetEncoding(437);
        }
    }


    /// <summary>
    /// Bytes whose character the .NET table lacks: ISO 8859-7 is the 1986 version there, without the
    /// euro sign that python-escpos writes at 0xA4 for its table 15.
    /// </summary>
    private static string Patch(int table, string decoded, ReadOnlySpan<byte> bytes)
    {
        if (table != 15 || decoded.Length != bytes.Length)
            return decoded;
        var chars = decoded.ToCharArray();
        for (var i = 0; i < bytes.Length; i++)
            chars[i] = bytes[i] switch { 0xA4 => '€', 0xA5 => '₯', 0xAA => 'ͺ', _ => chars[i] };
        return new string(chars);
    }


    /// <summary>Double-byte text of kanji mode in the given code page (932, 936, 950, 949); 932 if the page is unavailable.</summary>
    public static string DecodeKanji(ReadOnlySpan<byte> bytes, int codePage)
    {
        try
        {
            return Encoding.GetEncoding(codePage).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return Encoding.GetEncoding(932).GetString(bytes);
        }
    }

    /// <summary>
    /// Decodes a run of text bytes: UTF-8 when the run is valid UTF-8 and has bytes above 0x7F (many
    /// modern clients send it that way), otherwise with the active table.
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> bytes, int table)
    {
        if (bytes.ContainsAnyExceptInRange((byte)0, (byte)0x7F))
        {
            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                // not UTF-8: fall through to the code page
            }
        }
        return Patch(table, EncodingOf(table).GetString(bytes), bytes);
    }
}
