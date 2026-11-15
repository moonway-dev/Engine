namespace Engine.Core;

public sealed class LoggerFactory
{
    private readonly List<ILogSink> _sinks;
    private readonly Dictionary<string, ILogger> _loggers = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public LoggerFactory(params ILogSink[] sinks)
    {
        if (sinks.Length == 0)
            throw new ArgumentException("At least one log sink is required.", nameof(sinks));
        _sinks = [.. sinks];
    }

    public ILogger CreateLogger(string category)
    {
        lock (_lock)
        {
            if (_loggers.TryGetValue(category, out ILogger? logger))
                return logger;
            logger = new CategoryLogger(category, this);
            _loggers[category] = logger;
            return logger;
        }
    }

    public ILogger CreateLogger<T>() => CreateLogger(typeof(T).Name);

    public void AddSink(ILogSink sink)
    {
        lock (_lock)
            _sinks.Add(sink);
    }

    public void RemoveSink(ILogSink sink)
    {
        lock (_lock)
            _sinks.Remove(sink);
    }

    public bool HasEnabledSink(LogLevel level)
    {
        lock (_lock)
        {
            foreach (ILogSink sink in _sinks)
            {
                if (level >= sink.MinimumLevel)
                    return true;
            }
        }
        return false;
    }

    public void Write(in LogEntry entry)
    {
        ILogSink[] sinks;
        lock (_lock)
            sinks = [.. _sinks];
        foreach (ILogSink sink in sinks)
        {
            if (entry.Level < sink.MinimumLevel)
                continue;
            try
            {
                sink.Write(entry);
            }
            catch (Exception sinkException)
            {
                Console.Error.WriteLine($"Log sink '{sink.GetType().Name}' failed: {sinkException.Message}");
            }
        }
    }

    public void Flush()
    {
        ILogSink[] sinks;
        lock (_lock)
            sinks = [.. _sinks];
        foreach (ILogSink sink in sinks)
        {
            try
            {
                sink.Flush();
            }
            catch (Exception sinkException)
            {
                Console.Error.WriteLine($"Log sink '{sink.GetType().Name}' failed to flush: {sinkException.Message}");
            }
        }
    }

    public void Dispose()
    {
        ILogSink[] sinks;
        lock (_lock)
            sinks = [.. _sinks];
        foreach (ILogSink sink in sinks)
        {
            try
            {
                (sink as IDisposable)?.Dispose();
            }
            catch (Exception sinkException)
            {
                Console.Error.WriteLine($"Log sink '{sink.GetType().Name}' failed to dispose: {sinkException.Message}");
            }
        }
    }

    private sealed class CategoryLogger(string category, LoggerFactory factory) : ILogger
    {
        public string Category { get; } = category;

        public bool IsEnabled(LogLevel level) => factory.HasEnabledSink(level);

        public void Log(LogLevel level, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? context = null)
        {
            if (!IsEnabled(level))
                return;
            factory.Write(new LogEntry(DateTimeOffset.Now, level, Category, message, exception, context));
        }
    }
}
