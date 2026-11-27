using System.Reflection;
using System.Text.Json;
using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Editor.Mcp;

public static class ComponentValueConverter
{
    private static readonly Dictionary<Type, IReadOnlyList<PropertyInfo>> PropertyCache = [];

    public static Engine.Mcp.McpComponentInfo Describe(Component component) => new(
        component.Id,
        component.ComponentTypeName,
        PropertyDictionary(component));

    public static object? Read(Component component, string property)
    {
        PropertyInfo info = Resolve(component, property);
        return ConvertOut(info.GetValue(component));
    }

    public static void Write(Component component, string property, JsonElement value)
    {
        PropertyInfo info = Resolve(component, property);
        info.SetValue(component, ConvertIn(info.PropertyType, value, property));
    }

    private static PropertyInfo Resolve(Component component, string property)
    {
        IReadOnlyList<PropertyInfo> properties = GetProperties(component.GetType());
        PropertyInfo? match = properties.FirstOrDefault(candidate => candidate.Name.Equals(property, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Component '{component.ComponentTypeName}' has no writable property '{property}'.");
        return match;
    }

    private static Dictionary<string, object?> PropertyDictionary(Component component)
    {
        Dictionary<string, object?> result = new(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = component.Id.ToString("D"),
            ["enabled"] = component.Enabled,
        };
        foreach (PropertyInfo property in GetProperties(component.GetType()))
            result[property.Name] = ConvertOut(property.GetValue(component));
        return result;
    }

    private static IReadOnlyList<PropertyInfo> GetProperties(Type type)
    {
        if (PropertyCache.TryGetValue(type, out IReadOnlyList<PropertyInfo>? cached))
            return cached;
        List<PropertyInfo> properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0 && IsSupported(property.PropertyType))
            .OrderBy(property => property.MetadataToken)
            .ToList();
        IReadOnlyList<PropertyInfo> result = properties;
        PropertyCache[type] = result;
        return result;
    }

    private static bool IsSupported(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum)
            return true;
        return type == typeof(bool)
            || type == typeof(int)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(string)
            || type == typeof(Vector2)
            || type == typeof(Vector3)
            || type == typeof(Vector4)
            || type == typeof(Quaternion)
            || type == typeof(AssetReference)
            || type == typeof(Guid);
    }

    private static object? ConvertOut(object? value)
    {
        if (value is null)
            return null;
        if (value is Enum enumValue)
            return enumValue.ToString();
        if (value is bool || value is int || value is long || value is float || value is double || value is string)
            return value;
        if (value is Guid guid)
            return guid.ToString("D");
        if (value is Vector2 vector2)
            return new Dictionary<string, object?> { ["x"] = vector2.X, ["y"] = vector2.Y };
        if (value is Vector3 vector3)
            return new Dictionary<string, object?> { ["x"] = vector3.X, ["y"] = vector3.Y, ["z"] = vector3.Z };
        if (value is Vector4 vector4)
            return new Dictionary<string, object?> { ["x"] = vector4.X, ["y"] = vector4.Y, ["z"] = vector4.Z, ["w"] = vector4.W };
        if (value is Quaternion quaternion)
            return new Dictionary<string, object?> { ["x"] = quaternion.X, ["y"] = quaternion.Y, ["z"] = quaternion.Z, ["w"] = quaternion.W };
        if (value is AssetReference reference)
            return reference.Key;
        return value.ToString();
    }

    private static object? ConvertIn(Type targetType, JsonElement value, string property)
    {
        Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (type == typeof(JsonElement))
            return value;
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null)
                return null;
            throw new ArgumentException($"Property '{property}' cannot be null.");
        }
        try
        {
            if (type.IsEnum)
            {
                return value.ValueKind == JsonValueKind.String
                    ? Enum.Parse(type, value.GetString()!, ignoreCase: true)
                    : Enum.ToObject(type, value.GetInt64());
            }
            if (type == typeof(bool))
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new ArgumentException($"Value for '{property}' must be true or false.");
            if (type == typeof(int))
                return (int)ParseNumber(value, property);
            if (type == typeof(long))
                return (long)ParseNumber(value, property);
            if (type == typeof(float))
                return (float)ParseNumber(value, property);
            if (type == typeof(double))
                return ParseNumber(value, property);
            if (type == typeof(string))
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            if (type == typeof(Guid))
                return Guid.Parse(value.GetString()!);
            if (type == typeof(Vector2))
                return ParseVector2(value);
            if (type == typeof(Vector3))
                return ParseVector3(value);
            if (type == typeof(Vector4))
                return ParseVector4(value);
            if (type == typeof(Quaternion))
                return ParseQuaternion(value);
            if (type == typeof(AssetReference))
            {
                string path = value.ValueKind == JsonValueKind.String
                    ? value.GetString()!
                    : value.TryGetProperty("path", out JsonElement pathElement) ? pathElement.GetString()! : string.Empty;
                return new AssetReference(path);
            }
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"Value for '{property}' is not valid for type {type.Name}: {exception.Message}");
        }
        throw new ArgumentException($"Property '{property}' of type {type.Name} is not supported over MCP.");
    }

    private static Vector2 ParseVector2(JsonElement value) => value.ValueKind == JsonValueKind.Object
        ? new Vector2(Number(value, "x"), Number(value, "y"))
        : throw new ArgumentException("Expected an object with x and y.");

    private static Vector3 ParseVector3(JsonElement value) => value.ValueKind == JsonValueKind.Object
        ? new Vector3(Number(value, "x"), Number(value, "y"), Number(value, "z"))
        : throw new ArgumentException("Expected an object with x, y, z.");

    private static Vector4 ParseVector4(JsonElement value) => value.ValueKind == JsonValueKind.Object
        ? new Vector4(Number(value, "x"), Number(value, "y"), Number(value, "z"), Number(value, "w"))
        : throw new ArgumentException("Expected an object with x, y, z, w.");

    private static Quaternion ParseQuaternion(JsonElement value) => value.ValueKind == JsonValueKind.Object
        ? new Quaternion(Number(value, "x"), Number(value, "y"), Number(value, "z"), Number(value, "w"))
        : throw new ArgumentException("Expected an object with x, y, z, w.");

    private static double ParseNumber(JsonElement value, string property)
    {
        if (value.ValueKind == JsonValueKind.Number)
            return value.GetDouble();
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
            return parsed;
        throw new ArgumentException($"Value for '{property}' must be a number.");
    }

    private static float Number(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement item) && item.ValueKind == JsonValueKind.Number
            ? item.GetSingle()
            : throw new ArgumentException($"Missing number '{name}'.");
}
