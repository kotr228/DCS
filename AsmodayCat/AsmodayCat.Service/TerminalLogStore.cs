using AsmodayCat.Shared.Models;

namespace AsmodayCat.Service;

public class TerminalLogStore
{
    private readonly List<LogEntryDto> _entries = new(1024);
    private readonly object _lock = new();
    private const int MaxEntries = 1000;

    public int Count { get { lock (_lock) return _entries.Count; } }

    public void Add(LogEntryDto entry)
    {
        lock (_lock)
        {
            if (_entries.Count >= MaxEntries)
                _entries.RemoveAt(0);
            _entries.Add(entry);
        }
    }

    public LogBatchDto GetFrom(int offset)
    {
        lock (_lock)
        {
            if (offset >= _entries.Count)
                return new LogBatchDto { Entries = [], NextOffset = _entries.Count };

            return new LogBatchDto
            {
                Entries    = _entries.Skip(offset).ToList(),
                NextOffset = _entries.Count
            };
        }
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
    }
}
