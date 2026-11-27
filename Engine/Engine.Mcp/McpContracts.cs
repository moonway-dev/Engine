using System.Text.Json;

namespace Engine.Mcp;

public sealed record McpVec3(float X, float Y, float Z);

public sealed record McpQuat(float X, float Y, float Z, float W);

public sealed record McpTransform(
    McpVec3 LocalPosition,
    McpQuat LocalRotation,
    McpVec3 LocalScale,
    McpVec3 WorldPosition,
    McpQuat WorldRotation,
    McpVec3 EulerDegrees);

public sealed record McpObjectSummary(
    Guid Id,
    string Name,
    Guid? ParentId,
    bool Active,
    IReadOnlyList<string> ComponentTypes,
    IReadOnlyList<Guid> ChildIds);

public sealed record McpComponentInfo(Guid Id, string Type, IReadOnlyDictionary<string, object?> Properties);

public sealed record McpObjectDetail(
    Guid Id,
    string Name,
    Guid? ParentId,
    bool Active,
    McpTransform Transform,
    IReadOnlyList<McpComponentInfo> Components,
    IReadOnlyList<Guid> ChildIds);

public sealed record McpSceneInfo(
    string SceneName,
    string? ScenePath,
    bool Dirty,
    string PlayState,
    int ObjectCount,
    int ComponentCount,
    IReadOnlyDictionary<string, object?> Settings,
    string? ProjectRoot);

public sealed record McpLogEntry(DateTimeOffset Timestamp, string Level, string Category, string Message);

public interface IMcpEngineBridge
{
    McpSceneInfo GetEngineInfo();
    IReadOnlyList<McpObjectSummary> ListObjects(Guid? parentId);
    McpObjectDetail GetObject(Guid? id, string? name);
    McpObjectDetail CreateObject(string name, Guid? parentId, IReadOnlyList<string>? components, McpVec3? position, McpQuat? rotation, McpVec3? scale);
    bool DeleteObject(Guid id);
    bool RenameObject(Guid id, string name);
    bool ReparentObject(Guid id, Guid? parentId);
    McpObjectDetail DuplicateObject(Guid id);
    McpObjectDetail SetTransform(Guid id, McpVec3? position, McpQuat? rotation, McpVec3? eulerDegrees, McpVec3? scale, bool world);
    McpComponentInfo AddComponent(Guid objectId, string componentKey);
    bool RemoveComponent(Guid componentId);
    object? GetComponentProperty(Guid componentId, string property);
    void SetComponentProperty(Guid componentId, string property, JsonElement value);
    IReadOnlyList<string> ListComponentTypes();
    IReadOnlyDictionary<string, object?> GetSceneSettings();
    void SetSceneSettings(IReadOnlyDictionary<string, JsonElement> settings);
    string SaveScene(string? path);
    McpSceneInfo OpenScene(string path);
    McpSceneInfo NewScene(string name);
    string SetPlayState(string action);
    string Screenshot(string? path, string view);
    IReadOnlyList<McpLogEntry> GetLogs(int count, string? minLevel);
    IReadOnlyList<string> ListScripts();
    string ReadScript(string name);
    void WriteScript(string name, string content);
}
