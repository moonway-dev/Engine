using Engine.Mathematics;
using Engine.Scene;
using SceneModel = Engine.Scene.Scene;
using Xunit;

namespace Engine.Tests.Rendering;

public class SceneObjectModelTests
{
    [Fact]
    public void CreateObject_RegistersInScene()
    {
        SceneModel scene = new("Test");
        GameObject gameObject = scene.CreateObject("Player");
        Assert.Equal("Player", gameObject.Name);
        Assert.Same(gameObject, scene.FindByName("Player"));
        Assert.Same(gameObject, scene.FindById(gameObject.Id));
        Assert.Single(scene.RootObjects);
    }

    [Fact]
    public void AddComponent_DuplicateType_Throws()
    {
        SceneModel scene = new("Test");
        GameObject gameObject = scene.CreateObject("Object");
        gameObject.AddComponent<CameraComponent>();
        Assert.Throws<InvalidOperationException>(() => gameObject.AddComponent<CameraComponent>());
        Assert.NotNull(gameObject.GetComponent<CameraComponent>());
        Assert.True(gameObject.TryGetComponent(out CameraComponent _));
    }

    [Fact]
    public void RemoveComponent_Works()
    {
        SceneModel scene = new("Test");
        GameObject gameObject = scene.CreateObject("Object");
        gameObject.AddComponent<CameraComponent>();
        Assert.True(gameObject.RemoveComponent<CameraComponent>());
        Assert.Null(gameObject.GetComponent<CameraComponent>());
        Assert.False(gameObject.RemoveComponent<CameraComponent>());
    }

    [Fact]
    public void Parenting_UpdatesHierarchyAndRoots()
    {
        SceneModel scene = new("Test");
        GameObject parent = scene.CreateObject("Parent");
        GameObject child = scene.CreateObject("Child");
        Assert.Equal(2, scene.RootObjects.Count);

        child.SetParent(parent);
        Assert.Same(parent, child.Parent);
        Assert.Single(parent.Children);
        Assert.Single(scene.RootObjects);

        child.SetParent(null);
        Assert.Null(child.Parent);
        Assert.Equal(2, scene.RootObjects.Count);
    }

    [Fact]
    public void Parenting_ToOwnDescendant_Throws()
    {
        SceneModel scene = new("Test");
        GameObject parent = scene.CreateObject("Parent");
        GameObject child = scene.CreateObject("Child");
        child.SetParent(parent);
        Assert.Throws<InvalidOperationException>(() => parent.SetParent(child));
        Assert.Throws<InvalidOperationException>(() => parent.SetParent(parent));
    }

    [Fact]
    public void Remove_RemovesSubtree()
    {
        SceneModel scene = new("Test");
        GameObject parent = scene.CreateObject("Parent");
        GameObject child = scene.CreateObject("Child");
        child.SetParent(parent);
        Guid childId = child.Id;

        scene.Remove(parent);
        Assert.Null(scene.FindById(parent.Id));
        Assert.Null(scene.FindById(childId));
        Assert.Empty(scene.AllObjects);
        Assert.Empty(scene.RootObjects);
    }

    [Fact]
    public void ActiveInHierarchy_ConsidersParents()
    {
        SceneModel scene = new("Test");
        GameObject parent = scene.CreateObject("Parent");
        GameObject child = scene.CreateObject("Child");
        child.SetParent(parent);
        Assert.True(child.IsActiveInHierarchy);
        parent.Active = false;
        Assert.False(child.IsActiveInHierarchy);
        Assert.True(child.Active);
    }
}

public class TransformTests
{
    [Fact]
    public void LocalTRS_ProducesExpectedMatrix()
    {
        SceneModel scene = new("Test");
        GameObject gameObject = scene.CreateObject("Object");
        gameObject.Transform.SetLocalTRS(
            new Vector3(1f, 2f, 3f),
            Quaternion.FromAxisAngle(Vector3.UnitY, MathUtil.HalfPi),
            new Vector3(2f, 2f, 2f));
        MathAssert.Equal(new Vector3(1f, 2f, 3f), gameObject.Transform.Position);
    }

