namespace Engine.Core;

public sealed class MemoryLogSink(LogLevel minimumLevel = LogLevel.Trace) : ILogSink
{
    private readonly object _lock = new();
    private readonly List<LogEntry> _entries = [];

    public LogLevel MinimumLevel { get; } = minimumLevel;

    public IReadOnlyList<LogEntry> GetEntries()
    {
        lock (_lock)
            return [.. _entries];
    }

    public void Clear()
    {
        lock (_lock)
            _entries.Clear();
    }

    public void Write(in LogEntry entry)
    {
        lock (_lock)
            _entries.Add(entry);
    }

    public void Flush()
    {
    }
}
