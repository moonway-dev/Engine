using System.Text;

namespace Engine.Core;

public static class CrashReport
{
    public static string Write(string directory, Exception exception, string? extraInfo = null)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"crash-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.txt");
        StringBuilder builder = new();
        builder.AppendLine("Engine Crash Report");
        builder.AppendLine($"Engine Version: {EngineInfo.EngineVersion}");
        builder.AppendLine($"Time: {DateTimeOffset.Now:O}");
        builder.AppendLine($"OS: {EngineInfo.OperatingSystem}");
        builder.AppendLine($"Runtime: {EngineInfo.RuntimeDescription}");
        builder.AppendLine($"Process: {Environment.ProcessId} ({(EngineInfo.Is64BitProcess ? "x64" : "x86")})");
        if (!string.IsNullOrWhiteSpace(extraInfo))
        {
            builder.AppendLine();
            builder.AppendLine(extraInfo);
        }
        builder.AppendLine();
        builder.AppendLine(exception.ToString());
        File.WriteAllText(path, builder.ToString());
        return path;
    }
}
