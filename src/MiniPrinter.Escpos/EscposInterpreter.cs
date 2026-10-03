using System.Text;
using MiniPrinter.Protocol;

namespace MiniPrinter.Escpos;

/// <summary>What a client can ask the printer about (DLE EOT, GS r): paper, cover and error alarms.</summary>
public readonly record struct PrinterCondition(bool PaperOut = false, bool CoverOpen = false, bool Error = false);

/// <summary>
/// Streaming ESC/POS interpreter for a 384-dot head. Feed it the bytes of a connection in any chunking:
/// text is laid out in fixed character cells, a cut (<c>GS V</c>) ends a ticket, and status requests
/// are answered through <see cref="DrainResponses"/>. Unknown commands are skipped, never printed.
/// </summary>
public sealed class EscposInterpreter
{
    public const int HeadWidth = 384;

    /// <summary>A ticket taller than this (dots) is split, so that a hostile stream cannot grow without bound.</summary>
    public const int MaxTicketRows = 16000;

    /// <summary>Dots of paper one connection may ask for (about 20 m): beyond it the interpreter ignores the stream.</summary>
    public const int MaxTotalRows = 160_000;

    /// <summary>Largest command (image data) that is buffered; a longer declared length is skipped without buffering it.</summary>
    public const int MaxCommandBytes = 4 * 1024 * 1024;

    private const int DefaultLineGap = 6;

    private readonly Func<PrinterCondition?> _status;
    private readonly Action<string>? _log;
    private readonly int _width;
    private readonly HashSet<string> _unknownLogged = [];

    // Pending input: bytes not yet parsed (an incomplete command) and the current run of text bytes.
    private byte[] _buffer = new byte[1024];
    private int _start, _end;
    private readonly List<byte> _text = [];

    // Output.
    private readonly List<MonoBitmap> _tickets = [];
    private readonly List<byte> _responses = [];

    // Current ticket: finished rows (as parts) and the line being built.
    private readonly List<(MonoBitmap? Bitmap, int Rows)> _parts = [];
    private long _totalRows;
    private long _skip;   // bytes of an over-long command still to be discarded
    private int _partRows;
    private readonly List<Item> _items = [];
    private int _lineWidth;
    private int _lineAlign;

    // Style.
    private int _table;
    private bool _kanji;
    private readonly int _kanjiCodePage;
    private readonly TextRegion _defaultRegion;
    private bool _bold, _inverse, _fontB;
    private int _underline;
    private int _scaleW = 1, _scaleH = 1;
    private int _align;
    private int? _pitch;
    private int _spacing;

    // Codes.
    private string _qrData = "";
    private int _qrModule = 3, _qrEcc = 49;
    private int _barHeight = 162, _barWidth = 3, _barText;

    /// <param name="kanjiCodePage">Code page of the double-byte text of kanji mode: 932 Shift-JIS (default), 936 GBK, 950 Big5 or 949 EUC-KR.</param>
    public EscposInterpreter(Func<PrinterCondition?>? status = null, Action<string>? log = null, int width = HeadWidth, int kanjiCodePage = 932)
    {
        _status = status ?? (() => null);
        _log = log;
        _width = width;
        _kanjiCodePage = kanjiCodePage;
        _defaultRegion = CellFont.RegionOfCodePage(kanjiCodePage);
    }

    /// <summary>True when something has been received since the last ticket ended.</summary>
    /// <summary>The connection asked for more than <see cref="MaxTotalRows"/> dots of paper; the rest of the stream is ignored.</summary>
    public bool Overflowed { get; private set; }

    public bool HasPending => _text.Count > 0 || _items.Count > 0 || _parts.Count > 0;

    /// <summary>Bytes the client is waiting for (answers to DLE EOT and GS r), in order; clears them.</summary>
    public byte[] DrainResponses()
    {
        var bytes = _responses.ToArray();
        _responses.Clear();
        return bytes;
    }

    /// <summary>Tickets finished by a cut since the last call; clears them.</summary>
    public IReadOnlyList<MonoBitmap> DrainTickets()
    {
        var tickets = _tickets.ToArray();
        _tickets.Clear();
        return tickets;
    }

