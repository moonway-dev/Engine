using Engine.Core;
using Xunit;

namespace Engine.Tests.Core;

public class LoggingTests
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyContext = new Dictionary<string, object?>();

    [Fact]
    public void Factory_CreatesCachedLoggerPerCategory()
    {
        LoggerFactory factory = new(new MemoryLogSink());
        ILogger first = factory.CreateLogger("Test");
        ILogger second = factory.CreateLogger("Test");
        Assert.Same(first, second);
        Assert.Equal("Test", first.Category);
    }

    [Fact]
    public void Logger_WritesEntryWithMetadata()
    {
        MemoryLogSink sink = new();
        LoggerFactory factory = new(sink);
        ILogger log = factory.CreateLogger("TestCategory");
        log.Warning("Something odd happened");
        IReadOnlyList<LogEntry> entries = sink.GetEntries();
        LogEntry entry = Assert.Single(entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("TestCategory", entry.Category);
        Assert.Equal("Something odd happened", entry.Message);
        Assert.True(entry.Timestamp <= DateTimeOffset.Now);
    }

    [Fact]
    public void Logger_ExtensionMethods_SetLevel()
    {
        MemoryLogSink sink = new();
        ILogger log = new LoggerFactory(sink).CreateLogger("Levels");
        log.Trace("a");
        log.Debug("b");
        log.Information("c");
        log.Warning("d");
        log.Error("e");
        log.Critical("f");
        IReadOnlyList<LogEntry> entries = sink.GetEntries();
        Assert.Equal(
            new[] { LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical },
            entries.Select(e => e.Level).ToArray());
    }

    [Fact]
    public void Logger_MinimumLevel_FiltersEntries()
    {
        MemoryLogSink sink = new(LogLevel.Warning);
        ILogger log = new LoggerFactory(sink).CreateLogger("Filtered");
        log.Debug("hidden");
        log.Information("also hidden");
        log.Warning("visible");
        IReadOnlyList<LogEntry> entries = sink.GetEntries();
        LogEntry entry = Assert.Single(entries);
        Assert.Equal("visible", entry.Message);
    }

    [Fact]
    public void FileLogSink_WritesReadableText()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EngineTests", Guid.NewGuid().ToString("N"));
        try
        {
            using FileLogSink sink = new(directory, "unit");
            ILogger log = new LoggerFactory(sink).CreateLogger("File");
            log.Information("line with context", new Dictionary<string, object?> { ["value"] = 42 });
            sink.Flush();
            using FileStream stream = new(sink.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new(stream);
            string content = reader.ReadToEnd();
            Assert.Contains("[Information]", content);
            Assert.Contains("[File]", content);
            Assert.Contains("value=42", content);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Logger_Exception_IsPreserved()
    {
        MemoryLogSink sink = new();
        ILogger log = new LoggerFactory(sink).CreateLogger("Exception");
        Exception exception = new InvalidOperationException("boom");
        log.Error("failed", exception);
        Assert.Same(exception, Assert.Single(sink.GetEntries()).Exception);
    }
}
