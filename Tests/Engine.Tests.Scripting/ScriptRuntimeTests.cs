namespace Engine.Tests.Scripting;

using Engine.Core;
using Engine.Mathematics;
using Engine.Scene;
using SceneModel = Engine.Scene.Scene;
using Engine.Scripting;
using Xunit;

public sealed class ScriptRuntimeTests : IDisposable
{
    private readonly string _root;

    public ScriptRuntimeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "engine-script-runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ScriptingModule CreateModule()
    {
        ScriptingModule module = new(_root);
        module.Initialize(ApplicationBuilder.Create().AddLoggerSink(new MemoryLogSink()).Build().Context);
        Assert.True(module.LastCompileSuccessful, string.Join(" | ", module.LastErrors));
        return module;
    }

    private static readonly string LifecycleSource = """
        using Engine.Mathematics;
        using Engine.Scripting;

        public sealed class LifecycleScript : ScriptBehaviour
        {
            public int EnableCount { get; set; }
            public int StartCount { get; set; }
            public int UpdateCount { get; set; }
            public int FixedUpdateCount { get; set; }
            public int DisableCount { get; set; }
            public int DestroyCount { get; set; }
            public float LastDelta { get; set; }

            public override void OnEnable() => EnableCount++;
            public override void OnStart() => StartCount++;
            public override void OnUpdate() { UpdateCount++; LastDelta = ScriptTime.DeltaTime; }
            public override void OnFixedUpdate() => FixedUpdateCount++;
            public override void OnDisable() => DisableCount++;
            public override void OnDestroy() => DestroyCount++;
        }
        """;

    [Fact]
    public void PlayLifecycle_StartUpdatePauseStepDestroy()
    {
        File.WriteAllText(Path.Combine(_root, "LifecycleScript.cs"), LifecycleSource);
        using ScriptingModule module = CreateModule();
        SceneModel editScene = new("Lifecycle");
        module.AttachScene(editScene);
        GameObject gameObject = editScene.CreateObject("Actor");
        ScriptComponent script = (ScriptComponent)gameObject.AddComponent("LifecycleScript");

        SceneModel playScene = editScene.Clone(CloneIdPolicy.PreserveIds);
        module.BeginPlay(playScene);
        ScriptComponent playScript = Assert.IsAssignableFrom<ScriptComponent>(playScene.FindComponentById(script.Id));

        FrameTime time = new(1f / 60f, 1f / 60f, 0.5f, 1f / 30f, 0);
        module.Update(time);
        module.FixedUpdate(time);
        module.Update(time);

        ScriptBehaviour behaviour = (ScriptBehaviour)playScript.Instance!;
        Type type = behaviour.GetType();
        Assert.Equal(1, (int)type.GetProperty("EnableCount")!.GetValue(behaviour)!);
        Assert.Equal(1, (int)type.GetProperty("StartCount")!.GetValue(behaviour)!);
        Assert.Equal(2, (int)type.GetProperty("UpdateCount")!.GetValue(behaviour)!);
        Assert.Equal(1, (int)type.GetProperty("FixedUpdateCount")!.GetValue(behaviour)!);
        Assert.Equal(1f / 60f, (float)type.GetProperty("LastDelta")!.GetValue(behaviour)!);

        module.Pause();
        module.Update(time);
        module.FixedUpdate(time);
        Assert.Equal(2, (int)type.GetProperty("UpdateCount")!.GetValue(behaviour)!);

        module.Step();
        module.Update(time);
        module.FixedUpdate(time);
        Assert.Equal(3, (int)type.GetProperty("UpdateCount")!.GetValue(behaviour)!);

        module.EndPlay(playScene);
        Assert.Equal(1, (int)type.GetProperty("DisableCount")!.GetValue(behaviour)!);
        Assert.Equal(1, (int)type.GetProperty("DestroyCount")!.GetValue(behaviour)!);
        Assert.False(playScript.HasInstance);
    }

    [Fact]
    public void DisabledComponent_IsNotTicked()
    {
        File.WriteAllText(Path.Combine(_root, "LifecycleScript.cs"), LifecycleSource);
        using ScriptingModule module = CreateModule();
        SceneModel scene = new("Disabled");
        module.AttachScene(scene);
        GameObject gameObject = scene.CreateObject("Actor");
        ScriptComponent script = (ScriptComponent)gameObject.AddComponent("LifecycleScript");
        module.BeginPlay(scene);

        gameObject.Active = false;
        FrameTime time = new(1f / 60f, 1f / 60f, 1f, 1f / 30f, 0);
        module.Update(time);
        ScriptBehaviour behaviour = (ScriptBehaviour)script.Instance!;
        Assert.Equal(0, (int)behaviour.GetType().GetProperty("UpdateCount")!.GetValue(behaviour)!);

        gameObject.Active = true;
        module.Update(time);
        Assert.Equal(1, (int)behaviour.GetType().GetProperty("UpdateCount")!.GetValue(behaviour)!);
    }

