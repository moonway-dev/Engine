using Engine.Mathematics;
using Engine.Scene;
using SceneModel = Engine.Scene.Scene;
using Xunit;

namespace Engine.Tests.Rendering;

public class SceneSchemaV2Tests
{
    private sealed class ReferenceComponent : Component
    {
        public EntityReference Entity { get; set; }
        public ComponentReference Component { get; set; }
        public List<EntityReference> Entities { get; set; } = [];
        public AssetReference Asset { get; set; }
        [EntityReference]
        public Guid LegacyEntityId { get; set; }
        [ComponentReference]
        public Guid LegacyComponentId { get; set; }
    }

    private sealed class LifecycleComponent : Component
    {
        public int Attached { get; private set; }
        public int Detached { get; private set; }

        protected override void OnAttach() => Attached++;

        protected override void OnDetach() => Detached++;
    }

    private sealed class AttachStateComponent : Component
    {
        public int Value { get; set; }
        public int ValueObservedOnAttach { get; private set; }

        protected override void OnAttach() => ValueObservedOnAttach = Value;
    }

    private static ComponentRegistry CreateRegistry()
    {
        ComponentRegistry registry = new();
        registry.Register(() => new ReferenceComponent());
        registry.Register(() => new LifecycleComponent());
        registry.Register(() => new AttachStateComponent());
        return registry;
    }

    [Fact]
    public void SchemaV2_RoundTripsIdsStateAndReferences()
    {
        ComponentRegistry registry = CreateRegistry();
        SceneModel scene = new("V2", registry);
        GameObject parent = scene.CreateObject("Parent");
        GameObject child = scene.CreateObject("Child", parent);
        parent.Active = false;
        CameraComponent camera = child.AddComponent<CameraComponent>();
        camera.Enabled = false;
        ReferenceComponent reference = parent.AddComponent<ReferenceComponent>();
        reference.Enabled = false;
        reference.Entity = new EntityReference(child.Id);
        reference.Component = new ComponentReference(camera.Id);
        reference.Entities = [new EntityReference(parent.Id), new EntityReference(child.Id)];
        reference.Asset = new AssetReference("meshes/cube");
        reference.LegacyEntityId = child.Id;
        reference.LegacyComponentId = camera.Id;

        string json = scene.ToJson();
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.GetProperty("version").GetInt32());

        SceneModel restored = SceneModel.FromJson(json, registry);
        GameObject restoredParent = restored.FindById(parent.Id)!;
        GameObject restoredChild = restored.FindById(child.Id)!;
        ReferenceComponent restoredReference = restoredParent.GetComponent<ReferenceComponent>()!;
        CameraComponent restoredCamera = restoredChild.GetComponent<CameraComponent>()!;

