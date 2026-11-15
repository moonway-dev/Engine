namespace Engine.Core;

public static class LoggerExtensions
{
    public static void Trace(this ILogger logger, string message, IReadOnlyDictionary<string, object?>? context = null) =>
        logger.Log(LogLevel.Trace, message, null, context);

    public static void Debug(this ILogger logger, string message, IReadOnlyDictionary<string, object?>? context = null) =>
        logger.Log(LogLevel.Debug, message, null, context);

    public static void Information(this ILogger logger, string message, IReadOnlyDictionary<string, object?>? context = null) =>
        logger.Log(LogLevel.Information, message, null, context);

    public static void Warning(this ILogger logger, string message, IReadOnlyDictionary<string, object?>? context = null) =>
        logger.Log(LogLevel.Warning, message, null, context);

    public static void Error(this ILogger logger, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? context = null) =>
        logger.Log(LogLevel.Error, message, exception, context);

    public static void Critical(this ILogger logger, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? context = null) =>
        logger.Log(LogLevel.Critical, message, exception, context);
}
