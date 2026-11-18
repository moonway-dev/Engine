namespace Engine.Tests.Scripting;

using System.Reflection;
using Engine.Core;
using Engine.Mathematics;
using Engine.Scene;
using SceneModel = Engine.Scene.Scene;
using Engine.Scripting;
using Xunit;

public sealed class ScriptCompilerTests : IDisposable
{
    private readonly string _root;

    public ScriptCompilerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "engine-script-tests", Guid.NewGuid().ToString("N"));
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
        return module;
    }

    private static string SpinnerSource(string className, float speed) => $$"""
        using Engine.Mathematics;
        using Engine.Scripting;

        public sealed class {{className}} : ScriptBehaviour
        {
            public float Speed { get; set; } = {{speed.ToString(System.Globalization.CultureInfo.InvariantCulture)}}f;

            public override void OnUpdate()
            {
                Transform.LocalRotation = Quaternion.FromAxisAngle(Vector3.UnitY, ScriptTime.TotalTime * Speed);
            }
        }
        """;

    [Fact]
    public void Compile_RegisterAndInstantiateScriptComponent()
    {
        File.WriteAllText(Path.Combine(_root, "SpinTestA.cs"), SpinnerSource("SpinTestA", 2f));
        using ScriptingModule module = CreateModule();
        Assert.True(module.LastCompileSuccessful, string.Join(" | ", module.LastErrors));
        Assert.Single(module.Scripts);
        Assert.Equal("SpinTestA", module.Scripts[0].Key);

        SceneModel scene = new("Compile");
        module.AttachScene(scene);
        GameObject gameObject = scene.CreateObject("Spinner");
        Component component = gameObject.AddComponent("SpinTestA");
        ScriptComponent script = Assert.IsAssignableFrom<ScriptComponent>(component);
        Assert.True(script.HasInstance);
        Assert.IsAssignableFrom<ScriptBehaviour>(script.Instance);
        object instance = script.Instance!;
        PropertyInfo speed = instance.GetType().GetProperty("Speed")!;
        Assert.Equal(2f, (float)speed.GetValue(instance)!);
        speed.SetValue(instance, 5f);
        Assert.Equal(5f, (float)speed.GetValue(instance)!);
    }

    [Fact]
    public void Serialize_RoundTripPreservesScriptAndInstanceProperties()
    {
        File.WriteAllText(Path.Combine(_root, "SpinTestB.cs"), SpinnerSource("SpinTestB", 1.5f));
        using ScriptingModule module = CreateModule();
        Assert.True(module.LastCompileSuccessful, string.Join(" | ", module.LastErrors));
        SceneModel scene = new("RoundTrip");
        module.AttachScene(scene);
        GameObject gameObject = scene.CreateObject("Spinner");
        ScriptComponent script = (ScriptComponent)gameObject.AddComponent("SpinTestB");
        ((ScriptBehaviour)script.Instance!).GetType().GetProperty("Speed")!.SetValue(script.Instance, 4.25f);

        string path = Path.Combine(_root, "scene.json");
        scene.Save(path);
        SceneModel loaded = Scene.Load(path);
        GameObject loadedObject = loaded.FindByName("Spinner")!;
        ScriptComponent loadedScript = loadedObject.GetComponent<ScriptComponent>()!;
        Assert.Equal("SpinTestB", loadedScript.Script);
        Assert.True(loadedScript.HasInstance);
        Assert.Equal(4.25f, (float)loadedScript.Instance!.GetType().GetProperty("Speed")!.GetValue(loadedScript.Instance)!);
    }

    [Fact]
    public void Recompile_HotReloadReplacesInstancesAndPreservesValues()
    {
        File.WriteAllText(Path.Combine(_root, "SpinTestC.cs"), SpinnerSource("SpinTestC", 1f));
        using ScriptingModule module = CreateModule();
        SceneModel scene = new("Reload");
        module.AttachScene(scene);
        GameObject gameObject = scene.CreateObject("Spinner");
        ScriptComponent script = (ScriptComponent)gameObject.AddComponent("SpinTestC");
        ScriptBehaviour first = Assert.IsAssignableFrom<ScriptBehaviour>(script.Instance!);
        script.Instance!.GetType().GetProperty("Speed")!.SetValue(script.Instance, 7f);

        File.WriteAllText(Path.Combine(_root, "SpinTestC.cs"), SpinnerSource("SpinTestC", 3f));
        module.Recompile();

        Assert.True(script.HasInstance);
        Assert.NotSame(first, script.Instance);
        Assert.Equal(7f, (float)script.Instance!.GetType().GetProperty("Speed")!.GetValue(script.Instance)!);
    }

    [Fact]
    public void Compile_ReportErrorsForBrokenScript()
    {
        File.WriteAllText(Path.Combine(_root, "SpinTestD.cs"), "public sealed class Broken { this is not valid C# }");
        using ScriptingModule module = CreateModule();
        Assert.False(module.LastCompileSuccessful);
        Assert.NotEmpty(module.LastErrors);
        Assert.Empty(module.Scripts);
    }

    [Fact]
    public void Serialize_PublicFieldsRoundTripAndHotReload()
    {
        File.WriteAllText(Path.Combine(_root, "FieldScript.cs"), $$"""
            public sealed class FieldScript : ScriptBehaviour
            {
                public float Speed = 2f;
                public Vector3 Offset = new(1f, 2f, 3f);
                private int Secret;

                public override void OnUpdate()
                {
                    Transform.Position += Offset * ScriptTime.DeltaTime * Speed;
                }
            }
            """);
        using ScriptingModule module = CreateModule();
        Assert.True(module.LastCompileSuccessful, string.Join(" | ", module.LastErrors));
        SceneModel scene = new("Fields");
        module.AttachScene(scene);
        GameObject gameObject = scene.CreateObject("Actor");
        ScriptComponent script = Assert.IsAssignableFrom<ScriptComponent>(gameObject.AddComponent("FieldScript"));
        script.Instance!.GetType().GetField("Speed")!.SetValue(script.Instance, 5f);
        script.Instance!.GetType().GetField("Offset")!.SetValue(script.Instance, new Vector3(0f, 7f, 0f));

        SceneModel loaded = SceneModel.FromJson(scene.ToJson());
        ScriptComponent loadedScript = loaded.FindByName("Actor")!.GetComponent<ScriptComponent>()!;
        Assert.Equal(5f, (float)loadedScript.Instance!.GetType().GetField("Speed")!.GetValue(loadedScript.Instance)!);
        Assert.Equal(new Vector3(0f, 7f, 0f), (Vector3)loadedScript.Instance!.GetType().GetField("Offset")!.GetValue(loadedScript.Instance)!);

        File.WriteAllText(Path.Combine(_root, "FieldScript.cs"), $$"""
            public sealed class FieldScript : ScriptBehaviour
            {
                public float Speed = 1f;
                public Vector3 Offset = new(1f, 1f, 1f);

                public override void OnUpdate()
                {
                    Transform.Position += Offset * ScriptTime.DeltaTime * Speed;
                }
            }
            """);
        module.Recompile();
        Assert.Equal(5f, (float)script.Instance!.GetType().GetField("Speed")!.GetValue(script.Instance)!);
        Assert.Equal(new Vector3(0f, 7f, 0f), (Vector3)script.Instance!.GetType().GetField("Offset")!.GetValue(script.Instance)!);
        Assert.False(script.Instance.GetType().GetField("Secret") is not null);
    }

    [Fact]
    public void SceneLoad_RestoresInstanceValuesAfterLoad()
    {
        File.WriteAllText(Path.Combine(_root, "SpinTestE.cs"), SpinnerSource("SpinTestE", 2f));
        using ScriptingModule module = CreateModule();
        SceneModel scene = new("Pending");
        module.AttachScene(scene);
        GameObject gameObject = scene.CreateObject("Spinner");
        ScriptComponent script = (ScriptComponent)gameObject.AddComponent("SpinTestE");
        script.Instance!.GetType().GetProperty("Speed")!.SetValue(script.Instance, 9f);
        string json = scene.ToJson();

        SceneModel loaded = Scene.FromJson(json);
        ScriptComponent pending = loaded.FindByName("Spinner")!.GetComponent<ScriptComponent>()!;
        Assert.Equal("SpinTestE", pending.Script);

        ScriptingModule other = new(_root);
        other.Initialize(ApplicationBuilder.Create().AddLoggerSink(new MemoryLogSink()).Build().Context);
        other.AttachScene(loaded);
        Assert.True(pending.HasInstance);
        Assert.Equal(9f, (float)pending.Instance!.GetType().GetProperty("Speed")!.GetValue(pending.Instance)!);
        other.Shutdown();
    }
}
