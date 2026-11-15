using System.Text;

namespace Engine.Core;

public sealed class FileLogSink : ILogSink, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();
    private bool _disposed;

    public string FilePath { get; }

    public LogLevel MinimumLevel { get; }

    public FileLogSink(string directory, string? sessionName = null, LogLevel minimumLevel = LogLevel.Trace)
    {
        Directory.CreateDirectory(directory);
        string fileName = $"{sessionName ?? "session"}-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.log";
        FilePath = Path.Combine(directory, fileName);
        MinimumLevel = minimumLevel;
        FileStream stream = new(FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        _writer.WriteLine($"=== Engine session {DateTimeOffset.Now:O} ===");
    }

    public void Write(in LogEntry entry)
    {
        lock (_lock)
        {
            _writer.Write($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] [{entry.Category}] {entry.Message}");
            if (entry.Context is { Count: > 0 } context)
            {
                _writer.Write(" | ");
                bool first = true;
                foreach ((string key, object? value) in context)
                {
                    if (!first)
                        _writer.Write(", ");
                    _writer.Write($"{key}={value}");
                    first = false;
                }
            }
            if (entry.Exception is not null)
            {
                _writer.WriteLine();
                _writer.Write(entry.Exception.ToString());
            }
            _writer.WriteLine();
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            if (!_disposed)
                _writer.Flush();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _writer.Flush();
            _writer.Dispose();
        }
    }
}