        Assert.Equal(parent.Id, restoredParent.Id);
        Assert.Equal(child.Id, restoredChild.Id);
        Assert.Equal(reference.Id, restoredReference.Id);
        Assert.Equal(camera.Id, restoredCamera.Id);
        Assert.False(restoredParent.Active);
        Assert.False(restoredReference.Enabled);
        Assert.False(restoredCamera.Enabled);
        Assert.False(restoredChild.IsActiveInHierarchy);
        Assert.False(restoredCamera.IsActiveInHierarchy);
        Assert.Same(restoredChild, restoredReference.Entity.Resolve(restored));
        Assert.Same(restoredCamera, restoredReference.Component.Resolve(restored));
        Assert.Equal([parent.Id, child.Id], restoredReference.Entities.Select(item => item.Id));
        Assert.Equal("meshes/cube", restoredReference.Asset.Key);
        Assert.Equal(child.Id, restoredReference.LegacyEntityId);
        Assert.Equal(camera.Id, restoredReference.LegacyComponentId);
        Assert.Same(restoredReference, restored.FindComponentById(reference.Id));
    }

    [Fact]
    public void MigrationV1ToV2_PreservesAssetsAndGeneratesStableComponentIds()
    {
        Guid objectId = Guid.NewGuid();
        string v1 = $$"""
        {
          "version": 1,
          "name": "Legacy",
          "objects": [
            {
              "id": "{{objectId}}",
              "name": "Camera",
              "active": false,
              "transform": {
                "position": [1, 2, 3],
                "rotation": [0, 0, 0, 1],
                "scale": [1, 1, 1]
              },
              "components": [
                {
                  "type": "Camera",
                  "properties": {
                    "FieldOfViewY": 1.25,
                    "NearPlane": 0.2,
                    "FarPlane": 900
                  }
                }
              ]
            }
          ]
        }
        """;

        SceneModel first = SceneModel.FromJson(v1);
        SceneModel second = SceneModel.FromJson(v1);
        CameraComponent firstCamera = first.FindById(objectId)!.GetComponent<CameraComponent>()!;
        CameraComponent secondCamera = second.FindById(objectId)!.GetComponent<CameraComponent>()!;

        Assert.Equal(1, first.SourceVersion);
        Assert.Equal(2, first.Version);
        Assert.Equal(firstCamera.Id, secondCamera.Id);
        Assert.NotEqual(Guid.Empty, firstCamera.Id);
        Assert.False(first.FindById(objectId)!.Active);
        Assert.Equal(1.25f, firstCamera.FieldOfViewY, 5);
        Assert.Contains(first.Diagnostics, diagnostic => diagnostic.Code == SceneDiagnosticCode.Migrated);
        Assert.Contains("\"version\": 2", first.ToJson());
    }

    [Fact]
    public void UnknownComponent_RoundTripsPayloadWithoutLoss()
    {
        Guid objectId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();
        string json = $$"""
        {
          "version": 2,
          "name": "Unknown",
          "objects": [
            {
              "id": "{{objectId}}",
              "name": "Object",
              "active": true,
              "transform": {
                "position": [0, 0, 0],
                "rotation": [0, 0, 0, 1],
                "scale": [1, 1, 1]
              },
              "components": [
                {
                  "id": "{{componentId}}",
                  "type": "Plugin.Custom",
                  "enabled": false,
                  "properties": {
                    "nested": { "text": "keep", "values": [1, 2, 3] },
                    "flag": true
                  }
                }
              ]
            }
          ]
        }
        """;

        SceneModel loaded = SceneModel.FromJson(json);
        UnknownComponentPayload unknown = Assert.Single(loaded.UnknownComponents);
        Assert.Equal(objectId, unknown.OwnerId);
        Assert.Equal(componentId, unknown.ComponentId);
        Assert.Equal("Plugin.Custom", unknown.Type);
        Assert.False(unknown.Enabled);
        Assert.Equal("keep", unknown.Properties!["nested"].GetProperty("text").GetString());
        Assert.Equal([1, 2, 3], unknown.Properties["nested"].GetProperty("values").EnumerateArray().Select(value => value.GetInt32()));
        Assert.Contains(loaded.Diagnostics, diagnostic =>
            diagnostic.Code == SceneDiagnosticCode.UnknownComponent
            && diagnostic.ComponentId == componentId);

        SceneModel roundTripped = SceneModel.FromJson(loaded.ToJson());
        UnknownComponentPayload preserved = Assert.Single(roundTripped.UnknownComponents);
        Assert.Equal(componentId, preserved.ComponentId);
        Assert.False(preserved.Enabled);
        Assert.Equal(
            unknown.Properties["nested"].GetProperty("text").GetString(),
            preserved.Properties!["nested"].GetProperty("text").GetString());
        Assert.Equal(
            unknown.Properties["nested"].GetProperty("values").EnumerateArray().Select(value => value.GetInt32()),
            preserved.Properties["nested"].GetProperty("values").EnumerateArray().Select(value => value.GetInt32()));
    }

    [Fact]
    public void UnknownV1Component_IsMigratedAndPreserved()
    {
        Guid objectId = Guid.NewGuid();
        string v1 = $$"""
        {
          "version": 1,
          "name": "LegacyUnknown",
          "objects": [
            {
              "id": "{{objectId}}",
              "name": "Object",
              "active": true,
              "transform": { "position": [0, 0, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1] },
              "components": [
                { "type": "Missing", "properties": { "assetKey": "textures/a", "value": 42 } }
              ]
            }
          ]
        }
        """;

        SceneModel loaded = SceneModel.FromJson(v1);
        UnknownComponentPayload unknown = Assert.Single(loaded.UnknownComponents);
        Assert.NotEqual(Guid.Empty, unknown.ComponentId);
        Assert.Equal("textures/a", unknown.Properties!["assetKey"].GetString());
        Assert.Equal(42, unknown.Properties["value"].GetInt32());

        SceneModel again = SceneModel.FromJson(loaded.ToJson());
        Assert.Equal(unknown.ComponentId, Assert.Single(again.UnknownComponents).ComponentId);
    }

    [Fact]
    public void Clone_PreserveIdsAndGenerateIdsFollowPolicyAndRemapReferences()
    {
        ComponentRegistry registry = CreateRegistry();
        SceneModel scene = new("Source", registry);
        GameObject root = scene.CreateObject("Root");
        GameObject child = scene.CreateObject("Child", root);
        CameraComponent camera = child.AddComponent<CameraComponent>();
        ReferenceComponent links = root.AddComponent<ReferenceComponent>();
        links.Entity = new EntityReference(child.Id);
        links.Component = new ComponentReference(camera.Id);
        links.Entities = [new EntityReference(root.Id), new EntityReference(child.Id)];
        links.LegacyEntityId = child.Id;
        links.LegacyComponentId = camera.Id;

        SceneModel preserved = scene.Clone(CloneIdPolicy.PreserveIds);
        GameObject preservedRoot = preserved.FindById(root.Id)!;
        ReferenceComponent preservedLinks = preservedRoot.GetComponent<ReferenceComponent>()!;
        Assert.Equal(root.Id, preservedRoot.Id);
        Assert.Equal(links.Id, preservedLinks.Id);
        Assert.Equal(child.Id, preservedLinks.Entity.Id);
        Assert.Equal(camera.Id, preservedLinks.Component.Id);

        SceneModel generated = scene.Clone(CloneIdPolicy.GenerateNewIds);
        GameObject generatedRoot = Assert.Single(generated.RootObjects);
        GameObject generatedChild = Assert.Single(generatedRoot.Children);
        ReferenceComponent generatedLinks = generatedRoot.GetComponent<ReferenceComponent>()!;
        CameraComponent generatedCamera = generatedChild.GetComponent<CameraComponent>()!;
        Assert.NotEqual(root.Id, generatedRoot.Id);
        Assert.NotEqual(child.Id, generatedChild.Id);
        Assert.NotEqual(links.Id, generatedLinks.Id);
        Assert.NotEqual(camera.Id, generatedCamera.Id);
        Assert.Equal(generatedChild.Id, generatedLinks.Entity.Id);
        Assert.Equal(generatedCamera.Id, generatedLinks.Component.Id);
        Assert.Equal([generatedRoot.Id, generatedChild.Id], generatedLinks.Entities.Select(item => item.Id));
        Assert.Equal(generatedChild.Id, generatedLinks.LegacyEntityId);
        Assert.Equal(generatedCamera.Id, generatedLinks.LegacyComponentId);
        Assert.Same(generatedChild, generatedLinks.Entity.Resolve(generated));
        Assert.Same(generatedCamera, generatedLinks.Component.Resolve(generated));
    }

    [Fact]
    public void CloneSubtree_GeneratesIdsAndDoesNotLeaveStaleIndexEntries()
    {
        ComponentRegistry registry = CreateRegistry();
        SceneModel scene = new("Duplicate", registry);
        GameObject root = scene.CreateObject("Root");
        GameObject child = scene.CreateObject("Child", root);
        ReferenceComponent links = root.AddComponent<ReferenceComponent>();
        CameraComponent camera = child.AddComponent<CameraComponent>();
        links.Entity = new EntityReference(child.Id);
        links.Component = new ComponentReference(camera.Id);
        int before = scene.AllObjects.Count;

        GameObject duplicate = scene.CloneSubtree(root);
        GameObject duplicateChild = Assert.Single(duplicate.Children);
        ReferenceComponent duplicateLinks = duplicate.GetComponent<ReferenceComponent>()!;

        Assert.Equal(before + 2, scene.AllObjects.Count);
        Assert.NotEqual(root.Id, duplicate.Id);
        Assert.NotEqual(child.Id, duplicateChild.Id);
        Assert.Equal(duplicateChild.Id, duplicateLinks.Entity.Id);
        Assert.Equal(duplicateChild.GetComponent<CameraComponent>()!.Id, duplicateLinks.Component.Id);
        Assert.All(scene.AllObjects, gameObject => Assert.Same(gameObject, scene.FindById(gameObject.Id)));
        Assert.All(scene.AllComponents, component => Assert.Same(component, scene.FindComponentById(component.Id)));
        Assert.Equal(scene.AllObjects.Count, scene.AllObjects.Select(gameObject => gameObject.Id).Distinct().Count());
        Assert.Equal(scene.AllComponents.Count, scene.AllComponents.Select(component => component.Id).Distinct().Count());
    }

    [Fact]
    public void RuntimeComponentApi_AddsAndRemovesByKeyAndTypeWithLifecycle()
    {
        ComponentRegistry registry = CreateRegistry();
        SceneModel scene = new("Runtime", registry);
        GameObject gameObject = scene.CreateObject();

        LifecycleComponent byKey = Assert.IsType<LifecycleComponent>(gameObject.AddComponent("Lifecycle"));
        Assert.Equal(1, byKey.Attached);
        Assert.Same(byKey, scene.FindComponentById(byKey.Id));
        Assert.Same(byKey, gameObject.GetComponent("Lifecycle"));
        Assert.Throws<InvalidOperationException>(() => gameObject.AddComponent(typeof(LifecycleComponent)));
        Assert.True(gameObject.RemoveComponent("Lifecycle"));
        Assert.Equal(1, byKey.Detached);
        Assert.Null(scene.FindComponentById(byKey.Id));

        LifecycleComponent byType = Assert.IsType<LifecycleComponent>(gameObject.AddComponent(typeof(LifecycleComponent)));
        Assert.True(gameObject.RemoveComponent(typeof(LifecycleComponent)));
        Assert.Equal(1, byType.Detached);
        Assert.False(gameObject.RemoveComponent("Lifecycle"));
        Assert.Throws<KeyNotFoundException>(() => gameObject.AddComponent("Missing"));
    }

    [Fact]
    public void Restore_AppliesSerializedStateBeforeOnAttach()
    {
        ComponentRegistry registry = CreateRegistry();
        SceneModel scene = new("Lifecycle", registry);
        GameObject gameObject = scene.CreateObject();
        AttachStateComponent component = gameObject.AddComponent<AttachStateComponent>();
        component.Value = 42;

        SceneModel restored = SceneModel.FromJson(scene.ToJson(), registry);
        AttachStateComponent restoredComponent = Assert.Single(restored.AllComponents.OfType<AttachStateComponent>());

        Assert.Equal(42, restoredComponent.Value);
        Assert.Equal(42, restoredComponent.ValueObservedOnAttach);
    }

    [Fact]
    public void ComponentRegistry_UsesExplicitStableKeys()
    {
        ComponentRegistry registry = new();
        registry.Register(() => new LifecycleComponent(), "engine.lifecycle.v1");
        SceneModel scene = new("Keys", registry);
        GameObject gameObject = scene.CreateObject();
        LifecycleComponent component = Assert.IsType<LifecycleComponent>(gameObject.AddComponent("engine.lifecycle.v1"));

        string json = scene.ToJson();
        Assert.Contains("engine.lifecycle.v1", json);
        Assert.Equal(typeof(LifecycleComponent), scene.ComponentRegistry.GetRegisteredType("engine.lifecycle.v1"));
        Assert.Equal("engine.lifecycle.v1", scene.ComponentRegistry.GetKey(component.GetType()));
    }

    [Fact]
    public void Snapshot_IsTheSharedSaveLoadAndCloneRepresentation()
    {
        ComponentRegistry registry = CreateRegistry();
        SceneModel scene = new("Snapshot", registry);
        GameObject root = scene.CreateObject("Root");
        ReferenceComponent component = root.AddComponent<ReferenceComponent>();
        component.Asset = new AssetReference("asset/key");

        SceneSnapshot snapshot = scene.CaptureSnapshot();
        Assert.Equal(scene.ToJson(), snapshot.ToJson());

        SceneSnapshot parsed = SceneSnapshot.Parse(snapshot.ToJson());
        SceneModel restored = parsed.Restore(CloneIdPolicy.PreserveIds, registry).Scene;
        Assert.Equal(root.Id, Assert.Single(restored.RootObjects).Id);
        Assert.Equal(component.Id, Assert.Single(restored.AllComponents).Id);
        Assert.Equal("asset/key", restored.RootObjects[0].GetComponent<ReferenceComponent>()!.Asset.Key);
    }

    [Fact]
    public void SnapshotParse_RejectsHierarchyCycles()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        string json = $$"""
        {
          "version": 2,
          "name": "Cycle",
          "objects": [
            {
              "id": "{{first}}",
              "name": "First",
              "active": true,
              "parentId": "{{second}}",
              "transform": { "position": [0, 0, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1] },
              "components": []
            },
            {
              "id": "{{second}}",
              "name": "Second",
              "active": true,
              "parentId": "{{first}}",
              "transform": { "position": [0, 0, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1] },
              "components": []
            }
          ]
        }
        """;

        Assert.Throws<InvalidOperationException>(() => SceneSnapshot.Parse(json));
    }

    [Fact]
    public void SceneHost_RaisesOnlyForActualSceneChanges()
    {
        SceneHost host = new();
        SceneModel first = new("First");
        SceneModel second = new("Second");
        List<ActiveSceneChangedEventArgs> changes = [];
        host.ActiveSceneChanged += (_, args) => changes.Add(args);

        host.ActiveScene = first;
        host.ActiveScene = first;
        host.ActiveScene = second;
        host.ActiveScene = null;

        Assert.Equal(3, changes.Count);
        Assert.Null(changes[0].Previous);
        Assert.Same(first, changes[0].Current);
        Assert.Same(first, changes[1].Previous);
        Assert.Same(second, changes[1].Current);
        Assert.Same(second, changes[2].Previous);
        Assert.Null(changes[2].Current);
        Assert.Throws<InvalidOperationException>(() => host.RequireActiveScene());
    }
}

