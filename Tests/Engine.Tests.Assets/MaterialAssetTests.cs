using System.Text.Json;
using Engine.Assets;
using Engine.Mathematics;
using Engine.Rendering;
using Engine.Scene;
using Engine.RHI;
using Xunit;

namespace Engine.Tests.Assets;

public sealed class MaterialAssetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EngineMaterialTests", Guid.NewGuid().ToString("N"));
    private MaterialAssetStore Store() => new(_root) { RefreshInterval = TimeSpan.Zero };

    [Fact]
    public void Inheritance_UsesPresenceAndExplicitTextureClear()
    {
        MaterialAssetStore store = Store();
        MaterialAsset parent = new();
        parent.SetOverride("Metallic", 0.8f);
        parent.SetOverride("AlbedoTexture", new AssetReference("color.png"));
        AssetReference parentRef = store.Create("parent.material", parent);
        MaterialAsset child = new() { Parent = parentRef };
        child.SetOverride("Roughness", 0f);
        child.SetOverride("AlbedoTexture", AssetReference.Empty);
        AssetReference childRef = store.Create("child.material", child);
        MaterialProperties resolved = store.Resolve(childRef).Properties;
        Assert.Equal(0.8f, resolved.Metallic);
        Assert.Equal(0f, resolved.Roughness);
        Assert.True(resolved.AlbedoTexture.IsEmpty);
        Assert.False(resolved.LegacyEmission);
        child.RemoveOverride("AlbedoTexture");
        store.Save(childRef, child);
        Assert.Equal("color.png", store.Resolve(childRef).Properties.AlbedoTexture.Key);
    }

    [Fact]
    public void SaveAndPreview_InvalidateDescendantsAndCloneSnapshots()
    {
        MaterialAssetStore store = Store();
        AssetReference parent = store.Create("parent.material", new MaterialAsset());
        AssetReference child = store.Create("child.material", new MaterialAsset { Parent = parent });
        ResolvedMaterial before = store.Resolve(child);
        MaterialAsset edit = store.Load(parent);
        edit.SetOverride("Metallic", 0.75f);
        Assert.Equal(0f, store.Resolve(parent).Properties.Metallic);
        store.SetPreview(parent, edit);
        edit.SetOverride("Metallic", 0.25f);
        ResolvedMaterial preview = store.Resolve(child);
        Assert.Equal(0.75f, preview.Properties.Metallic);
        Assert.True(preview.Revision > before.Revision);
        store.SetPreview(parent, null);
        Assert.Equal(0f, store.Resolve(child).Properties.Metallic);
        store.Save(parent, edit);
        Assert.Equal(0.25f, store.Resolve(child).Properties.Metallic);
        Assert.Equal(0.25f, Store().Resolve(child).Properties.Metallic);
    }

    [Fact]
    public void CyclesAndMalformedExternalChanges_KeepLastGoodResolution()
    {
        MaterialAssetStore store = Store();
        AssetReference parent = store.Create("parent.material", new MaterialAsset());
        AssetReference child = store.Create("child.material", new MaterialAsset { Parent = parent });
        ResolvedMaterial good = store.Resolve(child);
        store.Save(parent, new MaterialAsset { Parent = child });
        Assert.Equal(good, store.Resolve(child));
        Assert.Contains(store.Diagnostics, message => message.Contains("cycle"));
        store.Save(parent, new MaterialAsset());
        good = store.Resolve(child);
        File.WriteAllText(store.ResolvePath(parent), "{invalid");
        store.Refresh();
        Assert.Equal(good, store.Resolve(child));
        Assert.NotEmpty(store.Diagnostics);
    }

    [Fact]
    public void ExternalParentChange_UpdatesDescendantRevision()
    {
        MaterialAssetStore store = Store();
        AssetReference parent = store.Create("parent.material", new MaterialAsset());
        AssetReference child = store.Create("child.material", new MaterialAsset { Parent = parent });
        ResolvedMaterial before = store.Resolve(child);
        MaterialAsset updated = new();
        updated.SetOverride("Roughness", 0.12f);
        File.WriteAllText(store.ResolvePath(parent), JsonSerializer.Serialize(updated, MaterialSchema.JsonOptions));
        store.Refresh();
        ResolvedMaterial after = store.Resolve(child);
        Assert.Equal(0.12f, after.Properties.Roughness);
        Assert.True(after.Revision > before.Revision);
        Assert.Equal(after.Revision, store.Resolve(child).Revision);
    }

    [Fact]
    public void Catalog_StableGuidAndRenameAndPathCompatibility()
    {
        MaterialAssetStore store = Store();
        AssetReference reference = store.Create("one.material", new MaterialAsset());
        Assert.StartsWith("guid:", reference.Key);
        Assert.Equal(reference, Store().GetReference("one.material"));
        store.Catalog.Rename(reference, "nested/two.material");
        Assert.Equal(reference, Store().GetReference("nested/two.material"));
        Assert.Equal(store.ResolvePath(reference), store.ResolvePath(new AssetReference("nested/two.material")));
        Assert.Throws<ArgumentException>(() => store.ResolvePath(new AssetReference("../escape.material")));
    }

    [Fact]
    public void Catalog_DuplicateMetadataIsDiagnosed()
    {
        MaterialAssetStore store = Store();
        store.Create("one.material", new MaterialAsset());
        File.Copy(Path.Combine(_root, "one.material"), Path.Combine(_root, "two.material"));
        File.Copy(Path.Combine(_root, "one.material.meta"), Path.Combine(_root, "two.material.meta"));
        store.Catalog.Refresh();
        Assert.Contains(store.Catalog.Diagnostics, message => message.Contains("Duplicate"));
    }

    [Fact]
    public void Schema_RoundTripsVectorsChannelsAndTextureClear()
    {
        MaterialAssetStore store = Store();
        MaterialAsset asset = new();
        asset.SetOverride("UvScale", new Vector2(2f, 3f));
        asset.SetOverride("EmissionColor", new Vector3(0.1f, 0.2f, 0.3f));
        asset.SetOverride("MetallicChannel", TextureChannel.B);
        asset.SetOverride<AssetReference?>("EmissionTexture", null);
        AssetReference reference = store.Create("test.material", asset);
        MaterialProperties properties = Store().Resolve(reference).Properties;
        Assert.Equal(new Vector2(2f, 3f), properties.UvScale);
        Assert.Equal(new Vector3(0.1f, 0.2f, 0.3f), properties.EmissionColor);
        Assert.Equal(TextureChannel.B, properties.MetallicChannel);
        Assert.True(properties.EmissionTexture.IsEmpty);
        Assert.Throws<InvalidDataException>(() => asset.SetOverride("Unknown", 1f));
        Assert.Throws<InvalidDataException>(() => asset.SetOverride("MetallicChannel", (TextureChannel)12));
    }

    [Fact]
    public void LegacyComponent_PreservesExistingValuesAndEmissionMode()
    {
        MeshRendererComponent component = new()
        {
            Metallic = 0.4f,
            Roughness = 0.2f,
            EmissiveIntensity = 2f,
            AlbedoTexture = "legacy.png",
        };
        MaterialProperties properties = MaterialProperties.FromComponent(component);
        Assert.True(properties.LegacyEmission);
        Assert.Equal(component.Metallic, properties.Metallic);
        Assert.Equal(component.Roughness, properties.Roughness);
        Assert.Equal(component.EmissiveIntensity, properties.EmissiveIntensity);
        Assert.Equal(component.AlbedoTexture, properties.AlbedoTexture);
        Assert.Equal(Vector3.One, properties.EmissionColor);
        Assert.Equal(TextureFormat.R8G8B8A8Srgb, AssetDatabase.GetTextureFormat(TextureSemantic.Emission));
    }

    [Fact]
    public void TextureReload_SameGuidChangesRevisionAndKeepsLastGoodOnDecodeFailure()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "texture.png");
        File.WriteAllBytes(path, ImageLoaderTests.CreatePng(1, 1));
        using Engine.Backends.Null.NullDevice device = new();
        using AssetDatabase database = new(device, _root);
        AssetReference reference = database.Catalog.GetReference("texture.png");
        ITexture original = database.LoadTexture(reference, TextureSemantic.Emission);
        long revision = database.GetTextureRevision(reference);
        Assert.Equal(TextureFormat.R8G8B8A8Srgb, original.Format);
        File.WriteAllBytes(path, ImageLoaderTests.CreatePng(2, 1));
        Thread.Sleep(300);
        database.Refresh();
        ITexture replaced = database.LoadTexture(reference, TextureSemantic.Emission);
        Assert.NotSame(original, replaced);
        Assert.Equal(2u, replaced.Width);
        Assert.True(database.GetTextureRevision(reference) > revision);
        revision = database.GetTextureRevision(reference);
        List<string> diagnostics = [];
        database.Diagnostic += diagnostics.Add;
        File.WriteAllText(path, "invalid image");
        Thread.Sleep(300);
        database.Refresh();
        Assert.Same(replaced, database.LoadTexture(reference, TextureSemantic.Emission));
        Assert.Equal(revision, database.GetTextureRevision(reference));
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void TextureReload_UnchangedBrokenFileIsNotRetriedUntilForced()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "retry.png");
        File.WriteAllBytes(path, ImageLoaderTests.CreatePng(1, 1));
        using Engine.Backends.Null.NullDevice device = new();
        using AssetDatabase database = new(device, _root) { RefreshInterval = TimeSpan.Zero };
        ITexture original = database.LoadTexture("retry.png");
        List<string> diagnostics = [];
        database.Diagnostic += diagnostics.Add;
        File.WriteAllText(path, "broken");
        database.Refresh();
        Assert.Single(diagnostics);
        database.Refresh();
        Assert.Single(diagnostics);
        Assert.Same(original, database.LoadTexture("retry.png"));
        database.Refresh(true);
        Assert.Equal(2, diagnostics.Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
