using Engine.Assets;
using Engine.Editor;
using Engine.Editor.Documents;
using Engine.Rendering;
using Xunit;
using SceneModel = Engine.Scene.Scene;

namespace Engine.Tests.Rendering;

public sealed class MaterialDocumentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EngineMaterials", Guid.NewGuid().ToString("N"));

    public MaterialDocumentTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void OverrideRevertUndoRedo_PreservesInheritanceAndSavePoint()
    {
        MaterialAssetStore store = new(_root);
        MaterialAsset parent = new();
        parent.SetOverride(nameof(MaterialProperties.Metallic), 0.7f);
        var parentReference = store.Create("parent.mat", parent);
        var reference = store.Create("variant.mat", new MaterialAsset { Parent = parentReference });
        using MaterialDocument document = new(store, reference);
        Assert.False(document.IsDirty);
        Assert.Equal(0.7f, document.Effective.Metallic);
        document.SetOverride(nameof(MaterialProperties.Metallic), 0.2f);
        Assert.True(document.IsDirty);
        Assert.Equal(0.2f, document.Effective.Metallic);
        document.Save();
        Assert.False(document.IsDirty);
        document.Revert(nameof(MaterialProperties.Metallic));
        Assert.Equal(0.7f, document.Effective.Metallic);
        document.History.Undo();
        Assert.Equal(0.2f, document.Effective.Metallic);
        Assert.False(document.IsDirty);
        document.History.Redo();
        Assert.Equal(0.7f, document.Effective.Metallic);
    }

    [Fact]
    public void ExternalChange_DoesNotOverwriteUnsavedPreview()
    {
        MaterialAssetStore store = new(_root);
        var reference = store.Create("material.mat", new MaterialAsset());
        using MaterialDocument document = new(store, reference);
        document.SetOverride(nameof(MaterialProperties.Roughness), 0.3f);
        MaterialAsset external = new();
        external.SetOverride(nameof(MaterialProperties.Roughness), 0.9f);
        File.WriteAllText(document.Path, System.Text.Json.JsonSerializer.Serialize(external, MaterialSchema.JsonOptions));
        document.Refresh();
        Assert.True(document.HasExternalConflict);
        Assert.Equal(0.3f, document.Effective.Roughness);
        Assert.Throws<InvalidOperationException>(() => document.Save());
        Assert.True(document.IsDirty);
        document.Save(overwriteExternal: true);
        Assert.False(document.IsDirty);
        Assert.False(document.HasExternalConflict);
        Assert.Equal(0.3f, store.Load(reference).GetOverride<float>(nameof(MaterialProperties.Roughness)));
    }

    [Fact]
    public void ActiveHistoryAndSave_AreIndependentOfScene()
    {
        SceneModel scene = new("Main");
        using EditorContext context = new(new SceneDocument(new DelegatingSceneHost(scene), scene), _root);
        context.Document.SaveAs(Path.Combine(_root, "main.scene"));
        context.Hierarchy.Create();
        var reference = context.CreateMaterial("material.mat");
        MaterialDocument material = context.SelectedMaterial!;
        material.SetOverride(nameof(MaterialProperties.Metallic), 0.8f);
        Assert.Same(material.History, context.History);
        context.Undo();
        Assert.Equal(0f, material.Effective.Metallic);
        Assert.True(context.Document.IsDirty);
        context.Redo();
        context.SaveActive();
        Assert.False(material.IsDirty);
        Assert.True(context.Document.IsDirty);
        context.SelectScene();
        Assert.Same(context.Document.History, context.History);
        context.Undo();
        Assert.Empty(context.Document.Scene.RootObjects);
        context.SelectMaterial(reference);
        Assert.Equal(0.8f, context.SelectedMaterial!.Effective.Metallic);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
