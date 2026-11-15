namespace Engine.Core;

public interface ILogger
{
    string Category { get; }

    bool IsEnabled(LogLevel level);

    void Log(LogLevel level, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? context = null);
}
