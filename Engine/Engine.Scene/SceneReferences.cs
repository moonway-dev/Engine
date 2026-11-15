using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Engine.Scene;

public enum SceneReferenceKind
{
    Entity,
    Component,
}

[AttributeUsage(AttributeTargets.Property)]
public class SceneReferenceAttribute : Attribute
{
    public SceneReferenceKind Kind { get; }

    public SceneReferenceAttribute(SceneReferenceKind kind)
    {
        Kind = kind;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class EntityReferenceAttribute : SceneReferenceAttribute
{
    public EntityReferenceAttribute()
        : base(SceneReferenceKind.Entity)
    {
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class ComponentReferenceAttribute : SceneReferenceAttribute
{
    public ComponentReferenceAttribute()
        : base(SceneReferenceKind.Component)
    {
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class SceneIgnoreAttribute : Attribute
{
}

[JsonConverter(typeof(EntityReferenceJsonConverter))]
public readonly record struct EntityReference(Guid Id)
{
    public static EntityReference Empty => new(Guid.Empty);

    public bool IsEmpty => Id == Guid.Empty;

    public GameObject? Resolve(Scene scene) => IsEmpty ? null : scene.FindById(Id);

    public static implicit operator EntityReference(Guid id) => new(id);

    public static implicit operator Guid(EntityReference reference) => reference.Id;
}

[JsonConverter(typeof(ComponentReferenceJsonConverter))]
public readonly record struct ComponentReference(Guid Id)
{
    public static ComponentReference Empty => new(Guid.Empty);

    public bool IsEmpty => Id == Guid.Empty;

    public Component? Resolve(Scene scene) => IsEmpty ? null : scene.FindComponentById(Id);

    public TComponent? Resolve<TComponent>(Scene scene) where TComponent : Component =>
        Resolve(scene) as TComponent;

    public static implicit operator ComponentReference(Guid id) => new(id);

    public static implicit operator Guid(ComponentReference reference) => reference.Id;
}

[JsonConverter(typeof(AssetReferenceJsonConverter))]
public readonly record struct AssetReference(string Key)
{
    public static AssetReference Empty => new(string.Empty);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Key);

    public override string ToString() => Key;

    public static implicit operator AssetReference(string key) => new(key);

    public static implicit operator string(AssetReference reference) => reference.Key;
}

public interface ISceneReferenceRemappable
{
    void RemapSceneReferences(SceneReferenceMap references);
}

public sealed class SceneReferenceMap
{
    private readonly IReadOnlyDictionary<Guid, Guid> _entityIds;
    private readonly IReadOnlyDictionary<Guid, Guid> _componentIds;

    public IReadOnlyDictionary<Guid, Guid> EntityIds => _entityIds;

    public IReadOnlyDictionary<Guid, Guid> ComponentIds => _componentIds;

    internal SceneReferenceMap(
        IReadOnlyDictionary<Guid, Guid> entityIds,
        IReadOnlyDictionary<Guid, Guid> componentIds)
    {
        _entityIds = new Dictionary<Guid, Guid>(entityIds);
        _componentIds = new Dictionary<Guid, Guid>(componentIds);
    }

    public Guid RemapEntity(Guid id) =>
        id != Guid.Empty && _entityIds.TryGetValue(id, out Guid remapped) ? remapped : id;

    public Guid RemapComponent(Guid id) =>
        id != Guid.Empty && _componentIds.TryGetValue(id, out Guid remapped) ? remapped : id;

    public EntityReference Remap(EntityReference reference) => new(RemapEntity(reference.Id));

    public ComponentReference Remap(ComponentReference reference) => new(RemapComponent(reference.Id));

    public bool TryRemapEntity(Guid id, out Guid remapped) => _entityIds.TryGetValue(id, out remapped);

    public bool TryRemapComponent(Guid id, out Guid remapped) => _componentIds.TryGetValue(id, out remapped);
}

internal sealed class EntityReferenceJsonConverter : JsonConverter<EntityReference>
{
    public override EntityReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(ReadGuid(ref reader));

    public override void Write(Utf8JsonWriter writer, EntityReference value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Id);

    private static Guid ReadGuid(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return Guid.Empty;
        if (reader.TokenType == JsonTokenType.String && Guid.TryParse(reader.GetString(), out Guid id))
            return id;
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty("id", out JsonElement idElement)
                && idElement.ValueKind == JsonValueKind.String
                && Guid.TryParse(idElement.GetString(), out id))
                return id;
        }
        throw new JsonException("Expected an entity GUID.");
    }
}

internal sealed class ComponentReferenceJsonConverter : JsonConverter<ComponentReference>
{
    public override ComponentReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(ReadGuid(ref reader));

    public override void Write(Utf8JsonWriter writer, ComponentReference value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Id);

    private static Guid ReadGuid(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return Guid.Empty;
        if (reader.TokenType == JsonTokenType.String && Guid.TryParse(reader.GetString(), out Guid id))
            return id;
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty("id", out JsonElement idElement)
                && idElement.ValueKind == JsonValueKind.String
                && Guid.TryParse(idElement.GetString(), out id))
                return id;
        }
        throw new JsonException("Expected a component GUID.");
    }
}

internal sealed class AssetReferenceJsonConverter : JsonConverter<AssetReference>
{
    public override AssetReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return AssetReference.Empty;
        if (reader.TokenType == JsonTokenType.String)
            return new AssetReference(reader.GetString() ?? string.Empty);
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty("key", out JsonElement keyElement))
                return new AssetReference(keyElement.GetString() ?? string.Empty);
        }
        throw new JsonException("Expected an asset key.");
    }

    public override void Write(Utf8JsonWriter writer, AssetReference value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Key);
}

