namespace Engine.Editor.Build;

public sealed class EngineBuildProfile
{
    public string ProductName = "Game";
    public string Version = "1.0.0";
    public List<string> Scenes = [];
    public string StartupScene = string.Empty;
    public string Platform = "win-x64";
    public string Mode = "Aot";
    public string Configuration = "Debug";
    public string OutputDirectory = "Builds";
    public string PackKey = string.Empty;
}

public sealed class BuildRequest
{
    public required string ProjectRoot { get; init; }
    public required string EngineRoot { get; init; }
    public required EngineBuildProfile Profile { get; init; }
    public Action<string>? Log { get; init; }
}

public sealed class BuildStepReport
{
    public required string Name { get; init; }
    public TimeSpan Duration { get; init; }
    public string Details { get; init; } = string.Empty;
}

public sealed class BuildReport
{
    public bool Success { get; init; }
    public TimeSpan Duration { get; init; }
    public string OutputDirectory { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public int FileCount { get; init; }
    public long TotalBytes { get; init; }
    public List<string> Warnings { get; init; } = [];
    public List<string> Errors { get; init; } = [];
    public List<BuildStepReport> Steps { get; init; } = [];

    public string Serialize()
    {
        System.Text.Json.JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            IncludeFields = true,
        };
        return System.Text.Json.JsonSerializer.Serialize(this, options);
    }
}