    [Fact]
    public void Child_InheritsParentTransform()
    {
        SceneModel scene = new("Test");
        GameObject parent = scene.CreateObject("Parent");
        GameObject child = scene.CreateObject("Child");
        child.SetParent(parent);

        parent.Transform.Position = new Vector3(10f, 0f, 0f);
        child.Transform.LocalPosition = new Vector3(0f, 1f, 0f);

        MathAssert.Equal(new Vector3(10f, 1f, 0f), child.Transform.Position);
        MathAssert.Equal(new Vector3(0f, 1f, 0f), child.Transform.LocalPosition);
    }

    [Fact]
    public void WorldPosition_Set_AccountsForParent()
    {
        SceneModel scene = new("Test");
        GameObject parent = scene.CreateObject("Parent");
        GameObject child = scene.CreateObject("Child");
        child.SetParent(parent);
        parent.Transform.Position = new Vector3(5f, 5f, 5f);

        child.Transform.Position = new Vector3(7f, 5f, 5f);
        MathAssert.Equal(new Vector3(2f, 0f, 0f), child.Transform.LocalPosition);
    }

    [Fact]
    public void DirtyPropagation_UpdatesChildWhenParentMoves()
    {
        SceneModel scene = new("Test");
        GameObject parent = scene.CreateObject("Parent");
        GameObject child = scene.CreateObject("Child");
        child.SetParent(parent);

        MathAssert.Equal(Vector3.Zero, child.Transform.Position);
        parent.Transform.Position = new Vector3(3f, 4f, 5f);
        MathAssert.Equal(new Vector3(3f, 4f, 5f), child.Transform.Position);
    }

    [Fact]
    public void Rotation_ComposesWithParent()
    {
        SceneModel scene = new("Test");
        GameObject parent = scene.CreateObject("Parent");
        GameObject child = scene.CreateObject("Child");
        child.SetParent(parent);
        parent.Transform.LocalRotation = Quaternion.FromAxisAngle(Vector3.UnitY, MathUtil.HalfPi);
        child.Transform.LocalRotation = Quaternion.Identity;

        MathAssert.Equal(new Vector3(-1f, 0f, 0f), child.Transform.Forward);
        Quaternion childWorld = child.Transform.Rotation;
        MathAssert.Equal(new Vector3(-1f, 0f, 0f), childWorld.Rotate(Vector3.Forward));
    }
}

public class SceneSerializationTests
{
    private sealed class TestDataComponent : Component
    {
        public float Speed { get; set; } = 5f;
        public Vector3 Direction { get; set; } = new(1f, 0f, 0f);
        public string Label { get; set; } = "default";
        public LightKind Kind { get; set; } = LightKind.Point;
        public int Nested { get; set; }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsScene()
    {
        SceneModel original = new("Main");
        GameObject parent = original.CreateObject("Parent");
        parent.Transform.Position = new Vector3(1f, 2f, 3f);
        parent.Transform.LocalRotation = Quaternion.FromAxisAngle(Vector3.UnitY, 0.5f);
        parent.Transform.LocalScale = new Vector3(2f, 2f, 2f);
        LightComponent light = parent.AddComponent<LightComponent>();
        light.Intensity = 3.5f;
        light.Color = new Vector3(1f, 0.5f, 0.25f);

        GameObject child = original.CreateObject("Child");
        child.SetParent(parent);
        child.Transform.Position = new Vector3(0f, 5f, 0f);
        CameraComponent camera = child.AddComponent<CameraComponent>();
        camera.FieldOfViewY = 1.1f;

        Guid parentId = parent.Id;
        Guid childId = child.Id;
        string json = original.ToJson();

        SceneModel loaded = SceneModel.FromJson(json);
        Assert.Equal("Main", loaded.Name);
        GameObject loadedParent = Assert.Single(loaded.RootObjects);
        Assert.Equal(parentId, loadedParent.Id);
        Assert.Equal("Parent", loadedParent.Name);
        Assert.Equal(childId, loadedParent.Children[0].Id);
        Assert.Equal(2, loaded.AllObjects.Count);

        MathAssert.Equal(new Vector3(1f, 2f, 3f), loadedParent.Transform.Position);
        LightComponent loadedLight = Assert.Single(loadedParent.Components.OfType<LightComponent>());
        Assert.Equal(3.5f, loadedLight.Intensity, 5);
        MathAssert.Equal(new Vector3(1f, 0.5f, 0.25f), loadedLight.Color);

        GameObject loadedChild = loadedParent.Children[0];
        MathAssert.Equal(new Vector3(0f, 5f, 0f), loadedChild.Transform.Position);
        CameraComponent loadedCamera = Assert.Single(loadedChild.Components.OfType<CameraComponent>());
        Assert.Equal(1.1f, loadedCamera.FieldOfViewY, 5);
    }

