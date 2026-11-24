using Camera = Engine.Rendering.Camera;
using Light = Engine.Rendering.Light;
using System.Text.Json;
using Engine.Assets;
using Engine.Editor.Commands;
using Engine.Rendering;
using Engine.Scene;

namespace Engine.Editor.Documents;

public sealed class MaterialDocument : IDisposable
{
    private readonly MaterialAssetStore _store;
    private MaterialAsset _asset;
    private string _diskContent;
    private bool _disposed;

    public MaterialDocument(MaterialAssetStore store, AssetReference reference)
    {
        _store = store;
        Reference = store.GetReference(store.ResolvePath(reference));
        _asset = store.Load(Reference);
        _diskContent = File.ReadAllText(Path);
        History.Changed += OnChanged;
    }

    public AssetReference Reference { get; }
    public string Path => _store.ResolvePath(Reference);
    public string Title => System.IO.Path.GetFileNameWithoutExtension(Path) + (IsDirty ? "*" : string.Empty);
    public CommandHistory History { get; } = new();
    public bool IsDirty => !History.IsAtSavePoint;
    public bool HasExternalConflict { get; private set; }
    public MaterialAsset Asset => _asset.Clone();
    public MaterialProperties Effective => _store.Resolve(Reference).Properties;
    public event EventHandler? Changed;

    public void Edit(string property, Action<MaterialAsset> edit, bool allowMerge = true)
    {
        MaterialAsset before = _asset.Clone();
        MaterialAsset after = before.Clone();
        edit(after);
        MaterialSchema.Validate(after);
        if (Serialize(before) == Serialize(after))
            return;
        History.Execute(new PropertyCommand<MaterialAsset>("Set " + property, (Reference.Key, property),
            () => _asset.Clone(), Apply, before, after), allowMerge);
    }

    public void SetOverride<T>(string property, T value, bool allowMerge = true) =>
        Edit(property, asset => asset.SetOverride(property, value), allowMerge);

    public void Revert(string property) => Edit(property, asset => asset.RemoveOverride(property), false);

    public void Refresh()
    {
        try
        {
            string content = File.ReadAllText(Path);
            if (content == _diskContent)
                return;
            if (IsDirty)
            {
                HasExternalConflict = true;
                return;
            }
            Reload();
        }
        catch (IOException)
        {
            HasExternalConflict = true;
        }
    }

    public void Save(bool overwriteExternal = false)
    {
        Refresh();
        if (HasExternalConflict && !overwriteExternal)
            throw new InvalidOperationException("Material changed on disk. Reload it or explicitly overwrite the external changes.");
        _store.Save(Reference, _asset);
        _diskContent = File.ReadAllText(Path);
        HasExternalConflict = false;
        History.MarkSavePoint();
    }

    public void Reload()
    {
        string content = File.ReadAllText(Path);
        MaterialAsset loaded = JsonSerializer.Deserialize<MaterialAsset>(content, MaterialSchema.JsonOptions)
            ?? throw new InvalidDataException("Material document is empty.");
        MaterialSchema.Validate(loaded);
        _store.SetPreview(Reference, null);
        _asset = loaded;
        _diskContent = content;
        HasExternalConflict = false;
        History.Reset(markSaved: true);
        _store.SetPreview(Reference, _asset);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(MaterialAsset value)
    {
        _asset = value.Clone();
        _store.SetPreview(Reference, _asset);
    }

    private void OnChanged(object? sender, EventArgs args) => Changed?.Invoke(this, EventArgs.Empty);
    private static string Serialize(MaterialAsset asset) => JsonSerializer.Serialize(asset, MaterialSchema.JsonOptions);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        History.Changed -= OnChanged;
        _store.SetPreview(Reference, null);
    }
}
