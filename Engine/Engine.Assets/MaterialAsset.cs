using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Engine.Mathematics;
using Engine.Rendering;
using Engine.Scene;

namespace Engine.Assets;

public sealed class MaterialAsset
{
    public int Version { get; set; } = 1;
    public string Shader { get; set; } = "PBR Lit";
    public AssetReference Parent { get; set; }
    public Dictionary<string, JsonElement> Overrides { get; set; } = new(StringComparer.Ordinal);

    public MaterialAsset Clone() => new()
    {
        Version = Version,
        Shader = Shader,
        Parent = Parent,
        Overrides = Overrides.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal),
    };

    public T? GetOverride<T>(string name) => TryGetOverride<T>(name, out T? value) ? value : default;

    public bool TryGetOverride<T>(string name, out T? value)
    {
        if (Overrides.TryGetValue(name, out JsonElement element))
        {
            value = element.Deserialize<T>(MaterialSchema.JsonOptions);
            return true;
        }
        value = default;
        return false;
    }

    public void SetOverride<T>(string name, T value)
    {
        JsonElement element = JsonSerializer.SerializeToElement(value, MaterialSchema.JsonOptions);
        MaterialSchema.Apply(new MaterialProperties(), new Dictionary<string, JsonElement> { [name] = element });
        Overrides[name] = element;
    }

    public bool RemoveOverride(string name) => Overrides.Remove(name);
}

public static class MaterialSchema
{
    private static readonly Dictionary<string, PropertyInfo> Properties = typeof(MaterialProperties)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.SetMethod is not null)
        .ToDictionary(property => property.Name, StringComparer.Ordinal);

    public static IReadOnlyCollection<string> PropertyNames => Properties.Keys;
    public static JsonSerializerOptions JsonOptions { get; } = CreateOptions();

    public static MaterialProperties Apply(MaterialProperties basis, IReadOnlyDictionary<string, JsonElement> overrides)
    {
        MaterialProperties result = basis with { };
        foreach ((string name, JsonElement element) in overrides)
        {
            if (!Properties.TryGetValue(name, out PropertyInfo? property))
                throw new InvalidDataException($"Unknown material property '{name}'.");
            object? value;
            if (property.PropertyType == typeof(AssetReference) && element.ValueKind == JsonValueKind.Null)
                value = AssetReference.Empty;
            else
                value = element.Deserialize(property.PropertyType, JsonOptions);
            if (value is null || !IsValid(value))
                throw new InvalidDataException($"Invalid material property '{name}'.");
            property.SetValue(result, value);
        }
        return result;
    }

    public static void Validate(MaterialAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Version != 1)
            throw new InvalidDataException($"Unsupported material version '{asset.Version}'.");
        if (asset.Shader != "PBR Lit")
            throw new InvalidDataException($"Unsupported material shader '{asset.Shader}'.");
        if (asset.Overrides is null)
            throw new InvalidDataException("Material overrides cannot be null.");
        Apply(new MaterialProperties(), asset.Overrides);
    }

    private static bool IsValid(object value) => value switch
    {
        float scalar => float.IsFinite(scalar),
        Vector2 vector => float.IsFinite(vector.X) && float.IsFinite(vector.Y),
        Vector3 vector => float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z),
        TextureChannel channel => Enum.IsDefined(channel),
        _ => true,
    };

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            IncludeFields = true,
            IgnoreReadOnlyProperties = true,
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
