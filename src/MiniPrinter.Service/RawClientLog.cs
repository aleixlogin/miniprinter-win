using MiniPrinter.Control;

namespace MiniPrinter.Service;

/// <summary>The latest clients of the RAW port, in memory only (nothing is written to disk and nothing survives a restart).</summary>
public sealed class RawClientLog(int capacity)
{
    private readonly Queue<RawClientDto> _entries = new();
    private readonly object _gate = new();

    public void Add(RawClientDto entry)
    {
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > capacity)
                _entries.Dequeue();
        }
    }

    /// <summary>The remembered clients, newest first.</summary>
    public IReadOnlyList<RawClientDto> Snapshot()
    {
        lock (_gate)
            return [.. _entries.Reverse()];
    }
}
