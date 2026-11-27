using System.IO;
using Engine.Mathematics;
using Engine.Scene;
using SceneModel = Engine.Scene.Scene;
using JsonElement = System.Text.Json.JsonElement;
using McpVec3 = Engine.Mcp.McpVec3;
using McpQuat = Engine.Mcp.McpQuat;

namespace Engine.Tests.Mcp;

public sealed class SceneBridge(string scriptsRoot) : Engine.Mcp.IMcpEngineBridge
{
    private readonly Scene.Scene _scene = new("TestScene");
    private readonly List<string> _logs = [];
    private string? _path;
    private string _playState = "Stopped";

    public Scene.Scene Scene => _scene;

    public Engine.Mcp.McpSceneInfo GetEngineInfo()
    {
        Dictionary<string, object?> settings = new() { ["clouds_enabled"] = _scene.CloudsEnabled };
        return new Engine.Mcp.McpSceneInfo(_scene.Name, _path, false, _playState, _scene.AllObjects.Count, _scene.AllComponents.Count, settings, null);
    }

    public IReadOnlyList<Engine.Mcp.McpObjectSummary> ListObjects(Guid? parentId)
    {
        if (parentId is Guid id)
        {
            GameObject? parent = _scene.FindById(id) ?? throw new KeyNotFoundException($"No object with id '{id}'.");
            return parent.Children.Select(Summarize).ToList();
        }
        return _scene.EnumerateHierarchy().Select(Summarize).ToList();
    }

    public Engine.Mcp.McpObjectDetail GetObject(Guid? id, string? name)
    {
        GameObject? target = id is Guid guid ? _scene.FindById(guid)
            : name is not null ? _scene.FindByName(name)
            : throw new ArgumentException("Provide 'id' or 'name'.");
        return target is null ? throw new KeyNotFoundException("No such object.") : Detail(target);
    }

    public Engine.Mcp.McpObjectDetail CreateObject(string name, Guid? parentId, IReadOnlyList<string>? components, McpVec3? position, McpQuat? rotation, McpVec3? scale)
    {
        GameObject? parent = parentId is Guid id ? _scene.FindById(id) : null;
        GameObject created = _scene.CreateObject(name, parent);
        foreach (string key in components ?? [])
            created.AddComponent(key);
        Apply(created, position, rotation, scale);
        return Detail(created);
    }

    public bool DeleteObject(Guid id)
    {
        GameObject? target = _scene.FindById(id) ?? throw new KeyNotFoundException($"No object with id '{id}'.");
        _scene.Remove(target);
        return true;
    }

    public bool RenameObject(Guid id, string name)
    {
        GameObject? target = _scene.FindById(id) ?? throw new KeyNotFoundException($"No object with id '{id}'.");
        target.Name = name;
        return true;
    }

    public bool ReparentObject(Guid id, Guid? parentId)
    {
        GameObject? target = _scene.FindById(id) ?? throw new KeyNotFoundException($"No object with id '{id}'.");
        GameObject? parent = parentId is Guid guid ? _scene.FindById(guid) : null;
        target.SetParent(parent);
        return true;
    }

    public Engine.Mcp.McpObjectDetail DuplicateObject(Guid id)
    {
        GameObject? target = _scene.FindById(id) ?? throw new KeyNotFoundException($"No object with id '{id}'.");
        return Detail(target.Clone(CloneIdPolicy.GenerateNewIds, target.Parent));
    }