public class PrefabV2Tests
{
    private sealed class PrefabLinkComponent : Component
    {
        public float Value { get; set; }
        public EntityReference TargetObject { get; set; }
        public ComponentReference TargetComponent { get; set; }
    }

    private static ComponentRegistry CreateRegistry()
    {
        ComponentRegistry registry = new();
        registry.Register(() => new PrefabLinkComponent());
        return registry;
    }

    [Fact]
    public void Instantiate_RemapIdsReferencesAndTrackSourceMetadataWithoutStaleIds()
    {
        ComponentRegistry registry = CreateRegistry();
        SceneModel source = new("Source", registry);
        GameObject sourceRoot = source.CreateObject("Root");
        GameObject sourceChild = source.CreateObject("Child", sourceRoot);
        CameraComponent sourceCamera = sourceChild.AddComponent<CameraComponent>();
        PrefabLinkComponent sourceLinks = sourceRoot.AddComponent<PrefabLinkComponent>();
        sourceLinks.Value = 2f;
        sourceLinks.TargetObject = new EntityReference(sourceChild.Id);
        sourceLinks.TargetComponent = new ComponentReference(sourceCamera.Id);
        PrefabAsset asset = PrefabAsset.Capture(sourceRoot, "prefabs/test");

        SceneModel target = new("Target", registry);
        GameObject instanceRoot = asset.Instantiate(target);
        GameObject instanceChild = Assert.Single(instanceRoot.Children);
        PrefabLinkComponent instanceLinks = instanceRoot.GetComponent<PrefabLinkComponent>()!;
        CameraComponent instanceCamera = instanceChild.GetComponent<CameraComponent>()!;

        Assert.NotEqual(sourceRoot.Id, instanceRoot.Id);
        Assert.NotEqual(sourceChild.Id, instanceChild.Id);
        Assert.NotEqual(sourceLinks.Id, instanceLinks.Id);
        Assert.NotEqual(sourceCamera.Id, instanceCamera.Id);
        Assert.Equal(instanceChild.Id, instanceLinks.TargetObject.Id);
        Assert.Equal(instanceCamera.Id, instanceLinks.TargetComponent.Id);
        Assert.Null(target.FindById(sourceRoot.Id));
        Assert.Null(target.FindById(sourceChild.Id));
        Assert.Null(target.FindComponentById(sourceLinks.Id));
        Assert.Null(target.FindComponentById(sourceCamera.Id));
        Assert.Same(instanceRoot, target.FindById(instanceRoot.Id));
        Assert.Same(instanceChild, target.FindById(instanceChild.Id));
        Assert.Same(instanceLinks, target.FindComponentById(instanceLinks.Id));
        Assert.Same(instanceCamera, target.FindComponentById(instanceCamera.Id));
        Assert.Equal("prefabs/test", instanceRoot.PrefabInstance!.AssetKey);
        Assert.Equal(sourceRoot.Id, instanceRoot.PrefabInstance.SourceRootId);
        Assert.Equal(instanceRoot.Id, instanceRoot.PrefabInstance.EntityIdMap[sourceRoot.Id]);
        Assert.Equal(instanceLinks.Id, instanceRoot.PrefabInstance.ComponentIdMap[sourceLinks.Id]);
    }

