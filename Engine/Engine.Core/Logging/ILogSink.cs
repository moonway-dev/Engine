namespace Engine.Core;

public interface ILogSink
{
    LogLevel MinimumLevel { get; }

    void Write(in LogEntry entry);

    void Flush();
}
