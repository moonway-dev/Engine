namespace Engine.Core;

public readonly record struct LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    Exception? Exception = null,
    IReadOnlyDictionary<string, object?>? Context = null);
