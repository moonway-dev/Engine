using Engine.Core;
using Xunit;

namespace Engine.Tests.Core;

public class CrashReportTests
{
    [Fact]
    public void Write_CreatesReportWithDiagnostics()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EngineTests", Guid.NewGuid().ToString("N"));
        try
        {
            Exception exception = new InvalidOperationException("engine exploded");
            string path = CrashReport.Write(directory, exception, "Frames rendered: 128");
            string content = File.ReadAllText(path);
            Assert.Contains("Engine Crash Report", content);
            Assert.Contains(EngineInfo.EngineVersion, content);
            Assert.Contains("engine exploded", content);
            Assert.Contains("Frames rendered: 128", content);
            Assert.Contains("InvalidOperationException", content);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