    /// <summary>Interprets a chunk of the stream. A command split across chunks waits for its remaining bytes.</summary>
    public void Feed(ReadOnlySpan<byte> data)
    {
        if (_end + data.Length > _buffer.Length)
        {
            var pending = _end - _start;
            var needed = pending + data.Length;
            var target = _buffer.Length;
            while (target < needed)
                target *= 2;
            var next = target == _buffer.Length ? _buffer : new byte[target];
            Buffer.BlockCopy(_buffer, _start, next, 0, pending);
            _buffer = next;
            _start = 0;
            _end = pending;
        }
        data.CopyTo(_buffer.AsSpan(_end));
        _end += data.Length;
        Parse();
        if (_start == _end)
            _start = _end = 0;
    }

    /// <summary>
    /// Ends the current ticket without a cut (connection closed or idle): flushes pending text and
    /// publishes the ticket if it has anything to print. An incomplete command is dropped.
    /// </summary>
    public void EndTicket()
    {
        _start = _end = 0;
        FlushText();
        FlushLine();
        CompleteTicket();
    }

    // ---- parser --------------------------------------------------------------------------------

    private void Parse()
    {
        while (_start < _end)
        {
            if (_skip > 0)
            {
                var drop = (int)Math.Min(_skip, Avail);
                _skip -= drop;
                _start += drop;
                continue;
            }
            var b = _buffer[_start];
            if (b >= 0x20)
            {
                _text.Add(b);
                _start++;
                continue;
            }

            FlushText();
            int consumed;
            switch (b)
            {
                case 0x0A or 0x0C:
                    LineFeed();
                    consumed = 1;
                    break;
                case 0x09:
                    Tab();
                    consumed = 1;
                    break;
                case 0x10:
                    consumed = ParseDle();
                    break;
                case 0x1B:
                    consumed = ParseEsc();
                    break;
                case 0x1C:
                    consumed = ParseFs();
                    break;
                case 0x1D:
                    consumed = ParseGs();
                    break;
                default:
                    consumed = 1; // NUL, CR and other control characters do nothing
                    break;
            }
            if (consumed < 0)
                return; // the command is not complete yet
            _start += consumed;
        }
    }

    private int Avail => _end - _start;

    private byte At(int offset) => _buffer[_start + offset];

    private static int Flag(int value, int bit = 1) => value & bit;

    private int ParseDle()
    {
        if (Avail < 2)
            return -1;
        switch (At(1))
        {
            case 0x04: // DLE EOT n
                if (Avail < 3)
                    return -1;
                Reply(At(2));
                return 3;
            case 0x05: // DLE ENQ n
                return Avail < 3 ? -1 : 3;
            case 0x14: // DLE DC4 fn m t
                return Avail < 5 ? -1 : 5;
            default:
                return Unknown("DLE", At(1), 1);
        }
    }

    private int ParseFs()
    {
        if (Avail < 2)
            return -1;
        var cmd = At(1);
        if (cmd is (byte)'&' or (byte)'.')
        {
            _kanji = cmd == (byte)'&';   // FS & selects kanji mode, FS . cancels it
            return 2;
        }
        if (cmd == (byte)'q')
            return ParseNvBitImages();
        var parameters = cmd switch
        {
            (byte)'&' or (byte)'.' => 0,
            (byte)'!' or (byte)'-' or (byte)'C' or (byte)'W' => 1,
            (byte)'S' or (byte)'p' => 2,
            _ => -1,
        };
        if (parameters < 0)
            return Unknown("FS", cmd, 2);
        return Avail < 2 + parameters ? -1 : 2 + parameters;
    }