    [Fact]
    public void SaveAndLoad_RegisteredCustomComponent()
    {
        ComponentRegistry.RegisterGlobal(() => new TestDataComponent());
        SceneModel scene = new("Custom");
        GameObject gameObject = scene.CreateObject("WithTestData");
        TestDataComponent data = gameObject.AddComponent<TestDataComponent>();
        data.Speed = 12.5f;
        data.Direction = new Vector3(0f, 1f, 0f);
        data.Label = "custom";
        data.Kind = LightKind.Directional;

        string json = scene.ToJson();
        SceneModel loaded = SceneModel.FromJson(json);
        GameObject loadedObject = Assert.Single(loaded.RootObjects);
        TestDataComponent? restored = loadedObject.GetComponent<TestDataComponent>();
        Assert.NotNull(restored);
        Assert.Equal(12.5f, restored!.Speed, 5);
        MathAssert.Equal(new Vector3(0f, 1f, 0f), restored.Direction);
        Assert.Equal("custom", restored.Label);
        Assert.Equal(LightKind.Directional, restored.Kind);
    }

    [Fact]
    public void FromJson_NewerVersion_Throws()
    {
        string json = """{"version": 99, "name": "Future", "objects": []}""";
        Assert.Throws<InvalidOperationException>(() => SceneModel.FromJson(json));
    }
}

public class PrefabTests
{
    [Fact]
    public void SaveAndInstantiate_CreatesNewIdsAndPreservesHierarchy()
    {
        string path = Path.Combine(Path.GetTempPath(), "EnginePrefabTests", Guid.NewGuid().ToString("N"), "cube.prefab");
        try
        {
            SceneModel source = new("PrefabSource");
            GameObject root = source.CreateObject("PrefabRoot");
            root.Transform.Position = new Vector3(1f, 2f, 3f);
            LightComponent light = root.AddComponent<LightComponent>();
            light.Intensity = 2f;
            GameObject child = source.CreateObject("PrefabChild");
            child.SetParent(root);
            child.Transform.Position = new Vector3(0f, 1f, 0f);
            CameraComponent camera = child.AddComponent<CameraComponent>();
            camera.NearPlane = 0.25f;

            Prefab.Save(root, path);

            SceneModel target = new("Target");
            GameObject instantiated = Prefab.Instantiate(target, path);
            Assert.Equal("PrefabRoot", instantiated.Name);
            Assert.NotEqual(root.Id, instantiated.Id);
            MathAssert.Equal(new Vector3(1f, 2f, 3f), instantiated.Transform.Position);
            LightComponent? instantiatedLight = instantiated.GetComponent<LightComponent>();
            Assert.NotNull(instantiatedLight);
            Assert.Equal(2f, instantiatedLight!.Intensity, 5);
            GameObject instantiatedChild = Assert.Single(instantiated.Children);
            Assert.Equal("PrefabChild", instantiatedChild.Name);
            Assert.NotEqual(child.Id, instantiatedChild.Id);
            CameraComponent? instantiatedCamera = instantiatedChild.GetComponent<CameraComponent>();
            Assert.NotNull(instantiatedCamera);
            Assert.Equal(0.25f, instantiatedCamera!.NearPlane, 5);
            Assert.Equal(2, target.AllObjects.Count);
            Assert.Equal(2, target.AllObjects.Select(gameObject => gameObject.Id).Distinct().Count());
            Assert.All(target.AllObjects, gameObject => Assert.Same(gameObject, target.FindById(gameObject.Id)));
            Assert.Single(target.RootObjects);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