    [Fact]
    public void PhysicsFacade_WithWiredModule_DrivesBodies()
    {
        File.WriteAllText(Path.Combine(_root, "FacadeScript.cs"), """
            public sealed class FacadeScript : ScriptBehaviour
            {
                public bool GroundedOnStart;

                public override void OnStart()
                {
                    GroundedOnStart = IsGrounded(1.2f);
                }
            }
            """);
        using ScriptingModule scripting = CreateModule();
        Engine.Physics.PhysicsModule physics = new();
        physics.Initialize(ApplicationBuilder.Create().AddLoggerSink(new MemoryLogSink()).Build().Context);
        scripting.Physics = physics;
        scripting.Recompile();

        SceneModel scene = new("Facade");
        GameObject ground = scene.CreateObject("Ground");
        ground.AddComponent<Engine.Physics.RigidBodyComponent>().Kind = Engine.Physics.RigidBodyKind.Static;
        ground.AddComponent<Engine.Physics.BoxColliderComponent>().Size = new Vector3(24f, 0.5f, 24f);
        ground.Transform.Position = new Vector3(0f, -0.25f, 0f);
        GameObject player = scene.CreateObject("Player");
        player.Transform.Position = new Vector3(0f, 1f, 0f);
        Engine.Physics.RigidBodyComponent rigidBody = player.AddComponent<Engine.Physics.RigidBodyComponent>();
        rigidBody.Kind = Engine.Physics.RigidBodyKind.Dynamic;
        rigidBody.Mass = 2f;
        player.AddComponent<Engine.Physics.BoxColliderComponent>().Size = new Vector3(1.4f, 1.4f, 1.4f);
        ScriptComponent script = Assert.IsAssignableFrom<ScriptComponent>(player.AddComponent("FacadeScript"));

        physics.AttachScene(scene);
        scripting.AttachScene(scene);
        FrameTime time = new(1f / 60f, 1f / 60f, 0f, 1f / 60f, 0);
        physics.Update(time);
        scripting.AlwaysPlay = true;
        scripting.Update(time);

        Assert.True(ScriptPhysics.HasBody(player));
        Assert.Equal(2f, ScriptPhysics.GetMass(player), 2f);
        ScriptPhysics.AddImpulse(player, Vector3.Up * 10f);
        Assert.InRange(ScriptPhysics.GetLinearVelocity(player).Y, 4.9f, 5.1f);
        Assert.True(ScriptPhysics.IsGrounded(player, 1.2f));
        Assert.True(script.HasInstance);
        ScriptBehaviour behaviour = Assert.IsAssignableFrom<ScriptBehaviour>(script.Instance)!;
        Assert.True((bool)behaviour.GetType().GetField("GroundedOnStart")!.GetValue(behaviour)!);
    }

    [Fact]
    public void PhysicsFacade_WithoutModule_ReturnsDefaults()
    {
        using ScriptingModule module = CreateModule();
        SceneModel scene = new("Facade");
        GameObject gameObject = scene.CreateObject("Actor");
        Assert.Equal(Vector3.Zero, ScriptPhysics.GetLinearVelocity(gameObject));
        Assert.Equal(0f, ScriptPhysics.GetMass(gameObject));
        Assert.False(ScriptPhysics.HasBody(gameObject));
        Assert.False(ScriptPhysics.IsGrounded(gameObject, 1f));
        Assert.Null(ScriptPhysics.RayCast(Vector3.Up, Vector3.Down, 5f));
        ScriptPhysics.SetLinearVelocity(gameObject, Vector3.Up);
        ScriptPhysics.AddImpulse(gameObject, Vector3.Up);
        ScriptPhysics.Wake(gameObject);
    }

    [Fact]
    public void AlwaysPlay_TicksAttachedScenes()
    {
        File.WriteAllText(Path.Combine(_root, "LifecycleScript.cs"), LifecycleSource);
        using ScriptingModule module = CreateModule();
        module.AlwaysPlay = true;
        SceneModel scene = new("Always");
        module.AttachScene(scene);
        GameObject gameObject = scene.CreateObject("Actor");
        gameObject.AddComponent("LifecycleScript");
        ScriptComponent script = gameObject.GetComponent<ScriptComponent>()!;

        FrameTime time = new(1f / 60f, 1f / 60f, 1f, 1f / 30f, 0);
        module.Update(time);
        Assert.NotNull(script);
        ScriptBehaviour behaviour = Assert.IsAssignableFrom<ScriptBehaviour>(script.Instance);
        Assert.Equal(1, (int)behaviour.GetType().GetProperty("UpdateCount")!.GetValue(behaviour)!);
    }
}