    private int ParseEsc()
    {
        if (Avail < 2)
            return -1;
        var cmd = At(1);
        int Need(int parameters) => Avail < 2 + parameters ? -1 : 2 + parameters;

        switch ((char)cmd)
        {
            case '@':
                ResetStyle();
                return 2;
            case '!':
            {
                var n = Need(1);
                if (n < 0) return n;
                var mode = At(2);
                _fontB = Flag(mode, 0x01) != 0;
                _bold = Flag(mode, 0x08) != 0;
                _scaleH = Flag(mode, 0x10) != 0 ? 2 : 1;
                _scaleW = Flag(mode, 0x20) != 0 ? 2 : 1;
                _underline = Flag(mode, 0x80) != 0 ? 1 : 0;
                return n;
            }
            case '-':
            {
                var n = Need(1);
                if (n < 0) return n;
                _underline = At(2) switch { 1 or 49 => 1, 2 or 50 => 2, _ => 0 };
                return n;
            }
            case 'E' or 'G':
            {
                var n = Need(1);
                if (n < 0) return n;
                _bold = (At(2) & 1) != 0;
                return n;
            }
            case 'M':
            {
                var n = Need(1);
                if (n < 0) return n;
                _fontB = At(2) is 1 or 49;
                return n;
            }
            case 'a':
            {
                var n = Need(1);
                if (n < 0) return n;
                _align = At(2) switch { 1 or 49 => 1, 2 or 50 => 2, _ => 0 };
                return n;
            }
            case 't':
            {
                var n = Need(1);
                if (n < 0) return n;
                _table = At(2);
                return n;
            }
            case 'd':
            {
                var n = Need(1);
                if (n < 0) return n;
                FlushLine();
                AddBlank(At(2) * LineHeight(CellHeight()));
                return n;
            }
            case 'J':
            {
                var n = Need(1);
                if (n < 0) return n;
                FlushLine();
                AddBlank(At(2));
                return n;
            }
            case '2':
                _pitch = null;
                return 2;
            case '3':
            {
                var n = Need(1);
                if (n < 0) return n;
                _pitch = At(2);
                return n;
            }
            case ' ':
            {
                var n = Need(1);
                if (n < 0) return n;
                _spacing = At(2);
                return n;
            }
            case 'i' or 'm': // partial and full cut of older models
                CutTicket();
                return 2;
            case '*':
                return ParseBitImage();
            case 'W': // page-mode print area: x y width height (2 bytes each)
                return Need(8);
            case '\f': // ESC FF: print the page-mode page
                return 2;
            case '&': // define user characters: y c1 c2 then, per character, x and x*y bytes
                return ParseUserCharacters();
            case 'D': // tab stops, ended by NUL
            {
                for (var i = 2; i < Math.Min(Avail, 34); i++)
                    if (At(i) == 0)
                        return i + 1;
                return Avail >= 34 ? 2 : -1;
            }
            // Accepted and ignored, skipping their parameters (drawer, buzzer, rotation, colours, tabs, ...).
            case 'p' or '7':
                return Need(3);
            case 'c' or '$' or '\\' or 'B': // ESC B n t is the beeper
                return Need(2);
            case 'R' or 'V' or 'r' or '{' or '4' or '=' or 'T' or 'U' or '>' or 'u' or 'v' or '%' or '?' or 'L' or 'S':
                return Need((char)cmd is 'L' or 'S' ? 0 : 1);
            default:
                return Unknown("ESC", cmd, 2);
        }
    }

    private int ParseGs()
    {
        if (Avail < 2)
            return -1;
        var cmd = At(1);
        int Need(int parameters) => Avail < 2 + parameters ? -1 : 2 + parameters;

        switch ((char)cmd)
        {
            case '!':
            {
                var n = Need(1);
                if (n < 0) return n;
                _scaleW = ((At(2) >> 4) & 0x07) + 1;
                _scaleH = (At(2) & 0x07) + 1;
                return n;
            }
            case 'B':
            {
                var n = Need(1);
                if (n < 0) return n;
                _inverse = (At(2) & 1) != 0;
                return n;
            }
            case 'V':
            {
                if (Avail < 3)
                    return -1;
                var m = At(2);
                var length = m is 65 or 66 or 97 or 98 ? 4 : 3;
                if (Avail < length)
                    return -1;
                CutTicket();
                return length;
            }
            case 'H':
            {
                var n = Need(1);
                if (n < 0) return n;
                _barText = At(2) & 3;
                return n;
            }
            case 'h':
            {
                var n = Need(1);
                if (n < 0) return n;
                _barHeight = Math.Max(1, (int)At(2));
                return n;
            }
            case 'w':
            {
                var n = Need(1);
                if (n < 0) return n;
                _barWidth = Math.Clamp((int)At(2), 1, 6);
                return n;
            }
            case 'r':
            {
                var n = Need(1);
                if (n < 0) return n;
                ReplyGsR(At(2));
                return n;
            }
            case 'v':
                return ParseRasterImage();
            case 'k':
                return ParseBarcode();
            case '(':
                return ParseGsFunction();
            case '*': // define a downloaded bit image: x y then x*y*8 bytes (ignored)
            {
                if (Avail < 4)
                    return -1;
                var length = 4 + At(2) * At(3) * 8;
                return Avail < length ? -1 : length;
            }
            case 'f' or 'a' or '/' or 'I' or 'r' or 'D' or 'E' or 'z':
                return Need(1);
            case 'P' or 'L' or 'W' or 'x' or 'y':
                return Need(2);
            default:
                return Unknown("GS", cmd, 2);
        }
    }

