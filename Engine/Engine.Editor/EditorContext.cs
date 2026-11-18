using Engine.Assets;
using Engine.Rendering;
using Engine.Editor.Commands;
using Engine.Editor.Documents;
using Engine.Scene;
using Engine.Mathematics;

namespace Engine.Editor;

public sealed class EditorContext : IDisposable
{
    private bool _disposed;

    public SceneDocument Document { get; }

    private readonly Dictionary<string, MaterialDocument> _materials = new(StringComparer.OrdinalIgnoreCase);

    public MaterialAssetStore Materials { get; }
    public AssetDatabase? ModelAssets { get; set; }
    public MaterialDocument? SelectedMaterial { get; private set; }
    public CommandHistory History => SelectedMaterial?.History ?? Document.History;
    public string ActiveTitle => SelectedMaterial?.Title ?? Document.Title;
    public string? Error { get; set; }
    public bool Undo() => History.Undo();
    public bool Redo() => History.Redo();
    public void SaveActive()
    {
        if (SelectedMaterial is { } material)
            material.Save();
        else
            Document.Save();
    }

    public void SaveAll()
    {
        foreach (MaterialDocument material in _materials.Values.Where(value => value.IsDirty))
            material.Save();
        if (Document.IsDirty)
            Document.Save();
    }

    public void SelectMaterial(AssetReference reference)
    {
        AssetReference canonical = Materials.GetReference(Materials.ResolvePath(reference));
        if (!_materials.TryGetValue(canonical.Key, out MaterialDocument? document))
        {
            document = new MaterialDocument(Materials, canonical);
            _materials.Add(canonical.Key, document);
        }
        History.EndMergeGroup();
        SelectedMaterial = document;
    }

    public void SelectScene()
    {
        History.EndMergeGroup();
        SelectedMaterial = null;
    }

    public void RefreshMaterials()
    {
        Materials.Refresh();
        foreach (MaterialDocument document in _materials.Values)
            document.Refresh();
    }

    public AssetReference CreateMaterial(string path, AssetReference parent = default, MaterialProperties? properties = null)
    {
        MaterialAsset asset = new() { Parent = parent };
        if (properties is not null)
            foreach (string name in MaterialSchema.PropertyNames)
            {
                object? value = typeof(MaterialProperties).GetProperty(name)!.GetValue(properties);
                asset.SetOverride(name, value);
            }
        AssetReference reference = Materials.Create(path, asset);
        SelectMaterial(reference);
        return reference;
    }

    public void AssignMaterial(Guid componentId, AssetReference reference)
    {
        MeshRendererComponent Resolve() => Document.Scene.FindComponentById(componentId) as MeshRendererComponent
            ?? throw new InvalidOperationException("Mesh renderer no longer exists.");
        AssetReference value = reference.IsEmpty ? reference : Materials.GetReference(Materials.ResolvePath(reference));
        Document.Execute(new PropertyCommand<AssetReference>("Assign Material", (componentId, "MaterialAsset"),
            () => Resolve().MaterialAsset, next => Resolve().MaterialAsset = next, Resolve().MaterialAsset, value), false);
    }

    public HierarchyCommands Hierarchy { get; }

    public TransformMode TransformMode { get; set; } = TransformMode.Translate;

    public TransformSpace TransformSpace { get; set; } = TransformSpace.Local;

    public const float MinimumScaleMagnitude = 0.01f;

    public static Vector3 ClampScale(Vector3 scale) => new(
        ClampScaleComponent(scale.X),
        ClampScaleComponent(scale.Y),
        ClampScaleComponent(scale.Z));

    private static float ClampScaleComponent(float value)
    {
        if (!float.IsFinite(value))
            return value < 0f ? -MinimumScaleMagnitude : MinimumScaleMagnitude;
        if (MathF.Abs(value) >= MinimumScaleMagnitude)
            return value;
        return value < 0f ? -MinimumScaleMagnitude : MinimumScaleMagnitude;
    }

    public string AssetsRoot { get; }

    public EditorContext(SceneDocument document, string assetsRoot, MaterialAssetStore? materials = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        AssetsRoot = materials?.Catalog.RootDirectory ?? Path.GetFullPath(assetsRoot ?? throw new ArgumentNullException(nameof(assetsRoot)));
        Materials = materials ?? new MaterialAssetStore(AssetsRoot);
        Hierarchy = new HierarchyCommands(document);
        Document.SelectionChanged += OnSceneSelectionChanged;
        Document.SceneChanged += OnSceneChanged;
    }

    private void OnSceneSelectionChanged(object? sender, EventArgs args) => SelectScene();
    private void OnSceneChanged(object? sender, SceneChangedEventArgs args) => SelectScene();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Document.SelectionChanged -= OnSceneSelectionChanged;
        Document.SceneChanged -= OnSceneChanged;
        foreach (MaterialDocument document in _materials.Values)
            document.Dispose();
        Document.Dispose();
    }
}

public enum TransformMode
{
    Translate,
    Rotate,
    Scale,
}
