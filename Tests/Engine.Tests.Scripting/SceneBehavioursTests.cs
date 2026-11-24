using Lifetime = Engine.Scene.Lifetime;
using System.Reflection;
using Engine.Core;
using Engine.Scripting;
using Engine.Editor.Documents;
using Engine.Mathematics;
using Engine.Scene;
using Xunit;
using SceneModel = Engine.Scene.Scene;

namespace Engine.Tests.Scripting;

public sealed class SceneBehavioursTests
{
    private static SceneBehavioursModule CreateModule(SceneModel scene)
    {
        SceneBehavioursModule module = new();
        module.Initialize(ApplicationBuilder.Create().AddLoggerSink(new MemoryLogSink()).Build().Context);
        module.AttachScene(scene);
        return module;
    }

    [Fact]
    public void RotatorRotatesAroundLocalAxis()
    {
        SceneModel scene = new("Behaviours");
        GameObject spinning = scene.CreateObject("Spinner");
        Rotator component = spinning.AddComponent<Rotator>();
        component.DegreesPerSecond = 90f;
        using SceneBehavioursModule module = CreateModule(scene);
        module.Update(new FrameTime(1f, 1f, 0f, 1f / 60f, 0));
        module.Update(new FrameTime(1f, 1f, 0f, 1f / 60f, 1));
        float yaw = spinning.Transform.Rotation.ToYawPitchRoll().X;
        Assert.InRange(MathF.Abs(yaw), MathF.PI - 0.02f, MathF.PI + 0.02f);
    }

    [Fact]
    public void DisabledBehaviourDoesNotTick()
    {
        SceneModel scene = new("Behaviours");
        GameObject spinning = scene.CreateObject("Spinner");
        Rotator component = spinning.AddComponent<Rotator>();
        component.DegreesPerSecond = 360f;
        component.Enabled = false;
        using SceneBehavioursModule module = CreateModule(scene);
        module.Update(new FrameTime(1f, 1f, 0f, 1f / 60f, 0));
        Assert.InRange(MathF.Abs(spinning.Transform.Rotation.ToYawPitchRoll().X), 0f, 0.001f);
    }

    [Fact]
    public void LookAtTurnsTowardReferencedTarget()
    {
        SceneModel scene = new("Behaviours");
        GameObject watcher = scene.CreateObject("Watcher");
        LookAt component = watcher.AddComponent<LookAt>();
        component.TurnSpeed = 0f;
        GameObject target = scene.CreateObject("Target");
        target.Transform.Position = new Vector3(0f, 0f, -10f);
        component.Target = new EntityReference(target.Id);
        using SceneBehavioursModule module = CreateModule(scene);
        module.Update(new FrameTime(0f, 0f, 0f, 1f / 60f, 0));
        Assert.True(watcher.Transform.Forward.Z < -0.99f, $"forward {watcher.Transform.Forward}");
    }

    [Fact]
    public void FollowComponentApproachesTarget()
    {
        SceneModel scene = new("Behaviours");
        GameObject follower = scene.CreateObject("Follower");
        follower.Transform.Position = new Vector3(0f, 0f, 0f);
        Follow component = follower.AddComponent<Follow>();
        component.Smoothness = 0f;
        GameObject target = scene.CreateObject("Target");
        target.Transform.Position = new Vector3(5f, 1f, 3f);
        component.Target = new EntityReference(target.Id);
        using SceneBehavioursModule module = CreateModule(scene);
        module.Update(new FrameTime(1f / 60f, 1f / 60f, 0f, 1f / 60f, 0));
        Assert.InRange(follower.Transform.Position.X, 4.99f, 5.01f);
    }

    [Fact]
    public void LifetimeDestroysOwnerAfterSeconds()
    {
        SceneModel scene = new("Behaviours");
        GameObject doomed = scene.CreateObject("Doomed");
        Lifetime component = doomed.AddComponent<Lifetime>();
        component.Seconds = 0.5f;
        using SceneBehavioursModule module = CreateModule(scene);
        module.Update(new FrameTime(0.3f, 0.3f, 0f, 1f / 60f, 0));
        Assert.NotNull(scene.FindById(doomed.Id));
        module.Update(new FrameTime(0.3f, 0.3f, 0f, 1f / 60f, 1));
        Assert.Null(scene.FindById(doomed.Id));
    }