    /// <summary><c>ESC &amp; y c1 c2 [x d1..d(x*y)]…</c>: user-defined characters, skipped (the cell font is fixed).</summary>
    private int ParseUserCharacters()
    {
        if (Avail < 5)
            return -1;
        int y = At(2), first = At(3), last = At(4);
        if (y is < 1 or > 3 || last < first)
            return Unknown("ESC", (byte)'&', 2);
        var offset = 5;
        for (var c = first; c <= last; c++)
        {
            if (Avail <= offset)
                return -1;
            offset += 1 + At(offset) * y;
        }
        return Avail < offset ? -1 : offset;
    }

    /// <summary><c>FS q n [xL xH yL yH d…]</c>: NV bit images are stored in the printer, here skipped.</summary>
    private int ParseNvBitImages()
    {
        if (Avail < 3)
            return -1;
        var offset = 3L;
        for (var i = 0; i < At(2); i++)
        {
            if (Avail < offset + 4)
                return -1;
            var size = (long)(At((int)offset) | (At((int)offset + 1) << 8)) * 8 * (At((int)offset + 2) | (At((int)offset + 3) << 8));
            offset += 4 + size;
            if (offset > MaxCommandBytes)
            {
                _skip = offset - Avail;
                return Avail;
            }
        }
        return Avail < offset ? -1 : (int)offset;
    }


    /// <summary>Skips an unknown command by dropping only its prefix, and logs it once.</summary>
    private int Unknown(string prefix, byte cmd, int length)
    {
        var name = $"{prefix} 0x{cmd:X2}";
        if (_unknownLogged.Add(name))
            _log?.Invoke($"ESC/POS: unsupported command {name} skipped");
        return length;
    }

    // ---- text and lines ---------------------------------------------------------------------------