    [Fact]
    public void Overrides_ApplyRevertAndPersistWithSceneSnapshot()
    {
        ComponentRegistry registry = CreateRegistry();
        SceneModel source = new("Source", registry);
        GameObject sourceRoot = source.CreateObject("Root");
        PrefabLinkComponent sourceComponent = sourceRoot.AddComponent<PrefabLinkComponent>();
        sourceComponent.Value = 3f;
        PrefabAsset asset = PrefabAsset.Capture(sourceRoot, "prefabs/override");
        SceneModel target = new("Target", registry);
        GameObject instanceRoot = asset.Instantiate(target);
        PrefabLinkComponent instanceComponent = instanceRoot.GetComponent<PrefabLinkComponent>()!;

        asset.SetOverride(instanceRoot, sourceRoot.Id, nameof(GameObject.Name), "Changed");
        asset.SetComponentOverride(instanceRoot, sourceComponent.Id, nameof(PrefabLinkComponent.Value), 9f);
        asset.SetComponentOverride(instanceRoot, sourceComponent.Id, nameof(Component.Enabled), false);
        asset.ApplyOverrides(instanceRoot);

        Assert.Equal("Changed", instanceRoot.Name);
        Assert.Equal(9f, instanceComponent.Value, 5);
        Assert.False(instanceComponent.Enabled);
        Assert.Equal(3, instanceRoot.PrefabInstance!.Overrides.Count);

        string sceneJson = target.ToJson();
        SceneModel reloaded = SceneModel.FromJson(sceneJson, registry);
        GameObject reloadedRoot = Assert.Single(reloaded.RootObjects);
        Assert.NotNull(reloadedRoot.PrefabInstance);
        Assert.Equal(3, reloadedRoot.PrefabInstance!.Overrides.Count);
        Assert.Equal("prefabs/override", reloadedRoot.PrefabInstance.AssetKey);
        Assert.Equal(reloadedRoot.Id, reloadedRoot.PrefabInstance.EntityIdMap[sourceRoot.Id]);
        Assert.Equal(
            reloadedRoot.GetComponent<PrefabLinkComponent>()!.Id,
            reloadedRoot.PrefabInstance.ComponentIdMap[sourceComponent.Id]);

        asset.RevertOverrides(instanceRoot);
        Assert.Equal("Root", instanceRoot.Name);
        Assert.Equal(3f, instanceComponent.Value, 5);
        Assert.True(instanceComponent.Enabled);
        Assert.Empty(instanceRoot.PrefabInstance.Overrides);
    }

