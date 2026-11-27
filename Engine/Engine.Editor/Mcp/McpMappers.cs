using Engine.Mathematics;
using System.Text.Json;
using Engine.Scene;
using SceneModel = Engine.Scene.Scene;
using JsonElement = System.Text.Json.JsonElement;
using McpVec3 = Engine.Mcp.McpVec3;
using McpQuat = Engine.Mcp.McpQuat;

namespace Engine.Editor.Mcp;

internal static class McpMappers
{
    public static Engine.Mcp.McpObjectSummary Summarize(GameObject gameObject) => new(
        gameObject.Id,
        gameObject.Name,
        gameObject.Parent?.Id,
        gameObject.Active,
        gameObject.Components.Select(component => component.ComponentTypeName).ToList(),
        gameObject.Children.Select(child => child.Id).ToList());

    public static Engine.Mcp.McpObjectDetail Detail(GameObject gameObject) => new(
        gameObject.Id,
        gameObject.Name,
        gameObject.Parent?.Id,
        gameObject.Active,
        Transform(gameObject.Transform),
        gameObject.Components.Select(ComponentValueConverter.Describe).ToList(),
        gameObject.Children.Select(child => child.Id).ToList());

    public static Engine.Mcp.McpTransform Transform(Transform transform)
    {
        Quaternion worldRotation = transform.Rotation;
        Vector3 yawPitchRoll = worldRotation.ToYawPitchRoll();
        return new Engine.Mcp.McpTransform(
            ToMcp(transform.LocalPosition),
            new McpQuat(transform.LocalRotation.X, transform.LocalRotation.Y, transform.LocalRotation.Z, transform.LocalRotation.W),
            ToMcp(transform.LocalScale),
            ToMcp(transform.Position),
            new McpQuat(worldRotation.X, worldRotation.Y, worldRotation.Z, worldRotation.W),
            new McpVec3(yawPitchRoll.Y * MathUtil.RadToDeg, yawPitchRoll.X * MathUtil.RadToDeg, yawPitchRoll.Z * MathUtil.RadToDeg));
    }

    public static void ApplyTransform(GameObject target, McpVec3? position, McpQuat? rotation, McpVec3? eulerDegrees, McpVec3? scale, bool world)
    {
        Transform transform = target.Transform;
        if (rotation is not null)
        {
            Quaternion value = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W).Normalized;
            if (world)
                transform.Rotation = value;
            else
                transform.LocalRotation = value;
        }
        if (eulerDegrees is not null)
        {
            Quaternion value = Quaternion.FromYawPitchRoll(
                eulerDegrees.Y * MathUtil.DegToRad,
                eulerDegrees.X * MathUtil.DegToRad,
                eulerDegrees.Z * MathUtil.DegToRad).Normalized;
            if (world)
                transform.Rotation = value;
            else
                transform.LocalRotation = value;
        }
        if (position is not null)
        {
            Vector3 value = new Vector3(position.X, position.Y, position.Z);
            if (world)
                transform.Position = value;
            else
                transform.LocalPosition = value;
        }
        if (scale is not null)
        {
            Vector3 value = new Vector3(
                MathF.Abs(scale.X) < 1e-4f ? EditorContext.MinimumScaleMagnitude : scale.X,
                MathF.Abs(scale.Y) < 1e-4f ? EditorContext.MinimumScaleMagnitude : scale.Y,
                MathF.Abs(scale.Z) < 1e-4f ? EditorContext.MinimumScaleMagnitude : scale.Z);
            transform.LocalScale = value;
        }
    }

    public static Dictionary<string, object?> SceneSettings(SceneModel scene) => new()
    {
        ["skybox_mode"] = scene.SkyboxMode.ToString(),
        ["sky_color"] = new Dictionary<string, object?> { ["r"] = scene.SkyColor.X, ["g"] = scene.SkyColor.Y, ["b"] = scene.SkyColor.Z, ["a"] = scene.SkyColor.W },
        ["sun_direction"] = new Dictionary<string, object?> { ["x"] = scene.SunDirection.X, ["y"] = scene.SunDirection.Y, ["z"] = scene.SunDirection.Z },
        ["clouds_enabled"] = scene.CloudsEnabled,
        ["cloud_preset"] = scene.CloudPreset,
        ["cloud_coverage"] = scene.CloudCoverage,
        ["cloud_density"] = scene.CloudDensity,
        ["cloud_wind_speed"] = scene.CloudWindSpeed,
    };

    public static void ApplySceneSettings(SceneModel scene, IReadOnlyDictionary<string, JsonElement> settings)
    {
        foreach ((string key, JsonElement value) in settings)
        {
            switch (key)
            {
                case "skybox_mode":
                    scene.SkyboxMode = Enum.TryParse<SkyboxMode>(value.GetString(), ignoreCase: true, out SkyboxMode mode) ? mode : throw new ArgumentException($"Unknown skybox mode '{value.GetString()}'.");
                    break;
                case "sky_color":
                    scene.SkyColor = ParseVector4(value);
                    break;
                case "sun_direction":
                    scene.SunDirection = ParseVector3(value);
                    break;
                case "clouds_enabled":
                    scene.CloudsEnabled = value.GetBoolean();
                    break;
                case "cloud_preset":
                    scene.CloudPreset = value.GetString() ?? scene.CloudPreset;
                    break;
                case "cloud_coverage":
                    scene.CloudCoverage = Math.Clamp(value.GetSingle(), 0f, 1f);
                    break;
                case "cloud_density":
                    scene.CloudDensity = Math.Clamp(value.GetSingle(), 0.1f, 2f);
                    break;
                case "cloud_wind_speed":
                    scene.CloudWindSpeed = Math.Clamp(value.GetSingle(), 0f, 2f);
                    break;
                default:
                    throw new ArgumentException($"Unknown scene setting '{key}'.");
            }
        }
    }

    private static McpVec3 ToMcp(Vector3 value) => new(value.X, value.Y, value.Z);

    private static Vector3 ParseVector3(JsonElement value) => value.ValueKind == JsonValueKind.Object
        ? new Vector3(GetNumber(value, "x"), GetNumber(value, "y"), GetNumber(value, "z"))
        : throw new ArgumentException("Expected an object with x, y, z.");

    private static Vector4 ParseVector4(JsonElement value) => value.ValueKind == JsonValueKind.Object
        ? new Vector4(GetNumber(value, "r"), GetNumber(value, "g"), GetNumber(value, "b"), value.TryGetProperty("a", out JsonElement alpha) ? alpha.GetSingle() : 1f)
        : throw new ArgumentException("Expected an object with r, g, b, a.");

    private static float GetNumber(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement item) && item.ValueKind == JsonValueKind.Number
            ? item.GetSingle()
            : throw new ArgumentException($"Missing number '{name}'.");
}