    public Engine.Mcp.McpObjectDetail SetTransform(Guid id, McpVec3? position, McpQuat? rotation, McpVec3? eulerDegrees, McpVec3? scale, bool world)
    {
        GameObject? target = _scene.FindById(id) ?? throw new KeyNotFoundException($"No object with id '{id}'.");
        if (rotation is not null)
            target.Transform.LocalRotation = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W).Normalized;
        if (eulerDegrees is not null)
            target.Transform.LocalRotation = Quaternion.FromYawPitchRoll(
                eulerDegrees.Y * MathUtil.DegToRad,
                eulerDegrees.X * MathUtil.DegToRad,
                eulerDegrees.Z * MathUtil.DegToRad).Normalized;
        Apply(target, position, null, scale);
        return Detail(target);
    }

    public Engine.Mcp.McpComponentInfo AddComponent(Guid objectId, string componentKey)
    {
        GameObject? target = _scene.FindById(objectId) ?? throw new KeyNotFoundException($"No object with id '{objectId}'.");
        Component component = target.AddComponent(componentKey);
        return new Engine.Mcp.McpComponentInfo(component.Id, component.ComponentTypeName, new Dictionary<string, object?> { ["enabled"] = component.Enabled });
    }

    public bool RemoveComponent(Guid componentId)
    {
        Component? component = _scene.FindComponentById(componentId) ?? throw new KeyNotFoundException($"No component with id '{componentId}'.");
        return component.Owner.RemoveComponent(component);
    }

    public object? GetComponentProperty(Guid componentId, string property)
    {
        Component? component = _scene.FindComponentById(componentId) ?? throw new KeyNotFoundException($"No component with id '{componentId}'.");
        return property switch
        {
            "intensity" => ((Light)component).Intensity,
            "kind" => ((Light)component).Kind.ToString(),
            _ => throw new ArgumentException($"Unknown property '{property}'."),
        };
    }

    public void SetComponentProperty(Guid componentId, string property, JsonElement value)
    {
        Component? component = _scene.FindComponentById(componentId) ?? throw new KeyNotFoundException($"No component with id '{componentId}'.");
        if (component is Light light && property == "intensity")
            light.Intensity = value.GetSingle();
        else
            throw new ArgumentException($"Unknown property '{property}'.");
    }

    public IReadOnlyList<string> ListComponentTypes() => _scene.ComponentRegistry.RegisteredKeys.OrderBy(key => key).ToList();

    public IReadOnlyDictionary<string, object?> GetSceneSettings()
    {
        Dictionary<string, object?> settings = new()
        {
            ["skybox_mode"] = _scene.SkyboxMode.ToString(),
            ["clouds_enabled"] = _scene.CloudsEnabled,
            ["cloud_coverage"] = _scene.CloudCoverage,
        };
        return settings;
    }

    public void SetSceneSettings(IReadOnlyDictionary<string, JsonElement> settings)
    {
        foreach ((string key, JsonElement value) in settings)
        {
            if (key == "clouds_enabled")
                _scene.CloudsEnabled = value.GetBoolean();
            else if (key == "cloud_coverage")
                _scene.CloudCoverage = Math.Clamp(value.GetSingle(), 0f, 1f);
            else
                throw new ArgumentException($"Unknown scene setting '{key}'.");
        }
    }

    public string SaveScene(string? path)
    {
        _path = path ?? Path.Combine(Path.GetTempPath(), $"mcp-test-{Guid.NewGuid():N}.scene");
        _scene.Save(_path);
        return Path.GetFullPath(_path);
    }

    public Engine.Mcp.McpSceneInfo OpenScene(string path)
    {
        SceneModel loaded = SceneModel.Load(path);
        return new Engine.Mcp.McpSceneInfo(loaded.Name, Path.GetFullPath(path), false, _playState, loaded.AllObjects.Count, loaded.AllComponents.Count, GetSceneSettings(), null);
    }

    public Engine.Mcp.McpSceneInfo NewScene(string name) => GetEngineInfo();

    public string SetPlayState(string action)
    {
        _playState = action.Trim().ToLowerInvariant() switch
        {
            "play" => "Playing",
            "stop" => "Stopped",
            "pause" => "Paused",
            _ => throw new ArgumentException($"Unknown play action '{action}'."),
        };
        return _playState;
    }

    public string Screenshot(string? path, string view) => throw new NotSupportedException("Screenshots require a device.");

    public IReadOnlyList<Engine.Mcp.McpLogEntry> GetLogs(int count, string? minLevel) => [new Engine.Mcp.McpLogEntry(DateTimeOffset.UtcNow, "Information", "Test", "hello")];

    public IReadOnlyList<string> ListScripts()
    {
        if (!Directory.Exists(scriptsRoot))
            return [];
        return Directory.EnumerateFiles(scriptsRoot, "*.cs", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).OrderBy(name => name).ToList()!;
    }

    public string ReadScript(string name) => File.ReadAllText(Path.Combine(scriptsRoot, name));

    public void WriteScript(string name, string content)
    {
        string resolved = Path.GetFullPath(Path.Combine(scriptsRoot, name));
        if (!resolved.StartsWith(Path.GetFullPath(scriptsRoot), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Scripts must stay in the scripts root.");
        File.WriteAllText(resolved, content);
    }

    private static void Apply(GameObject target, McpVec3? position, McpQuat? rotation, McpVec3? scale)
    {
        if (position is not null)
            target.Transform.LocalPosition = new Vector3(position.X, position.Y, position.Z);
        if (scale is not null)
            target.Transform.LocalScale = new Vector3(scale.X, scale.Y, scale.Z);
    }

    private static Engine.Mcp.McpObjectSummary Summarize(GameObject gameObject) => new(
        gameObject.Id,
        gameObject.Name,
        gameObject.Parent?.Id,
        gameObject.Active,
        gameObject.Components.Select(component => component.ComponentTypeName).ToList(),
        gameObject.Children.Select(child => child.Id).ToList());

    private static Engine.Mcp.McpObjectDetail Detail(GameObject gameObject) => new(
        gameObject.Id,
        gameObject.Name,
        gameObject.Parent?.Id,
        gameObject.Active,
        new Engine.Mcp.McpTransform(
            new McpVec3(gameObject.Transform.LocalPosition.X, gameObject.Transform.LocalPosition.Y, gameObject.Transform.LocalPosition.Z),
            new McpQuat(0f, 0f, 0f, 1f),
            new McpVec3(gameObject.Transform.LocalScale.X, gameObject.Transform.LocalScale.Y, gameObject.Transform.LocalScale.Z),
            new McpVec3(gameObject.Transform.Position.X, gameObject.Transform.Position.Y, gameObject.Transform.Position.Z),
            new McpQuat(0f, 0f, 0f, 1f),
            new McpVec3(0f, 0f, 0f)),
        gameObject.Components.Select(component => new Engine.Mcp.McpComponentInfo(component.Id, component.ComponentTypeName, new Dictionary<string, object?>())).ToList(),
        gameObject.Children.Select(child => child.Id).ToList());
}
