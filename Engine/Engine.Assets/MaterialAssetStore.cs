using System.Text.Json;
using Engine.Core;
using Engine.Rendering;
using Engine.Scene;

namespace Engine.Assets;

public sealed class MaterialAssetStore
{
    private sealed record Document(MaterialAsset Asset, string Content, long Revision);
    private sealed record Resolution(ResolvedMaterial Material, string Signature);
    private readonly Dictionary<string, Document> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Document> _previews = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Resolution> _resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _diagnostics = [];
    private DateTime _nextRefresh;
    private long _revision;
    private long _generation;
    private readonly Dictionary<string, long> _resolvedGenerations = new(StringComparer.OrdinalIgnoreCase);

    public MaterialAssetStore(string assetsRoot) : this(new AssetCatalog(assetsRoot)) { }

    public MaterialAssetStore(AssetCatalog catalog)
    {
        Catalog = catalog;
        Catalog.Diagnostic += Report;
    }

    public AssetCatalog Catalog { get; }
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMilliseconds(250);
    public IReadOnlyList<string> Diagnostics => _diagnostics;
    public event Action<string>? Diagnostic;

    public AssetReference GetReference(string path) => Catalog.GetReference(path);
    public string ResolvePath(AssetReference reference) => Catalog.ResolvePath(reference);

    public MaterialAsset Load(AssetReference reference)
    {
        Refresh();
        string key = Key(reference);
        return GetDocument(key).Asset.Clone();
    }

    public ResolvedMaterial Resolve(AssetReference reference)
    {
        Refresh();
        string key;
        try
        {
            key = Key(reference);
        }
        catch (Exception exception) when (IsAssetError(exception))
        {
            Report(exception.Message);
            return new ResolvedMaterial(new MaterialProperties(), 0);
        }
        try
        {
            if (_resolvedGenerations.TryGetValue(key, out long generation) && generation == _generation
                && _resolved.TryGetValue(key, out Resolution? cached))
                return cached.Material;
            (MaterialProperties properties, string signature) = ResolveCore(key, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            _resolvedGenerations[key] = _generation;
            if (_resolved.TryGetValue(key, out Resolution? previous) && previous.Signature == signature)
                return previous.Material;
            ResolvedMaterial material = new(properties, ++_revision);
            _resolved[key] = new Resolution(material, signature);
            return material;
        }
        catch (Exception exception) when (IsAssetError(exception))
        {
            Report($"Material '{reference}': {exception.Message}");
            return _resolved.TryGetValue(key, out Resolution? previous)
                ? previous.Material
                : new ResolvedMaterial(new MaterialProperties(), 0);
        }
    }

    public AssetReference Create(string path, MaterialAsset asset)
    {
        MaterialSchema.Validate(asset);
        string fullPath = Catalog.Contain(path);
        if (File.Exists(fullPath))
            throw new IOException($"Asset '{path}' already exists.");
        WriteAtomic(fullPath, asset);
        AssetReference reference = Catalog.GetReference(fullPath);
        UpdateDocument(reference.Key, asset);
        return reference;
    }

    public void Save(AssetReference reference, MaterialAsset asset)
    {
        MaterialSchema.Validate(asset);
        string key = Key(reference);
        string path = Catalog.ResolvePath(key);
        WriteAtomic(path, asset);
        UpdateDocument(key, asset);
        _previews.Remove(key);
    }

    public void SetPreview(AssetReference reference, MaterialAsset? asset)
    {
        string key = Key(reference);
        if (asset is null)
        {
            if (_previews.Remove(key))
                _generation++;
            return;
        }
        MaterialSchema.Validate(asset);
        _previews[key] = new Document(asset.Clone(), string.Empty, ++_revision);
        _generation++;
    }

    public void Refresh()
    {
        DateTime now = DateTime.UtcNow;
        if (now < _nextRefresh)
            return;
        _nextRefresh = now + RefreshInterval;
        _diagnostics.Clear();
        Catalog.Refresh();
        foreach (string key in _documents.Keys.ToArray())
        {
            try
            {
                string content = VirtualFileSystem.ReadAllText(Catalog.ResolvePath(key));
                if (content == _documents[key].Content)
                    continue;
                MaterialAsset asset = Deserialize(content);
                _documents[key] = new Document(asset, content, ++_revision);
                _generation++;
            }
            catch (Exception exception) when (IsAssetError(exception))
            {
                Report($"Material '{key}': {exception.Message}");
            }
        }
    }

    private (MaterialProperties Properties, string Signature) ResolveCore(string key, HashSet<string> visiting)
    {
        if (!visiting.Add(key))
            throw new InvalidDataException("Material inheritance cycle detected.");
        if (visiting.Count > 128)
            throw new InvalidDataException("Material inheritance exceeds 128 levels.");
        Document document = _previews.TryGetValue(key, out Document? preview) ? preview : GetDocument(key);
        MaterialProperties basis = new();
        string signature = key + ":" + document.Revision;
        if (!document.Asset.Parent.IsEmpty)
        {
            var parent = ResolveCore(Key(document.Asset.Parent), visiting);
            basis = parent.Properties;
            signature += "/" + parent.Signature;
        }
        visiting.Remove(key);
        return (MaterialSchema.Apply(basis, document.Asset.Overrides), signature);
    }

    private Document GetDocument(string key)
    {
        if (_documents.TryGetValue(key, out Document? document))
            return document;
        string content = VirtualFileSystem.ReadAllText(Catalog.ResolvePath(key));
        document = new Document(Deserialize(content), content, ++_revision);
        _documents[key] = document;
        return document;
    }

    private string Key(AssetReference reference)
    {
        if (reference.Key?.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) == true && Guid.TryParse(reference.Key[5..], out Guid id))
            return "guid:" + id.ToString("N");
        return Catalog.GetReference(Catalog.ResolvePath(reference)).Key;
    }

    private void UpdateDocument(string key, MaterialAsset asset)
    {
        string content = JsonSerializer.Serialize(asset, MaterialSchema.JsonOptions);
        _documents[key] = new Document(asset.Clone(), content, ++_revision);
        _generation++;
    }

    private static MaterialAsset Deserialize(string content)
    {
        MaterialAsset asset = JsonSerializer.Deserialize<MaterialAsset>(content, MaterialSchema.JsonOptions)
            ?? throw new InvalidDataException("Material document is null.");
        MaterialSchema.Validate(asset);
        return asset;
    }

    private static void WriteAtomic(string path, MaterialAsset asset)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(asset, MaterialSchema.JsonOptions));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static bool IsAssetError(Exception exception) => exception is IOException or UnauthorizedAccessException
        or JsonException or InvalidDataException or ArgumentException or InvalidOperationException;

    private void Report(string message)
    {
        if (_diagnostics.Contains(message))
            return;
        _diagnostics.Add(message);
        Diagnostic?.Invoke(message);
    }
}