    [Fact]
    public void EntityReferenceTargetSurvivesSceneRoundTripAndRemap()
    {
        SceneModel scene = new("Behaviours");
        GameObject watcher = scene.CreateObject("Watcher");
        LookAt component = watcher.AddComponent<LookAt>();
        GameObject target = scene.CreateObject("Target");
        component.Target = new EntityReference(target.Id);
        component.KeepUpright = false;

        string json = SceneSerializer.ToJson(scene);
        SceneModel loaded = SceneSerializer.FromJson(json, scene.ComponentRegistry);
        LookAt loadedComponent = Assert.IsType<LookAt>(loaded.FindById(watcher.Id)!.GetComponent<LookAt>());
        Assert.Equal(target.Id, loadedComponent.Target.Id);
        Assert.False(loadedComponent.KeepUpright);

        SceneModel remapped = scene.Clone(CloneIdPolicy.GenerateNewIds);
        GameObject newWatcher = remapped.EnumerateHierarchy().First(root => root.Name == "Watcher");
        GameObject newTarget = remapped.EnumerateHierarchy().First(root => root.Name == "Target");
        LookAt remappedComponent = Assert.IsType<LookAt>(newWatcher.GetComponent<LookAt>());
        Assert.Equal(newTarget.Id, remappedComponent.Target.Id);
    }

    [Fact]
    public void ScriptGameObjectAndTransformFieldsSerializeAsEntityReferences()
    {
        string root = Path.Combine(Path.GetTempPath(), "EngineScriptRefTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "RefHolder.cs"),
                """
                using Engine.Scene;

                public sealed class RefHolder : ScriptBehaviour
                {
                    public GameObject? Target;
                    public Transform? Muzzle;
                }
                """);
            using ScriptingModule scripting = new(root);
            scripting.Initialize(ApplicationBuilder.Create().AddLoggerSink(new MemoryLogSink()).Build().Context);
            Assert.True(scripting.LastCompileSuccessful, string.Join(Environment.NewLine, scripting.LastErrors));
            SceneModel scene = new("Refs");
            scripting.AttachScene(scene);
            GameObject holder = scene.CreateObject("Holder");
            GameObject target = scene.CreateObject("Target");
            GameObject muzzleParent = scene.CreateObject("Muzzle");
            Script component = (Script)holder.AddComponent("RefHolder");
            component.Instance!.GetType().GetField("Target")!.SetValue(component.Instance, target);
            component.Instance!.GetType().GetField("Muzzle")!.SetValue(component.Instance, muzzleParent.Transform);

            Dictionary<string, System.Text.Json.JsonElement>? properties = SceneSerializer.CaptureProperties(component, out Dictionary<string, SceneReferenceKind>? kinds);
            Assert.NotNull(properties);
            Assert.Equal(SceneReferenceKind.Entity, kinds!["Target"]);
            Assert.Equal(SceneReferenceKind.Entity, kinds["Muzzle"]);
            Assert.Equal(target.Id, Guid.Parse(properties["Target"].GetString()!));
            Assert.Equal(muzzleParent.Id, Guid.Parse(properties["Muzzle"].GetString()!));

            GameObject holderCopy = scene.CreateObject("HolderCopy");
            Script copy = (Script)holderCopy.AddComponent("RefHolder");
            SceneSerializer.ApplyProperties(copy, properties);
            Assert.Same(target, copy.Instance!.GetType().GetField("Target")!.GetValue(copy.Instance));
            Assert.Same(muzzleParent.Transform, copy.Instance!.GetType().GetField("Muzzle")!.GetValue(copy.Instance));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ScriptReferenceMembersAreInspectable()
    {
        Type inspectorType = typeof(Engine.Editor.Inspection.ComponentPropertyInspector);
        MethodInfo isSupported = inspectorType.GetMethod("IsSupportedType", BindingFlags.Public | BindingFlags.Static)!;
        Assert.True((bool)isSupported.Invoke(null, [typeof(GameObject)])!);
        Assert.True((bool)isSupported.Invoke(null, [typeof(Transform)])!);
        Assert.True((bool)isSupported.Invoke(null, [typeof(EntityReference)])!);
        Assert.True((bool)isSupported.Invoke(null, [typeof(AssetReference)])!);
        Assert.False((bool)isSupported.Invoke(null, [typeof(Component)])!);
    }
}
