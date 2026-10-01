namespace MiniPrinter.Protocol;

/// <summary>
/// Incremental decoder for printer→host bytes. Tolerates packets split across reads or
/// several packets in one read; skips garbage and packets with a bad CRC or terminator.
/// </summary>
public sealed class FrameDecoder
{
    private readonly List<byte> _buffer = new(256);

    /// <summary>Number of rejected packets (bad CRC or terminator) since creation.</summary>
    public int RejectedCount { get; private set; }

    public IReadOnlyList<Frame> Feed(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            _buffer.Add(b);

        var frames = new List<Frame>();
        while (true)
        {
            var start = FindPrefix();
            if (start < 0)
            {
                // Keep a trailing 0x51 in case the next read starts with 0x78.
                var keep = _buffer.Count > 0 && _buffer[^1] == Frame.Prefix0 ? 1 : 0;
                _buffer.RemoveRange(0, _buffer.Count - keep);
                break;
            }
            if (start > 0)
                _buffer.RemoveRange(0, start);
            if (_buffer.Count < Frame.HeaderLength)
                break;

            var length = _buffer[4] | (_buffer[5] << 8);
            var total = Frame.Overhead + length;
            if (_buffer.Count < total)
                break;

            var payload = _buffer.GetRange(Frame.HeaderLength, length).ToArray();
            if (_buffer[total - 1] != Frame.Terminator || _buffer[total - 2] != Crc8.Compute(payload))
            {
                RejectedCount++;
                _buffer.RemoveAt(0);
                continue;
            }

            frames.Add(new Frame(_buffer[2], _buffer[3], payload));
            _buffer.RemoveRange(0, total);
        }
        return frames;
    }

    private int FindPrefix()
    {
        for (var i = 0; i + 1 < _buffer.Count; i++)
        {
            if (_buffer[i] == Frame.Prefix0 && _buffer[i + 1] == Frame.Prefix1)
                return i;
        }
        return -1;
    }
}