    [Fact]
    public void RevertReferenceOverride_RestoresRemappedInstanceReference()
    {
        ComponentRegistry registry = CreateRegistry();
        SceneModel source = new("Source", registry);
        GameObject sourceRoot = source.CreateObject("Root");
        GameObject sourceChild = source.CreateObject("Child", sourceRoot);
        PrefabLinkComponent sourceComponent = sourceRoot.AddComponent<PrefabLinkComponent>();
        sourceComponent.TargetObject = new EntityReference(sourceChild.Id);
        PrefabAsset asset = PrefabAsset.Capture(sourceRoot, "prefabs/reference");
        SceneModel target = new("Target", registry);
        GameObject instanceRoot = asset.Instantiate(target);
        GameObject instanceChild = Assert.Single(instanceRoot.Children);
        PrefabLinkComponent instanceComponent = instanceRoot.GetComponent<PrefabLinkComponent>()!;

        asset.SetComponentOverride(
            instanceRoot,
            sourceComponent.Id,
            nameof(PrefabLinkComponent.TargetObject),
            EntityReference.Empty);
        asset.ApplyOverrides(instanceRoot);
        Assert.True(instanceComponent.TargetObject.IsEmpty);

        asset.RevertOverrides(instanceRoot);
        Assert.Equal(instanceChild.Id, instanceComponent.TargetObject.Id);
        Assert.Same(instanceChild, instanceComponent.TargetObject.Resolve(target));
    }

