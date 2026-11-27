using System.IO;
using System.Text.Json;
using Engine.Mcp;
using Xunit;

namespace Engine.Tests.Mcp;

public sealed class McpToolTests : IDisposable
{
    private readonly string _scriptsRoot = Path.Combine(Path.GetTempPath(), "EngineMcpTests", Guid.NewGuid().ToString("N"));
    private readonly SceneBridge _bridge;

    public McpToolTests() => _bridge = new SceneBridge(_scriptsRoot);

    public void Dispose()
    {
        string root = Path.GetDirectoryName(_scriptsRoot);
        if (root is not null && Directory.Exists(root))
            Directory.Delete(root, true);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private JsonElement Args(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static string ToJson(object? value) => JsonSerializer.Serialize(value, JsonOptions);

    [Fact]
    public void CreateObjectAddsObjectAndComponents()
    {
        object? result = McpTools.Call(_bridge, "create_object", Args("""{"name":"Sun","components":["Light"]}"""));
        string json = ToJson(result);
        Assert.Contains("\"name\":\"Sun\"", json);
        Assert.Equal(1, _bridge.Scene.AllObjects.Count);
        Assert.Single(_bridge.Scene.AllObjects.First().Components);
    }

    [Fact]
    public void ListAndGetObjectRoundTrip()
    {
        Engine.Mcp.McpObjectDetail created = (Engine.Mcp.McpObjectDetail)McpTools.Call(_bridge, "create_object", Args("""{"name":"Car","position":{"x":1,"y":2,"z":3}}"""))!;
        Guid id = created.Id;
        IReadOnlyList<Engine.Mcp.McpObjectSummary> listed = (IReadOnlyList<Engine.Mcp.McpObjectSummary>)McpTools.Call(_bridge, "list_objects", Args("{}"))!;
        Assert.Single(listed);
        Engine.Mcp.McpObjectDetail fetched = (Engine.Mcp.McpObjectDetail)McpTools.Call(_bridge, "get_object", Args(JsonSerializer.Serialize(new { id = id.ToString() })))!;
        Assert.Equal("Car", fetched.Name);
        Assert.Equal(1f, fetched.Transform.LocalPosition.X);
        Engine.Mcp.McpObjectDetail byName = (Engine.Mcp.McpObjectDetail)McpTools.Call(_bridge, "get_object", Args("""{"name":"Car"}"""))!;
        Assert.Equal(id, byName.Id);
    }

    [Fact]
    public void SetTransformAcceptsEulerDegrees()
    {
        Guid id = ((Engine.Mcp.McpObjectDetail)McpTools.Call(_bridge, "create_object", Args("""{"name":"Object"}""")))!.Id;
        McpTools.Call(_bridge, "set_transform", Args(JsonSerializer.Serialize(new
        {
            id = id.ToString(),
            rotation_euler_degrees = new { x = 0f, y = 90f, z = 0f },
            scale = new { x = 2f, y = 2f, z = 2f },
        })));
        var rotation = _bridge.Scene.FindByName("Object")!.Transform.LocalRotation;
        Assert.Equal(0f, rotation.X, 3);
        Assert.Equal(0.7071f, rotation.Y, 3);
        Assert.Equal(0f, rotation.Z, 3);
        Assert.Equal(0.7071f, rotation.W, 3);
        Assert.Equal(2f, _bridge.Scene.FindByName("Object")!.Transform.LocalScale.Y);
    }

    [Fact]
    public void DeleteRenameAndReparentWorkThroughTools()
    {
        Guid parentId = ((Engine.Mcp.McpObjectDetail)McpTools.Call(_bridge, "create_object", Args("""{"name":"Parent"}""")))!.Id;
        Guid childId = ((Engine.Mcp.McpObjectDetail)McpTools.Call(_bridge, "create_object", Args("""{"name":"Child"}""")))!.Id;
        McpTools.Call(_bridge, "set_parent", Args(JsonSerializer.Serialize(new { id = childId.ToString(), parent_id = parentId.ToString() })));
        Assert.Same(_bridge.Scene.FindById(parentId), _bridge.Scene.FindById(childId)!.Parent);
        McpTools.Call(_bridge, "rename_object", Args(JsonSerializer.Serialize(new { id = childId.ToString(), name = "Renamed" })));
        Assert.Equal("Renamed", _bridge.Scene.FindById(childId)!.Name);
        McpTools.Call(_bridge, "delete_object", Args(JsonSerializer.Serialize(new { id = parentId.ToString() })));
        Assert.Null(_bridge.Scene.FindById(childId));
    }

    [Fact]
    public void SceneSettingsAndPlayStateToolsWork()
    {
        McpTools.Call(_bridge, "set_scene_settings", Args("""{"settings":{"clouds_enabled":true,"cloud_coverage":0.8}}"""));
        object? settings = McpTools.Call(_bridge, "get_scene_settings", Args("{}"));
        string json = JsonSerializer.Serialize(settings);
        Assert.Contains("true", json);
        Assert.Equal("Playing", McpTools.Call(_bridge, "set_play_state", Args("""{"action":"play"}""")));
    }

    [Fact]
    public void SaveSceneWritesLoadableFile()
    {
        McpTools.Call(_bridge, "create_object", Args("""{"name":"Saved"}"""));
        string path = Path.Combine(Path.GetTempPath(), $"mcp-save-{Guid.NewGuid():N}.scene");
        string saved = (string)McpTools.Call(_bridge, "save_scene", Args(JsonSerializer.Serialize(new { path })))!;
        Assert.True(File.Exists(saved));
        Engine.Mcp.McpSceneInfo opened = (Engine.Mcp.McpSceneInfo)McpTools.Call(_bridge, "open_scene", Args(JsonSerializer.Serialize(new { path = saved })))!;
        Assert.Equal(1, opened.ObjectCount);
        File.Delete(saved);
    }

    [Fact]
    public void ScriptToolsRoundTripAndRejectEscapes()
    {
        Directory.CreateDirectory(_scriptsRoot);
        McpTools.Call(_bridge, "write_script", Args("""{"name":"Motor.cs","content":"class Motor {}"}"""));
        Assert.Equal("class Motor {}", McpTools.Call(_bridge, "read_script", Args("""{"name":"Motor.cs"}""")));
        Assert.Contains("Motor.cs", (IReadOnlyList<string>)McpTools.Call(_bridge, "list_scripts", Args("{}"))!);
        Assert.ThrowsAny<Exception>(() => McpTools.Call(_bridge, "write_script", Args("""{"name":"../escape.cs","content":"x"}""")));
    }

    [Fact]
    public void ToolErrorsReportIsError()
    {
        Assert.Throws<KeyNotFoundException>(() => McpTools.Call(_bridge, "get_object", Args("""{"name":"Missing"}""")));
        Assert.Throws<ArgumentException>(() => McpTools.Call(_bridge, "get_object", Args("{}")));
        Assert.Throws<KeyNotFoundException>(() => McpTools.Call(_bridge, "delete_object", Args(JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString() }))));
        Assert.Throws<KeyNotFoundException>(() => McpTools.Call(_bridge, "unknown_tool", Args("{}")));
    }
}
