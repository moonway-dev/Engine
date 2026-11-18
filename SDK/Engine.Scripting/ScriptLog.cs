namespace Engine.Scripting;

public static class ScriptLog
{
    public static Action<string, string>? Sink { get; set; }

    public static void Info(string source, string message) => Sink?.Invoke("Information", $"[Script] {source}: {message}");

    public static void Warning(string source, string message) => Sink?.Invoke("Warning", $"[Script] {source}: {message}");

    public static void Error(string source, string message) => Sink?.Invoke("Error", $"[Script] {source}: {message}");
}
