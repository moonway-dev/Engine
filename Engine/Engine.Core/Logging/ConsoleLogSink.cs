namespace Engine.Core;

public sealed class ConsoleLogSink(LogLevel minimumLevel = LogLevel.Trace) : ILogSink
{
    private static readonly object ConsoleLock = new();

    public LogLevel MinimumLevel { get; } = minimumLevel;

    public void Write(in LogEntry entry)
    {
        lock (ConsoleLock)
        {
            string line = $"{entry.Timestamp:HH:mm:ss.fff} [{entry.Level}] [{entry.Category}] {entry.Message}";
            if (entry.Exception is not null)
                line += Environment.NewLine + entry.Exception;
            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = SelectColor(entry.Level);
            try
            {
                if (entry.Level >= LogLevel.Error)
                    Console.Error.WriteLine(line);
                else
                    Console.Out.WriteLine(line);
            }
            finally
            {
                Console.ForegroundColor = previous;
            }
        }
    }

    public void Flush()
    {
        lock (ConsoleLock)
        {
            Console.Out.Flush();
            Console.Error.Flush();
        }
    }

    private static ConsoleColor SelectColor(LogLevel level) => level switch
    {
        LogLevel.Trace => ConsoleColor.DarkGray,
        LogLevel.Debug => ConsoleColor.Gray,
        LogLevel.Information => ConsoleColor.White,
        LogLevel.Warning => ConsoleColor.Yellow,
        LogLevel.Error => ConsoleColor.Red,
        LogLevel.Critical => ConsoleColor.Magenta,
        _ => ConsoleColor.White,
    };
}
