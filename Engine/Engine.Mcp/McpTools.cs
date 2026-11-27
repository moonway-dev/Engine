using System.Text.Json;

namespace Engine.Mcp;

public sealed record McpTool(
    string Name,
    string Description,
    string InputSchema,
    Func<IMcpEngineBridge, JsonElement, object?> Handler);

public static class McpTools
{
    public static IReadOnlyList<McpTool> All { get; } = Build();

    public static McpTool? Find(string name) => All.FirstOrDefault(tool => tool.Name == name);

    public static object? Call(IMcpEngineBridge bridge, string name, JsonElement arguments)
    {
        McpTool? tool = Find(name) ?? throw new KeyNotFoundException($"Unknown tool '{name}'.");
        return tool.Handler(bridge, arguments);
    }

    private static McpVec3? Vec3(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Object)
            return null;
        return new McpVec3(Req(value, "x").GetSingle(), Req(value, "y").GetSingle(), Req(value, "z").GetSingle());
    }

    private static McpQuat? Quat(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Object)
            return null;
        return new McpQuat(Req(value, "x").GetSingle(), Req(value, "y").GetSingle(), Req(value, "z").GetSingle(), Req(value, "w").GetSingle());
    }

    private static JsonElement Req(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement item) ? item
            : throw new ArgumentException($"Missing '{name}' in '{value}'.");

    private static Guid? OptGuid(JsonElement args, string name) =>
        args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? Guid.Parse(text)
            : null;

    private static string? OptString(JsonElement args, string name) =>
        args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Str(JsonElement args, string name) =>
        args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new ArgumentException($"'{name}' is required.");

    private static IReadOnlyList<string>? OptList(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            return null;
        return value.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
    }

    private static int OptInt(JsonElement args, string name, int fallback) =>
        args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : fallback;

    private static bool OptBool(JsonElement args, string name, bool fallback)
    {
        if (!args.TryGetProperty(name, out JsonElement value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return fallback;
        return value.GetBoolean();
    }

    private static IReadOnlyList<McpTool> Build() =>
    [
        new("engine_info", "Returns project root, active scene name and path, dirty state, play state, object and component counts, and scene settings.",
            """{"type":"object","properties":{},"additionalProperties":false}""",
            (bridge, _) => bridge.GetEngineInfo()),
        new("list_objects", "Lists every GameObject in the active scene as a flat list with ids, names, parents, component types and children. Use get_object for details.",
            """{"type":"object","properties":{"parent_id":{"type":"string","description":"Optional parent GUID; only direct children are returned."}},"additionalProperties":false}""",
            (bridge, args) => bridge.ListObjects(OptGuid(args, "parent_id"))),
        new("get_object", "Returns one GameObject by id or exact name: transform (local, world, Euler degrees in degrees), components with all properties, child ids.",
            """{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"}},"additionalProperties":false}""",
            (bridge, args) => bridge.GetObject(OptGuid(args, "id"), OptString(args, "name"))),
        new("create_object", "Creates a GameObject, optionally under a parent, with optional component keys and optional local position, quaternion rotation and scale.",
            """{"type":"object","properties":{"name":{"type":"string"},"parent_id":{"type":"string"},"components":{"type":"array","items":{"type":"string"}},"position":{"type":"object","properties":{"x":{"type":"number"},"y":{"type":"number"},"z":{"type":"number"}},"required":["x","y","z"],"additionalProperties":false},"rotation":{"type":"object","properties":{"x":{"type":"number"},"y":{"type":"number"},"z":{"type":"number"},"w":{"type":"number"}},"required":["x","y","z","w"],"additionalProperties":false},"scale":{"type":"object","properties":{"x":{"type":"number"},"y":{"type":"number"},"z":{"type":"number"}},"required":["x","y","z"],"additionalProperties":false}},"required":["name"],"additionalProperties":false}""",
            (bridge, args) => bridge.CreateObject(Str(args, "name"), OptGuid(args, "parent_id"), OptList(args, "components"), Vec3(args, "position"), Quat(args, "rotation"), Vec3(args, "scale"))),
        new("delete_object", "Deletes a GameObject and its subtree.",
            """{"type":"object","properties":{"id":{"type":"string"}},"required":["id"],"additionalProperties":false}""",
            (bridge, args) => bridge.DeleteObject(OptGuid(args, "id") ?? throw new ArgumentException("'id' is required."))),
        new("rename_object", "Renames a GameObject.",
            """{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"}},"required":["id","name"],"additionalProperties":false}""",
            (bridge, args) => bridge.RenameObject(OptGuid(args, "id") ?? throw new ArgumentException("'id' is required."), Str(args, "name"))),
        new("set_parent", "Moves a GameObject under another parent; null parent puts it at the scene root.",
            """{"type":"object","properties":{"id":{"type":"string"},"parent_id":{"type":["string","null"]}},"required":["id"],"additionalProperties":false}""",
            (bridge, args) => bridge.ReparentObject(OptGuid(args, "id") ?? throw new ArgumentException("'id' is required."), OptGuid(args, "parent_id"))),
        new("duplicate_object", "Duplicates a GameObject subtree with new ids.",
            """{"type":"object","properties":{"id":{"type":"string"}},"required":["id"],"additionalProperties":false}""",
            (bridge, args) => bridge.DuplicateObject(OptGuid(args, "id") ?? throw new ArgumentException("'id' is required."))),
        new("set_transform", "Sets an object transform: position, quaternion rotation and/or scale. Rotation may instead be given as Euler degrees (x,y,z). World=true transforms in world space.",
            """{"type":"object","properties":{"id":{"type":"string"},"position":{"type":"object","properties":{"x":{"type":"number"},"y":{"type":"number"},"z":{"type":"number"}},"required":["x","y","z"],"additionalProperties":false},"rotation":{"type":"object","properties":{"x":{"type":"number"},"y":{"type":"number"},"z":{"type":"number"},"w":{"type":"number"}},"required":["x","y","z","w"],"additionalProperties":false},"rotation_euler_degrees":{"type":"object","properties":{"x":{"type":"number"},"y":{"type":"number"},"z":{"type":"number"}},"required":["x","y","z"],"additionalProperties":false},"scale":{"type":"object","properties":{"x":{"type":"number"},"y":{"type":"number"},"z":{"type":"number"}},"required":["x","y","z"],"additionalProperties":false},"world":{"type":"boolean"}},"required":["id"],"additionalProperties":false}""",
            (bridge, args) => bridge.SetTransform(OptGuid(args, "id") ?? throw new ArgumentException("'id' is required."), Vec3(args, "position"), Quat(args, "rotation"), Vec3(args, "rotation_euler_degrees"), Vec3(args, "scale"), OptBool(args, "world", false))),
        new("add_component", "Attaches a component by registry key (for example MeshRenderer, Light, Camera, RigidBody).",
            """{"type":"object","properties":{"id":{"type":"string"},"component":{"type":"string"}},"required":["id","component"],"additionalProperties":false}""",
            (bridge, args) => bridge.AddComponent(OptGuid(args, "id") ?? throw new ArgumentException("'id' is required."), Str(args, "component"))),
        new("remove_component", "Detaches a component by its component id.",
            """{"type":"object","properties":{"component_id":{"type":"string"}},"required":["component_id"],"additionalProperties":false}""",
            (bridge, args) => bridge.RemoveComponent(OptGuid(args, "component_id") ?? throw new ArgumentException("'component_id' is required."))),
        new("get_component_property", "Returns one property of a component as JSON.",
            """{"type":"object","properties":{"component_id":{"type":"string"},"property":{"type":"string"}},"required":["component_id","property"],"additionalProperties":false}""",
            (bridge, args) => bridge.GetComponentProperty(OptGuid(args, "component_id") ?? throw new ArgumentException("'component_id' is required."), Str(args, "property"))),
        new("set_component_property", "Sets one property of a component. Vectors are {x,y,z(,w)}, AssetReference is a path string, enums are names.",
            """{"type":"object","properties":{"component_id":{"type":"string"},"property":{"type":"string"},"value":{}},"required":["component_id","property","value"],"additionalProperties":false}""",
            (bridge, args) =>
            {
                bridge.SetComponentProperty(OptGuid(args, "component_id") ?? throw new ArgumentException("'component_id' is required."), Str(args, "property"), args.GetProperty("value"));
                return "ok";
            }),
        new("list_component_types", "Lists component registry keys attachable to GameObjects.",
            """{"type":"object","properties":{},"additionalProperties":false}""",
            (bridge, _) => bridge.ListComponentTypes()),
        new("get_scene_settings", "Returns scene-level settings: sky mode, sky color, sun direction, clouds.",
            """{"type":"object","properties":{},"additionalProperties":false}""",
            (bridge, _) => bridge.GetSceneSettings()),
        new("set_scene_settings", "Sets scene-level settings: skybox_mode (SolidColor|Procedural), sky_color {r,g,b,a}, sun_direction {x,y,z}, clouds_enabled, cloud_preset, cloud_coverage, cloud_density, cloud_wind_speed.",
            """{"type":"object","properties":{"settings":{"type":"object"}},"required":["settings"],"additionalProperties":false}""",
            (bridge, args) =>
            {
                Dictionary<string, JsonElement> settings = [];
                foreach (JsonProperty property in args.GetProperty("settings").EnumerateObject())
                    settings.Add(property.Name, property.Value);
                bridge.SetSceneSettings(settings);
                return bridge.GetSceneSettings();
            }),
        new("save_scene", "Saves the active scene to its current path or to the given .scene path.",
            """{"type":"object","properties":{"path":{"type":"string"}},"additionalProperties":false}""",
            (bridge, args) => bridge.SaveScene(OptString(args, "path"))),
        new("open_scene", "Loads a .scene file from disk and makes it active.",
            """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"],"additionalProperties":false}""",
            (bridge, args) => bridge.OpenScene(Str(args, "path"))),
        new("new_scene", "Replaces the active scene with an empty one.",
            """{"type":"object","properties":{"name":{"type":"string"}},"additionalProperties":false}""",
            (bridge, args) => bridge.NewScene(OptString(args, "name") ?? "Untitled")),
        new("set_play_state", "Controls play mode: play, stop, pause, resume, toggle, step.",
            """{"type":"object","properties":{"action":{"type":"string","enum":["play","stop","pause","resume","toggle","step"]}},"required":["action"],"additionalProperties":false}""",
            (bridge, args) => bridge.SetPlayState(Str(args, "action"))),
        new("screenshot", "Captures the scene or game view to a PNG file (bgra readback) and returns the absolute path.",
            """{"type":"object","properties":{"path":{"type":"string","description":"Optional .png path; defaults to the project Cache folder."},"view":{"type":"string","enum":["scene","game"]}},"additionalProperties":false}""",
            (bridge, args) => bridge.Screenshot(OptString(args, "path"), OptString(args, "view") ?? "game")),
        new("get_logs", "Returns recent editor log entries, newest last.",
            """{"type":"object","properties":{"count":{"type":"integer"},"min_level":{"type":"string","enum":["Trace","Debug","Information","Warning","Error","Critical"]}},"additionalProperties":false}""",
            (bridge, args) => bridge.GetLogs(OptInt(args, "count", 50), OptString(args, "min_level"))),
        new("list_scripts", "Lists C# script files under the project Scripts folder.",
            """{"type":"object","properties":{},"additionalProperties":false}""",
            (bridge, _) => bridge.ListScripts()),
        new("read_script", "Returns the content of one project script.",
            """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}""",
            (bridge, args) => bridge.ReadScript(Str(args, "name"))),
        new("write_script", "Creates or overwrites a project script with C# source.",
            """{"type":"object","properties":{"name":{"type":"string"},"content":{"type":"string"}},"required":["name","content"],"additionalProperties":false}""",
            (bridge, args) =>
            {
                bridge.WriteScript(Str(args, "name"), Str(args, "content"));
                return "ok";
            }),
    ];
}