    private void FlushText()
    {
        if (Overflowed)
            _text.Clear();
        if (_text.Count == 0)
            return;
        var bytes = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_text);
        var text = _kanji ? CodePages.DecodeKanji(bytes, _kanjiCodePage) : CodePages.Decode(bytes, _table);
        _text.Clear();
        try
        {
            text = text.Normalize(NormalizationForm.FormC);   // e + combining acute → é
        }
        catch (ArgumentException)
        {
            // an unpaired surrogate: draw it as is (it becomes a '?')
        }
        text = CellFont.VisualOrder(text);
        var region = CellFont.RegionOf(text, _defaultRegion);
        foreach (var (segment, script) in CellFont.Segment(text))
        {
            if (Overflowed)
                break;
            if (script != ComplexScript.None && EmitRun(segment, script))
                continue;
            EmitCells(segment, region);
        }
    }

    /// <summary>Arabic and Thai go through the font engine as whole runs (joined letters, positioned marks).</summary>
    private bool EmitRun(string text, ComplexScript script)
    {
        var lines = CellFont.Run(text, script, _bold, _fontB, _width / _scaleW);
        if (lines is null)
            return false;
        var cellH = CellFont.CellHeight(_fontB);
        foreach (var line in lines)
            AddItem(new Item(Scale(line, _scaleW, _scaleH), line.Width * _scaleW, cellH * _scaleH, _inverse, _underline));
        return true;
    }

    private void EmitCells(string text, TextRegion region)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (Overflowed)
                break;
            if (rune.Value < 0x20 || CellFont.IsZeroWidth(rune))
                continue;
            var glyph = CellFont.Glyph(rune, _bold, _fontB, region);
            var cellW = CellFont.CellWidth(_fontB) * glyph.Cells;
            var cellH = CellFont.CellHeight(_fontB);
            AddItem(new Item(
                glyph.Bitmap is null ? null : Scale(glyph.Bitmap, _scaleW, _scaleH),
                cellW * _scaleW + _spacing, cellH * _scaleH, _inverse, _underline));
        }
    }

    private int CellHeight() => CellFont.CellHeight(_fontB) * _scaleH;

    private int LineHeight(int content) => _pitch is { } pitch ? Math.Max(content, pitch) : content + DefaultLineGap;

    private void AddItem(Item item)
    {
        if (_items.Count > 0 && _lineWidth + item.Advance > _width)
            FlushLine();
        if (_items.Count == 0)
            _lineAlign = _align;
        _items.Add(item);
        _lineWidth += item.Advance;
    }

    private void Tab()
    {
        // Horizontal tab: advance to the next multiple of 8 columns.
        var cell = CellFont.CellWidth(_fontB) * _scaleW;
        var column = _lineWidth / cell;
        var next = (column / 8 + 1) * 8;
        for (var i = column; i < next; i++)
            AddItem(new Item(null, cell, CellHeight(), false, 0));
    }

    private void LineFeed()
    {
        if (_items.Count > 0)
            FlushLine();
        else
            AddBlank(LineHeight(CellHeight()));
    }

    private void FlushLine()
    {
        if (Overflowed)
        {
            _items.Clear();
            _lineWidth = 0;
        }
        if (_items.Count == 0)
            return;
        var contentHeight = _items.Max(i => i.Height);
        var line = new MonoBitmap(_width, LineHeight(contentHeight));
        var x = Math.Max(0, _lineAlign switch { 1 => (_width - _lineWidth) / 2, 2 => _width - _lineWidth, _ => 0 });
        foreach (var item in _items)
        {
            Draw(item, line, x, contentHeight - item.Height);
            x += item.Advance;
        }
        _items.Clear();
        _lineWidth = 0;
        AddPart(line);
    }

    private static void Draw(Item item, MonoBitmap target, int x, int y)
    {
        for (var yy = 0; yy < item.Height; yy++)
        for (var xx = 0; xx < item.Advance; xx++)
        {
            var tx = x + xx;
            var ty = y + yy;
            if (tx >= target.Width || ty >= target.Height)
                continue;
            var on = item.Bitmap is { } bmp && xx < bmp.Width && yy < bmp.Height && bmp[xx, yy];
            if (item.Inverse)
                on = !on;
            if (item.Underline > 0 && yy >= item.Height - item.Underline)
                on = !item.Inverse;
            if (on)
                target[tx, ty] = true;
        }
    }

    private static MonoBitmap Scale(MonoBitmap glyph, int sx, int sy)
    {
        if (sx == 1 && sy == 1)
            return glyph;
        var scaled = new MonoBitmap(glyph.Width * sx, glyph.Height * sy);
        for (var y = 0; y < scaled.Height; y++)
        for (var x = 0; x < scaled.Width; x++)
            scaled[x, y] = glyph[x / sx, y / sy];
        return scaled;
    }

    // ---- ticket --------------------------------------------------------------------------------------

    private void AddBlank(int rows)
    {
        if (rows > 0)
            AddPart(null, Math.Min(rows, MaxTicketRows));   // a feed longer than a whole ticket is just a ticket of blank paper
    }

    private void AddPart(MonoBitmap part) => AddPart(part, part.Height);

    /// <summary>Adds rows to the ticket (null = blank paper, kept as a count and never allocated).</summary>
    private void AddPart(MonoBitmap? part, int rows)
    {
        if (Overflowed)
            return;
        _totalRows += rows;
        if (_totalRows > MaxTotalRows)
        {
            // A connection that asks for more paper than this is not printing tickets: publish what we have and ignore the rest.
            Overflowed = true;
            _log?.Invoke($"ESC/POS: more than {MaxTotalRows} dots of paper requested on one connection, the rest is ignored");
            CompleteTicket();
            _items.Clear();
            _lineWidth = 0;
            _text.Clear();
            return;
        }
        _parts.Add((part, rows));
        _partRows += rows;
        if (_partRows >= MaxTicketRows)
            CompleteTicket();
    }

    private void CutTicket()
    {
        FlushLine();
        CompleteTicket();
    }

    /// <summary>Joins the rows of the ticket, drops the blank rows at the end and publishes it if it has ink.</summary>
    private void CompleteTicket()
    {
        if (_parts.Count == 0)
            return;
        // Everything after the last part that has pixels is blank paper at the end of the ticket: not even allocated.
        var lastInk = _parts.FindLastIndex(p => p.Bitmap is not null);
        var rows = 0;
        for (var i = 0; i <= lastInk; i++)
            rows += _parts[i].Rows;
        if (lastInk < 0)
        {
            _parts.Clear();
            _partRows = 0;
            return; // only blank paper
        }
        var ticket = new MonoBitmap(_width, rows);
        var y = 0;
        for (var i = 0; i <= lastInk; i++)
        {
            var (bitmap, count) = _parts[i];
            if (bitmap is not null)
                for (var row = 0; row < bitmap.Height; row++)
                    bitmap.Row(row).CopyTo(ticket.MutableRow(y + row));
            y += count;
        }
        _parts.Clear();
        _partRows = 0;

        var last = ticket.Height - 1;
        while (last >= 0 && ticket.IsRowBlank(last))
            last--;
        if (last < 0)
            return; // nothing to print
        _tickets.Add(last == ticket.Height - 1 ? ticket : ticket.Slice(0, last + 1));
    }

    private void ResetStyle()
    {
        _table = 0;
        _kanji = false;
        _bold = _inverse = _fontB = false;
        _underline = 0;
        _scaleW = _scaleH = 1;
        _align = 0;
        _pitch = null;
        _spacing = 0;
    }

    // ---- images and codes ----------------------------------------------------------------------------

    /// <summary>Puts a standalone bitmap (image, QR, barcode) on its own rows, aligned like text.</summary>
    private void AddBlock(MonoBitmap bitmap)
    {
        FlushLine();
        var block = new MonoBitmap(_width, bitmap.Height);
        var left = _align switch { 1 => (_width - bitmap.Width) / 2, 2 => _width - bitmap.Width, _ => 0 };
        left = Math.Max(0, left);
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width && left + x < _width; x++)
            if (bitmap[x, y])
                block[left + x, y] = true;
        AddPart(block);
    }

    /// <summary>Reads 1-bit rows; only the columns that fit the head are kept, so a wide image costs nothing extra.</summary>
    private MonoBitmap? ReadBits(int offset, int bytesPerRow, int rows)
    {
        if (Overflowed || rows <= 0 || rows > MaxTicketRows || bytesPerRow <= 0)
            return null;
        var width = Math.Min(bytesPerRow * 8, _width);
        var bitmap = new MonoBitmap(width, rows);
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < width; x++)
            if ((At(offset + y * bytesPerRow + x / 8) & (0x80 >> (x % 8))) != 0)
                bitmap[x, y] = true;
        return bitmap;
    }

    private int ParseRasterImage()
    {
        // GS v 0 m xL xH yL yH d1..dk
        if (Avail < 3)
            return -1;
        if (At(2) is not (0x30 or 0x00))
            return Unknown("GS v", At(2), 3);
        if (Avail < 8)
            return -1;
        var m = At(3);
        var bytesPerRow = At(4) | (At(5) << 8);
        var rows = At(6) | (At(7) << 8);
        var length = 8L + (long)bytesPerRow * rows;
        if (length > MaxCommandBytes)
        {
            // Too big to be a ticket image: drop it without buffering (the data may be gigabytes long).
            _log?.Invoke($"ESC/POS: raster image of {length} bytes skipped");
            _skip = length - 8;
            return 8;
        }
        if (Avail < length)
            return -1;
        if (ReadBits(8, bytesPerRow, rows) is { } image)
        {
            var sx = (m & 1) != 0 ? 2 : 1;
            var sy = (m & 2) != 0 ? 2 : 1;
            AddBlock(Scale(image, sx, sy));
        }
        else if (!Overflowed)
        {
            _log?.Invoke($"ESC/POS: raster image {bytesPerRow * 8}x{rows} not printed (empty or taller than {MaxTicketRows} dots)");
        }
        return (int)length;
    }

    private int ParseBitImage()
    {
        // ESC * m nL nH d1..dk: columns of 8 or 24 dots, most significant bit on top
        if (Avail < 5)
            return -1;
        var m = At(2);
        var columns = At(3) | (At(4) << 8);
        var bytesPerColumn = m is 32 or 33 ? 3 : 1;
        if (m is not (0 or 1 or 32 or 33))
            return Unknown("ESC *", m, 2);
        var length = 5 + columns * bytesPerColumn;
        if (Avail < length)
            return -1;
        var stretch = m is 0 or 32 ? 2 : 1; // single density is half the horizontal resolution
        var height = bytesPerColumn * 8;
        var bitmap = new MonoBitmap(Math.Max(1, columns * stretch), height);
        for (var c = 0; c < columns; c++)
        for (var row = 0; row < height; row++)
        {
            var data = At(5 + c * bytesPerColumn + row / 8);
            if ((data & (0x80 >> (row % 8))) == 0)
                continue;
            for (var s = 0; s < stretch; s++)
                bitmap[c * stretch + s, row] = true;
        }
        if (columns > 0)
            AddItem(new Item(bitmap, bitmap.Width, height, false, 0));
        return length;
    }

    private int ParseGsFunction()
    {
        // GS ( x pL pH data: QR (x = k) and other function groups, all with a length prefix
        if (Avail < 5)
            return -1;
        var length = At(3) | (At(4) << 8);
        var total = 5 + length;
        if (Avail < total)
            return -1;
        if (At(2) == (byte)'k' && length >= 2 && At(5) == 49)
            QrFunction(At(6), 7, length - 2);
        else if (At(2) == (byte)'k' && length >= 2 && At(5) is 48 or 53 or 54)
            SymbolFunction(At(5), At(6), 7, length - 2);
        else if (At(2) != (byte)'k')
            Unknown("GS (", At(2), 0);
        return total;
    }

    private void QrFunction(int function, int offset, int count)
    {
        switch (function)
        {
            case 67 when count >= 1: // module size
                _qrModule = At(offset);
                break;
            case 69 when count >= 1: // error correction
                _qrEcc = At(offset);
                break;
            case 80 when count >= 1: // store the data (m = 48, then the text)
                _qrData = CodePages.Decode(_buffer.AsSpan(_start + offset + 1, Math.Max(0, count - 1)), _table);
                break;
            case 81 when Overflowed:
                break;
            case 81: // print what is stored
                if (_qrData.Length == 0)
                    break;
                if (Codes.Qr(_qrData, _qrModule, _qrEcc, _width) is { } qr)
                    AddBlock(qr);
                else
                    _log?.Invoke("ESC/POS: QR code does not fit in the paper width or is too long, not printed");
                break;
        }
    }

    private readonly Dictionary<int, (string Data, int Module)> _symbols = [];

    /// <summary>
    /// PDF417 (cn 48), Aztec (53) and Data Matrix (54) of <c>GS ( k</c>: module size (67), data (80) and print (81);
    /// the shape parameters (columns, rows, error correction) are left to the encoder.
    /// </summary>
    private void SymbolFunction(int cn, int function, int offset, int count)
    {
        var (data, module) = _symbols.GetValueOrDefault(cn, ("", 3));
        switch (function)
        {
            case 67 when count >= 1:
                _symbols[cn] = (data, At(offset));
                break;
            case 80 when count >= 1:
                _symbols[cn] = (CodePages.Decode(_buffer.AsSpan(_start + offset + 1, Math.Max(0, count - 1)), _table), module);
                break;
            case 81 when !Overflowed && data.Length > 0:
                if (Codes.Symbol(cn, data, module, _width) is { } symbol)
                    AddBlock(symbol);
                else
                    _log?.Invoke($"ESC/POS: 2D code (type {cn}) does not fit in the paper width or cannot be encoded, not printed");
                break;
        }
    }


    private int ParseBarcode()
    {
        // GS k m d1..dk NUL (m 0-6) or GS k m n d1..dn (m 65-73)
        if (Avail < 3)
            return -1;
        var m = At(2);
        string data;
        int length;
        if (m <= 6)
        {
            var end = -1;
            for (var i = 3; i < Math.Min(Avail, 260); i++)
                if (At(i) == 0)
                {
                    end = i;
                    break;
                }
            if (end < 0)
                return Avail >= 260 ? Unknown("GS k", m, 3) : -1;
            data = Encoding.Latin1.GetString(_buffer, _start + 3, end - 3);
            length = end + 1;
        }
        else if (m is >= 65 and <= 73)
        {
            if (Avail < 4)
                return -1;
            var count = At(3);
            length = 4 + count;
            if (Avail < length)
                return -1;
            data = Encoding.Latin1.GetString(_buffer, _start + 4, count);
        }
        else
        {
            return Unknown("GS k", m, 3);
        }

        if (Overflowed)
            return length;
        var format = Codes.FormatOf(m)!;
        if (Codes.Barcode(format, data, _barWidth, _barHeight, _width, out var text) is { } bars)
            AddBarcode(bars, text);
        else
            _log?.Invoke($"ESC/POS: barcode {format} '{data}' is not valid or does not fit, not printed");
        return length;
    }

    private void AddBarcode(MonoBitmap bars, string text)
    {
        var parts = new List<MonoBitmap>();
        if ((_barText & 1) != 0 && TextLine(text, bars.Width) is { } above)
            parts.Add(above);
        parts.Add(bars);
        if ((_barText & 2) != 0 && TextLine(text, bars.Width) is { } below)
            parts.Add(below);
        var height = parts.Sum(p => p.Height) + 4;
        var block = new MonoBitmap(parts.Max(p => p.Width), height);
        var y = 2;
        foreach (var part in parts)
        {
            var left = (block.Width - part.Width) / 2;
            for (var row = 0; row < part.Height; row++)
            for (var x = 0; x < part.Width; x++)
                if (part[x, row])
                    block[left + x, y + row] = true;
            y += part.Height;
        }
        AddBlock(block);
    }

    /// <summary>The human-readable line of a barcode in the current font, or null if it would be wider than the bars allow.</summary>
    private MonoBitmap? TextLine(string text, int minWidth)
    {
        var cellW = CellFont.CellWidth(_fontB);
        var cellH = CellFont.CellHeight(_fontB);
        var width = Math.Max(minWidth, Math.Min(text.Length * cellW, _width));
        var line = new MonoBitmap(width, cellH);
        var left = Math.Max(0, (width - text.Length * cellW) / 2);
        var x = left;
        foreach (var rune in text.EnumerateRunes())
        {
            if (x + cellW > width)
                break;
            if (CellFont.Glyph(rune, _bold, _fontB, TextRegion.Japanese).Bitmap is { } glyph)
                for (var yy = 0; yy < glyph.Height; yy++)
                for (var xx = 0; xx < glyph.Width; xx++)
                    if (glyph[xx, yy])
                        line[x + xx, yy] = true;
            x += cellW;
        }
        return line;
    }

    // ---- status ---------------------------------------------------------------------------------------

    private void Reply(int n)
    {
        var status = _status() ?? default;
        byte value = n switch
        {
            1 => 0x12,
            2 => status.CoverOpen ? (byte)0x32 : (byte)0x12,
            3 => status.Error || status.PaperOut || status.CoverOpen ? (byte)0x72 : (byte)0x12,
            4 => status.PaperOut ? (byte)0x72 : (byte)0x12,
            _ => 0,
        };
        if (n is >= 1 and <= 4)
            _responses.Add(value);
    }

    private void ReplyGsR(int n)
    {
        var status = _status() ?? default;
        if (n is 1 or 49)
            _responses.Add(status.PaperOut ? (byte)0x0C : (byte)0x00);
        else if (n is 2 or 50)
            _responses.Add(0x00);
    }

    private readonly record struct Item(MonoBitmap? Bitmap, int Advance, int Height, bool Inverse, int Underline);
}
