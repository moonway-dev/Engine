using Camera = Engine.Rendering.Camera;
using Light = Engine.Rendering.Light;
using System.Reflection;
using Engine.Editor;
using Engine.Editor.Documents;
using Engine.Editor.Inspection;
using Engine.Mathematics;
using Engine.Physics;
using Engine.Rendering;
using Engine.Scene;
using Xunit;

namespace Engine.Tests.Rendering;

public sealed class ScriptInspectorDispatchTests
{
    private class InspectorBaseComponent : Component;
    private sealed class GeneratedInspectorComponent : InspectorBaseComponent;
    private class FallbackInspectorComponent : Component;
    private sealed class DerivedFallbackInspectorComponent : FallbackInspectorComponent;

    private sealed class ScriptFields
    {
        public float MoveSpeed = 6f;
        public float JumpImpulse = 16f;
        public Vector3 Offset = Vector3.Zero;
        public readonly float ConstantSpeed = 1f;
        public static float SharedSpeed = 1f;
        [InspectorHidden]
        public float HiddenSpeed = 1f;
        public bool LockCursor { get; set; } = true;
    }

    [Fact]
    public void GeneratedComponentUsesInspectorRegisteredForItsBaseClass()
    {
        bool called = false;
        ComponentInspectorHooks.Register<InspectorBaseComponent>((_, _) =>
        {
            called = true;
            return true;
        });
        Assert.True(ComponentInspectorHooks.TryDraw(null!, new GeneratedInspectorComponent()));
        Assert.True(called);
    }

    [Fact]
    public void ExactHookRunsFirstAndCanFallBackToBaseInspector()
    {
        List<string> calls = [];
        ComponentInspectorHooks.Register<DerivedFallbackInspectorComponent>((_, _) =>
        {
            calls.Add("derived");
            return false;
        });
        ComponentInspectorHooks.Register<FallbackInspectorComponent>((_, _) =>
        {
            calls.Add("base");
            return true;
        });
        Assert.True(ComponentInspectorHooks.TryDraw(null!, new DerivedFallbackInspectorComponent()));
        Assert.Equal(new[] { "derived", "base" }, calls);
    }

    [Fact]
    public void RigidbodyConstraintInspectorEditsSupportUndoAndRedo()
    {
        string root = Path.Combine(Path.GetTempPath(), "EngineRigidbodyInspectorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Scene.Scene scene = new("Rigidbody inspector");
            Rigidbody body = scene.CreateObject("Body").AddComponent<Rigidbody>();
            using EditorContext context = new(new SceneDocument(new DelegatingSceneHost(scene), scene), root);
            RigidbodyConstraints constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;

            InspectorEditing.SetComponentProperty(context, body.Id, nameof(Rigidbody.Constraints),
                component => ((Rigidbody)component).Constraints,
                (component, value) => ((Rigidbody)component).Constraints = value,
                constraints, false);

            Assert.Equal(constraints, body.Constraints);
            Assert.True(context.Undo());
            Assert.Equal(RigidbodyConstraints.None, body.Constraints);
            Assert.True(context.Redo());
            Assert.Equal(constraints, body.Constraints);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RigidbodyInspectorDrawsFromGenericAttributes()
    {
        PropertyInfo constraints = typeof(Rigidbody).GetProperty(nameof(Rigidbody.Constraints))!;
        Assert.NotNull(constraints.GetCustomAttribute<InspectorGroupAttribute>());
        Assert.True(typeof(RigidbodyConstraints).GetCustomAttribute<FlagsAttribute>() is not null);
        MethodInfo sleep = typeof(Rigidbody).GetMethod(nameof(Rigidbody.Sleep))!;
        Assert.NotNull(sleep.GetCustomAttribute<InspectorButtonAttribute>());
        Assert.NotNull(typeof(Rigidbody).GetProperty(nameof(Rigidbody.IsKinematic))!.GetCustomAttribute<InspectorHiddenAttribute>());
        PropertyInfo resourceKey = typeof(MeshRenderer).GetProperty(nameof(MeshRenderer.ResourceKey))!;
        InspectorAssetDropAttribute? drop = resourceKey.GetCustomAttribute<InspectorAssetDropAttribute>();
        Assert.NotNull(drop);
        Assert.Equal("Mesh", drop!.Kind);
    }

    [Fact]
    public void ScriptInspectorIncludesPublicFieldsAndProperties()
    {
        string[] names = ScriptComponentInspector.GetInstanceMembers(typeof(ScriptFields)).Select(member => member.Name).ToArray();
        Assert.Contains(nameof(ScriptFields.MoveSpeed), names);
        Assert.Contains(nameof(ScriptFields.JumpImpulse), names);
        Assert.Contains(nameof(ScriptFields.Offset), names);
        Assert.Contains(nameof(ScriptFields.LockCursor), names);
        Assert.DoesNotContain(nameof(ScriptFields.ConstantSpeed), names);
        Assert.DoesNotContain(nameof(ScriptFields.SharedSpeed), names);
        Assert.DoesNotContain(nameof(ScriptFields.HiddenSpeed), names);
    }
}
