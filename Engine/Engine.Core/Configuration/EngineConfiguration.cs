using System.Text.Json;
using System.Text.Json.Serialization;

namespace Engine.Core;

public sealed class EngineConfiguration
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _lock = new();
    private readonly Dictionary<string, JsonElement> _sections = new(StringComparer.Ordinal);

    public T GetSection<T>(string key) where T : class, new()
    {
        lock (_lock)
        {
            if (_sections.TryGetValue(key, out JsonElement element))
            {
                try
                {
                    return element.Deserialize<T>(JsonOptions) ?? new T();
                }
                catch (JsonException)
                {
                    return new T();
                }
            }
        }
        return new T();
    }

    public void SetSection<T>(string key, T value) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        JsonElement element = JsonSerializer.SerializeToElement(value, JsonOptions);
        lock (_lock)
            _sections[key] = element;
    }

    public bool HasSection(string key)
    {
        lock (_lock)
            return _sections.ContainsKey(key);
    }

    public bool RemoveSection(string key)
    {
        lock (_lock)
            return _sections.Remove(key);
    }

    public IReadOnlyList<string> GetSectionKeys()
    {
        lock (_lock)
            return [.. _sections.Keys];
    }

    public void Save(string path)
    {
        Dictionary<string, JsonElement> snapshot;
        lock (_lock)
            snapshot = new Dictionary<string, JsonElement>(_sections);
        string serialized = JsonSerializer.Serialize(snapshot, JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, serialized);
        File.Move(temporaryPath, path, overwrite: true);
    }

    public bool TryLoad(string path)
    {
        if (!File.Exists(path))
            return false;
        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return false;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            Dictionary<string, JsonElement> loaded = [];
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
                loaded[property.Name] = property.Value.Clone();
            lock (_lock)
            {
                _sections.Clear();
                foreach ((string key, JsonElement element) in loaded)
                    _sections[key] = element;
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
