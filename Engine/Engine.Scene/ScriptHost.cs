namespace Engine.Scene;

public sealed record ScriptDescription(string Key, string DisplayName, Type ComponentType);

public static class ScriptHost
{
    public static Func<Script, object?>? InstanceFactory { get; set; }

    public static Action<Script, object>? InstanceReleaser { get; set; }

    public static Func<IReadOnlyList<ScriptDescription>>? ScriptsProvider { get; set; }

    public static IReadOnlyList<ScriptDescription> AvailableScripts => ScriptsProvider?.Invoke() ?? [];

    internal static void AttachInstance(Script component)
    {
        if (InstanceFactory is null || string.IsNullOrWhiteSpace(component.Script))
            return;
        component.Instance = InstanceFactory(component);
    }

    internal static void DetachInstance(Script component)
    {
        object? instance = component.Instance;
        component.Instance = null;
        if (instance is not null)
            InstanceReleaser?.Invoke(component, instance);
    }
}
