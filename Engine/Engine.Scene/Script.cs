namespace Engine.Scene;

using System.Text.Json;

public abstract class ScriptBase : Component
{
    public string Script { get; set; } = string.Empty;
}

public class Script : ScriptBase
{

    public object? Instance { get; internal set; }

    public bool HasInstance => Instance is not null;

    internal Dictionary<string, JsonElement>? PendingProperties { get; set; }

    protected internal override void OnAttach()
    {
        if (Instance is null)
            ScriptHost.AttachInstance(this);
    }

    protected internal override void OnDetach()
    {
        ScriptHost.DetachInstance(this);
    }

    public void RecreateInstance()
    {
        ScriptHost.DetachInstance(this);
        if (string.IsNullOrWhiteSpace(Script))
            return;
        ScriptHost.AttachInstance(this);
        if (PendingProperties is not null)
        {
            ApplyPending();
            PendingProperties = null;
        }
    }

    internal bool EnsureInstance()
    {
        if (Instance is not null)
            return true;
        if (string.IsNullOrWhiteSpace(Script))
            return false;
        ScriptHost.AttachInstance(this);
        return Instance is not null;
    }

    internal void ApplyPending()
    {
        if (PendingProperties is null || Instance is null)
            return;
        Dictionary<string, JsonElement> properties = PendingProperties;
        PendingProperties = null;
        SceneSerializer.ApplyInstanceProperties(Instance, properties, Owner?.Scene);
    }
}