    [Fact]
    public void ApplyInstanceToPrefab_UpdatesAssetAndClearsOverrides()
    {
        ComponentRegistry registry = CreateRegistry();
        SceneModel source = new("Source", registry);
        GameObject sourceRoot = source.CreateObject("Root");
        PrefabLinkComponent sourceComponent = sourceRoot.AddComponent<PrefabLinkComponent>();
        sourceComponent.Value = 1f;
        PrefabAsset asset = PrefabAsset.Capture(sourceRoot, "prefabs/apply");
        SceneModel target = new("Target", registry);
        GameObject first = asset.Instantiate(target);

        asset.SetComponentOverride(first, sourceComponent.Id, nameof(PrefabLinkComponent.Value), 7f);
        asset.ApplyOverrides(first);
        asset.ApplyInstanceToPrefab(first);

        Assert.Empty(first.PrefabInstance!.Overrides);
        SceneModel anotherTarget = new("Another", registry);
        GameObject second = asset.Instantiate(anotherTarget);
        Assert.Equal(7f, second.GetComponent<PrefabLinkComponent>()!.Value, 5);
    }

    [Fact]
    public void PrefabV1_LoadsAndInstantiatesWithGeneratedIds()
    {
        Guid sourceId = Guid.NewGuid();
        string v1 = $$"""
        {
          "version": 1,
          "name": "LegacyPrefab",
          "objects": [
            {
              "id": "{{sourceId}}",
              "name": "LegacyRoot",
              "active": true,
              "transform": { "position": [1, 2, 3], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1] },
              "components": [
                { "type": "Camera", "properties": { "FieldOfViewY": 1.1, "NearPlane": 0.25, "FarPlane": 100 } }
              ]
            }
          ]
        }
        """;
        PrefabAsset asset = PrefabAsset.FromJson(v1, "prefabs/legacy");
        SceneModel target = new("Target");

        GameObject root = asset.Instantiate(target);
        Assert.NotEqual(sourceId, root.Id);
        Assert.Equal("LegacyRoot", root.Name);
        Assert.Equal(1.1f, root.GetComponent<CameraComponent>()!.FieldOfViewY, 5);
        Assert.Null(target.FindById(sourceId));
        Assert.Equal(sourceId, root.PrefabInstance!.SourceRootId);
    }
}
