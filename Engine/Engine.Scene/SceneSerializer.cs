using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Engine.Mathematics;

namespace Engine.Scene;

public static class SceneSerializer
{
    public const int CurrentVersion = 2;

    private static readonly HashSet<Type> SupportedPropertyTypes =
    [
        typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal), typeof(string), typeof(Guid),
        typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Quaternion),
        typeof(EntityReference), typeof(ComponentReference), typeof(AssetReference),
    ];

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { MathJsonConverterFactory.Instance, new JsonStringEnumConverter() },
    };

    public static void Save(Scene scene, string path)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string json = ToJson(scene);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, json);
    }

    public static Scene Load(string path, ComponentRegistry? componentRegistry = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string json = File.ReadAllText(path);
        return FromJson(json, componentRegistry);
    }

    public static string ToJson(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return SceneSnapshot.Capture(scene).ToJson();
    }

    public static Scene FromJson(string json, ComponentRegistry? componentRegistry = null)
    {
        int sourceVersion = ReadSourceVersion(json);
        SceneSnapshot snapshot = SceneSnapshot.Parse(json, out IReadOnlyList<SceneDiagnostic> diagnostics);
        Scene scene = snapshot.Restore(CloneIdPolicy.PreserveIds, componentRegistry).Scene;
        scene.SourceVersion = sourceVersion;
        foreach (SceneDiagnostic diagnostic in diagnostics)
            scene.AddDiagnostic(diagnostic);
        return scene;
    }

    internal static void CopyProperties(Component source, Component target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Dictionary<string, JsonElement>? properties = CaptureProperties(source, out _);
        ApplyProperties(target, properties);
    }

    internal static Dictionary<string, JsonElement>? CaptureProperties(
        Component component,
        out Dictionary<string, SceneReferenceKind>? referenceKinds)
    {
        if (component is ScriptComponent scriptComponent)
            return CaptureScriptProperties(scriptComponent, out referenceKinds);
        return CaptureComponentProperties(component, out referenceKinds);
    }

    private static Dictionary<string, JsonElement>? CaptureComponentProperties(
        Component component,
        out Dictionary<string, SceneReferenceKind>? referenceKinds)
    {
        Dictionary<string, JsonElement>? properties = null;
        referenceKinds = null;
        Type type = component.GetType();
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            if (!IsSerializableProperty(property))
                continue;
            object? value;
            try
            {
                value = property.GetValue(component);
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException(
                    $"Failed to read component property '{type.Name}.{property.Name}'.",
                    exception.InnerException ?? exception);
            }
            properties ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            properties[property.Name] = JsonSerializer.SerializeToElement(value, property.PropertyType, JsonOptions);
            SceneReferenceKind? referenceKind = SceneReferenceRemapper.GetReferenceKind(property);
            if (referenceKind is not null)
            {
                referenceKinds ??= new Dictionary<string, SceneReferenceKind>(StringComparer.Ordinal);
                referenceKinds[property.Name] = referenceKind.Value;
            }
        }
        return properties;
    }

    private static Dictionary<string, JsonElement>? CaptureScriptProperties(
        ScriptComponent script,
        out Dictionary<string, SceneReferenceKind>? referenceKinds)
    {
        Dictionary<string, JsonElement>? properties = CaptureComponentProperties(script, out referenceKinds);
        object? instance = script.Instance;
        if (instance is null)
            return properties;
        Type type = instance.GetType();
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            if (!IsSerializableProperty(property) || property.Name == nameof(ScriptComponent.Script))
                continue;
            object? value;
            try
            {
                value = property.GetValue(instance);
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException(
                    $"Failed to read script property '{type.Name}.{property.Name}'.",
                    exception.InnerException ?? exception);
            }
            properties ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            properties[property.Name] = JsonSerializer.SerializeToElement(value, property.PropertyType, JsonOptions);
            SceneReferenceKind? referenceKind = SceneReferenceRemapper.GetReferenceKind(property);
            if (referenceKind is not null)
            {
                referenceKinds ??= new Dictionary<string, SceneReferenceKind>(StringComparer.Ordinal);
                referenceKinds[property.Name] = referenceKind.Value;
            }
        }
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                     .OrderBy(field => field.Name, StringComparer.Ordinal))
        {
            if (!IsSerializableField(field) || properties is not null && properties.ContainsKey(field.Name))
                continue;
            object? value;
            try
            {
                value = field.GetValue(instance);
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException(
                    $"Failed to read script field '{type.Name}.{field.Name}'.",
                    exception.InnerException ?? exception);
            }
            properties ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            properties[field.Name] = JsonSerializer.SerializeToElement(value, field.FieldType, JsonOptions);
            SceneReferenceKind? referenceKind = SceneReferenceRemapper.GetReferenceKind(field.FieldType);
            if (referenceKind is not null)
            {
                referenceKinds ??= new Dictionary<string, SceneReferenceKind>(StringComparer.Ordinal);
                referenceKinds[field.Name] = referenceKind.Value;
            }
        }
        return properties;
    }

    private static bool IsSerializableField(FieldInfo field)
    {
        if (field.IsInitOnly || field.IsLiteral || field.IsStatic)
            return false;
        if (field.GetCustomAttribute<SceneIgnoreAttribute>() is not null
            || field.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
            return false;
        return IsSupportedPropertyType(field.FieldType);
    }

    internal static void ApplyProperties(
        Component component,
        Dictionary<string, JsonElement>? properties)
    {
        if (properties is null)
            return;
        if (component is ScriptComponent scriptComponent)
        {
            ApplyScriptProperties(scriptComponent, properties);
            return;
        }
        ApplyComponentProperties(component, properties);
    }

    private static void ApplyScriptProperties(ScriptComponent script, Dictionary<string, JsonElement> properties)
    {
        if (properties.TryGetValue(nameof(ScriptComponent.Script), out JsonElement scriptElement))
            ApplyPropertyValue(
                script,
                typeof(ScriptComponent).GetProperty(nameof(ScriptComponent.Script))!,
                scriptElement);
        Dictionary<string, JsonElement> remainder = properties
            .Where(pair => pair.Key != nameof(ScriptComponent.Script))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (remainder.Count == 0)
            return;
        if (!script.EnsureInstance())
        {
            script.PendingProperties = remainder;
            return;
        }
        ApplyInstanceProperties(script.Instance!, remainder);
    }

    internal static void ApplyInstanceProperties(object instance, Dictionary<string, JsonElement> properties)
    {
        Type type = instance.GetType();
        Dictionary<string, PropertyInfo> writableProperties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(IsSerializableProperty)
            .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, FieldInfo> writableFields = type
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(IsSerializableField)
            .Where(field => !writableProperties.ContainsKey(field.Name))
            .ToDictionary(field => field.Name, StringComparer.OrdinalIgnoreCase);
        foreach ((string key, JsonElement element) in properties)
        {
            if (writableProperties.TryGetValue(key, out PropertyInfo? property))
                ApplyPropertyValue(instance, property, element);
            else if (writableFields.TryGetValue(key, out FieldInfo? field))
                ApplyFieldValue(instance, field, element);
        }
    }

    private static void ApplyFieldValue(
        object target,
        FieldInfo field,
        JsonElement element)
    {
        try
        {
            object? value = DeserializeValue(element, field.FieldType);
            field.SetValue(target, value);
        }
        catch (Exception exception) when (exception is JsonException
                                          or FormatException
                                          or InvalidCastException
                                          or TargetInvocationException
                                          or ArgumentException)
        {
            throw new InvalidOperationException(
                $"Failed to apply component field '{target.GetType().Name}.{field.Name}'.",
                exception is TargetInvocationException invocation && invocation.InnerException is not null
                    ? invocation.InnerException
                    : exception);
        }
    }

    private static void ApplyComponentProperties(
        Component component,
        Dictionary<string, JsonElement>? properties)
    {
        if (properties is null)
            return;
        Type type = component.GetType();
        Dictionary<string, PropertyInfo> writableProperties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(IsSerializableProperty)
            .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);
        foreach ((string key, JsonElement element) in properties)
        {
            if (!writableProperties.TryGetValue(key, out PropertyInfo? property))
                continue;
            ApplyProperty(component, property, element);
        }
    }

    internal static void ApplyProperty(
        Component component,
        PropertyInfo property,
        JsonElement element)
    {
        ApplyPropertyValue(component, property, element);
    }

    private static void ApplyPropertyValue(
        object target,
        PropertyInfo property,
        JsonElement element)
    {
        try
        {
            object? value = DeserializeValue(element, property.PropertyType);
            property.SetValue(target, value);
        }
        catch (Exception exception) when (exception is JsonException
                                          or FormatException
                                          or InvalidCastException
                                          or TargetInvocationException
                                          or ArgumentException)
        {
            throw new InvalidOperationException(
                $"Failed to apply component property '{target.GetType().Name}.{property.Name}'.",
                exception is TargetInvocationException invocation && invocation.InnerException is not null
                    ? invocation.InnerException
                    : exception);
        }
    }

    internal static Vector3 ToVector3(float[] values) => SceneSnapshot.ToVector3(values);

    internal static Quaternion ToQuaternion(float[] values) => SceneSnapshot.ToQuaternion(values);

    private static int ReadSourceVersion(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
        return document.RootElement.TryGetProperty("version", out JsonElement version)
            && version.TryGetInt32(out int parsed)
                ? parsed
                : 1;
    }

    private static bool IsSerializableProperty(PropertyInfo property)
    {
        if (!property.CanRead || property.GetMethod?.IsPublic != true
            || !property.CanWrite || property.SetMethod?.IsPublic != true)
            return false;
        if (property.GetIndexParameters().Length != 0)
            return false;
        if (property.GetCustomAttribute<SceneIgnoreAttribute>() is not null
            || property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
            return false;
        if (property.DeclaringType == typeof(Component))
            return false;
        return IsSupportedPropertyType(property.PropertyType);
    }

    private static bool IsSupportedPropertyType(Type type)
    {
        Type actual = Nullable.GetUnderlyingType(type) ?? type;
        if (SupportedPropertyTypes.Contains(actual) || actual.IsEnum)
            return true;
        if (actual.IsArray)
            return IsSupportedPropertyType(actual.GetElementType()!);
        if (actual.IsGenericType && actual.GetGenericTypeDefinition() == typeof(List<>))
            return IsSupportedPropertyType(actual.GetGenericArguments()[0]);
        return false;
    }

    private static object? DeserializeValue(JsonElement element, Type targetType)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null)
                return null;
            return Activator.CreateInstance(targetType);
        }
        return JsonSerializer.Deserialize(element.GetRawText(), targetType, JsonOptions);
    }
}
