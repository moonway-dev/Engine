namespace Engine.Scripting;

public static class ScriptTime
{
    public static float DeltaTime { get; internal set; } = 1f / 60f;

    public static float UnscaledDeltaTime { get; internal set; } = 1f / 60f;

    public static float TotalTime { get; internal set; }

    public static float FixedDeltaTime { get; internal set; } = 1f / 60f;
}