internal static class SceneReferenceRemapper
{
    public static SceneReferenceKind? GetReferenceKind(PropertyInfo property)
    {
        SceneReferenceAttribute? attribute = property.GetCustomAttribute<SceneReferenceAttribute>();
        if (attribute is not null)
            return attribute.Kind;
        return GetReferenceKind(property.PropertyType);
    }

    public static SceneReferenceKind? GetReferenceKind(Type type)
    {
        Type actual = Nullable.GetUnderlyingType(type) ?? type;
        if (actual == typeof(EntityReference))
            return SceneReferenceKind.Entity;
        if (actual == typeof(ComponentReference))
            return SceneReferenceKind.Component;
        if (actual.IsArray)
            return GetReferenceKind(actual.GetElementType()!);
        if (actual.IsGenericType && actual.GetGenericTypeDefinition() == typeof(List<>))
            return GetReferenceKind(actual.GetGenericArguments()[0]);
        return null;
    }

    public static void RemapComponent(Component component, SceneReferenceMap map)
    {
        Type type = component.GetType();
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetMethod?.IsPublic != true || property.SetMethod?.IsPublic != true)
                continue;
            SceneReferenceKind? kind = GetReferenceKind(property);
            if (kind is null)
                continue;
            object? value = property.GetValue(component);
            object? remapped = RemapValue(value, property.PropertyType, kind.Value, map);
            property.SetValue(component, remapped);
        }
        RemapCustomComponent(component, map);
    }

    public static void RemapCustomComponent(Component component, SceneReferenceMap map)
    {
        if (component is ISceneReferenceRemappable custom)
            custom.RemapSceneReferences(map);
    }

    public static JsonElement RemapJson(JsonElement value, SceneReferenceKind kind, SceneReferenceMap map)
    {
        object? remapped = RemapJsonValue(value, kind, map);
        return JsonSerializer.SerializeToElement(remapped, SceneSerializer.JsonOptions);
    }

    private static object? RemapValue(object? value, Type type, SceneReferenceKind kind, SceneReferenceMap map)
    {
        if (value is null)
            return null;
        Type actual = Nullable.GetUnderlyingType(type) ?? type;
        if (actual == typeof(EntityReference))
            return map.Remap((EntityReference)value);
        if (actual == typeof(ComponentReference))
            return map.Remap((ComponentReference)value);
        if (actual == typeof(Guid))
        {
            Guid id = (Guid)value;
            return kind == SceneReferenceKind.Entity ? map.RemapEntity(id) : map.RemapComponent(id);
        }
        if (actual.IsArray)
        {
            Type elementType = actual.GetElementType()!;
            Array source = (Array)value;
            Array target = Array.CreateInstance(elementType, source.Length);
            for (int i = 0; i < source.Length; i++)
                target.SetValue(RemapValue(source.GetValue(i), elementType, kind, map), i);
            return target;
        }
        if (actual.IsGenericType && actual.GetGenericTypeDefinition() == typeof(List<>))
        {
            Type elementType = actual.GetGenericArguments()[0];
            IList target = (IList)Activator.CreateInstance(actual)!;
            foreach (object? item in (IEnumerable)value)
                target.Add(RemapValue(item, elementType, kind, map));
            return target;
        }
        return value;
    }

    private static object? RemapJsonValue(JsonElement value, SceneReferenceKind kind, SceneReferenceMap map)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.String:
                string? text = value.GetString();
                if (Guid.TryParse(text, out Guid id))
                {
                    Guid remapped = kind == SceneReferenceKind.Entity ? map.RemapEntity(id) : map.RemapComponent(id);
                    return remapped;
                }
                return text;
            case JsonValueKind.Array:
                return value.EnumerateArray().Select(element => RemapJsonValue(element, kind, map)).ToArray();
            case JsonValueKind.Object:
            {
                Dictionary<string, object?> result = [];
                foreach (JsonProperty property in value.EnumerateObject())
                    result[property.Name] = property.Name.Equals("id", StringComparison.OrdinalIgnoreCase)
                        ? RemapJsonValue(property.Value, kind, map)
                        : CloneJsonValue(property.Value);
                return result;
            }
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number:
                if (value.TryGetInt64(out long integer))
                    return integer;
                if (value.TryGetDecimal(out decimal number))
                    return number;
                return value.GetDouble();
            default:
                return value.GetRawText();
        }
    }

    private static object? CloneJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Array => value.EnumerateArray().Select(CloneJsonValue).ToArray(),
            JsonValueKind.Object => value.EnumerateObject().ToDictionary(
                property => property.Name,
                property => CloneJsonValue(property.Value)),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt64(out long integer) => integer,
            JsonValueKind.Number when value.TryGetDecimal(out decimal number) => number,
            JsonValueKind.Number => value.GetDouble(),
            _ => value.GetRawText(),
        };
    }
}
