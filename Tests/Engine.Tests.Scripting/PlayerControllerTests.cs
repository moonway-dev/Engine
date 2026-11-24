using System.Reflection;
using Engine.Core;
using Engine.Mathematics;
using Engine.Platform;
using Engine.Scene;
using Engine.Scripting;
using ImGuiNET;
using Xunit;
using SceneModel = Engine.Scene.Scene;

namespace Engine.Tests.Scripting;

public sealed class PlayerControllerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EnginePlayerControllerTests", Guid.NewGuid().ToString("N"));

    public PlayerControllerTests()
    {
        Directory.CreateDirectory(_root);
        File.Copy(Path.Combine(FindRepositoryRoot(), "Projects", "SimpleFps", "Scripts", "PlayerController.cs"), Path.Combine(_root, "PlayerController.cs"));
    }

    [Theory]
    [InlineData(KeyCode.W, 0f, 0f, 0f, 0f, 0f, -6f)]
    [InlineData(KeyCode.W, 90f, 0f, 0f, -6f, 0f, 0f)]
    [InlineData(KeyCode.W, 90f, 80f, 0f, -6f, 0f, 0f)]
    [InlineData(KeyCode.W, 0f, 0f, 90f, -6f, 0f, 0f)]
    [InlineData(KeyCode.S, 90f, 0f, 0f, 6f, 0f, 0f)]
    [InlineData(KeyCode.D, 90f, 0f, 0f, 0f, 0f, -6f)]
    [InlineData(KeyCode.A, 90f, 0f, 0f, 0f, 0f, 6f)]
    public void MovementFollowsCameraHeadingOnHorizontalPlane(KeyCode key, float yaw, float pitch, float parentYaw, float x, float y, float z)
    {
        KeyboardState keyboard = new();
        HashSet<KeyCode> keys = (HashSet<KeyCode>)typeof(KeyboardState).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(keyboard)!;
        keys.Add(key);
        IWindow window = DispatchProxy.Create<IWindow, InputWindowProxy>();
        ((InputWindowProxy)window).Keyboard = keyboard;
        IEngineContext context = ApplicationBuilder.Create().AddLoggerSink(new MemoryLogSink())
            .ConfigureServices(services => services.RegisterSingleton<IWindow>(window)).Build().Context;
        using ScriptingModule scripting = new(_root);
        scripting.Initialize(context);
        Assert.True(scripting.LastCompileSuccessful, string.Join(Environment.NewLine, scripting.LastErrors));
        SceneModel scene = new("Camera relative movement");
        scripting.AttachScene(scene);
        GameObject player = scene.CreateObject("Player");
        player.Transform.Rotation = Quaternion.FromYawPitchRoll((parentYaw + yaw) * MathUtil.DegToRad, 0f, 0f);
        GameObject cameraObject = scene.CreateObject("Camera", player);
        cameraObject.Transform.LocalRotation = Quaternion.FromYawPitchRoll(0f, pitch * MathUtil.DegToRad, 0f);
        cameraObject.AddComponent<Engine.Scene.Camera>();
        Script component = (Script)player.AddComponent("PlayerController");
        component.Instance!.GetType().GetField("LockCursor")!.SetValue(component.Instance, false);
        component.Instance!.GetType().GetField("AirControl")!.SetValue(component.Instance, 1f);
        Vector3 actual = Vector3.Zero;
        ScriptPhysics.LinearVelocitySetter = (_, velocity) => actual = velocity;
        scripting.BeginPlay(scene);
        scripting.Update(new FrameTime(1f / 60f, 1f / 60f, 0f, 1f / 60f, 0));
        Assert.Equal(x, actual.X, 4);
        Assert.Equal(y, actual.Y, 4);
        Assert.Equal(z, actual.Z, 4);
        Assert.Equal(6f, actual.Length, 4);
    }

    [Theory]
    [InlineData(100f, 0f)]
    [InlineData(-100f, 0f)]
    [InlineData(0f, 100f)]
    [InlineData(0f, -100f)]
    public void MouseLookUsesCorrectedHorizontalSignAndIndependentAxes(float mouseX, float mouseY)
    {
        IWindow window = DispatchProxy.Create<IWindow, InputWindowProxy>();
        MouseState mouse = ((InputWindowProxy)window).Mouse;
        typeof(MouseState).GetProperty(nameof(MouseState.Delta))!.SetValue(mouse, new Vector2(mouseX, mouseY));
        IEngineContext context = ApplicationBuilder.Create().AddLoggerSink(new MemoryLogSink())
            .ConfigureServices(services => services.RegisterSingleton<IWindow>(window)).Build().Context;
        using ScriptingModule scripting = new(_root);
        scripting.Initialize(context);
        Assert.True(scripting.LastCompileSuccessful, string.Join(Environment.NewLine, scripting.LastErrors));
        SceneModel scene = new("Mouse look");
        scripting.AttachScene(scene);
        GameObject player = scene.CreateObject("Player");
        GameObject cameraObject = scene.CreateObject("Camera", player);
        cameraObject.AddComponent<Engine.Scene.Camera>();
        Script component = (Script)player.AddComponent("PlayerController");
        component.Instance!.GetType().GetField("LockCursor")!.SetValue(component.Instance, false);
        scripting.BeginPlay(scene);
        scripting.Update(new FrameTime(1f / 60f, 1f / 60f, 0f, 1f / 60f, 0));
        float sensitivity = (float)component.Instance.GetType().GetField("MouseSensitivity")!.GetValue(component.Instance)!;
        Quaternion expectedPlayer = Quaternion.FromYawPitchRoll(-mouseX * sensitivity, 0f, 0f);
        Assert.Equal(expectedPlayer.X, player.Transform.Rotation.X, 5);
        Assert.Equal(expectedPlayer.Y, player.Transform.Rotation.Y, 5);
        Assert.Equal(expectedPlayer.Z, player.Transform.Rotation.Z, 5);
        Assert.Equal(expectedPlayer.W, player.Transform.Rotation.W, 5);
        Quaternion expectedCamera = Quaternion.FromYawPitchRoll(0f, -mouseY * sensitivity, 0f);
        Assert.Equal(expectedCamera.X, cameraObject.Transform.LocalRotation.X, 5);
        Assert.Equal(expectedCamera.Y, cameraObject.Transform.LocalRotation.Y, 5);
        Assert.Equal(expectedCamera.Z, cameraObject.Transform.LocalRotation.Z, 5);
        Assert.Equal(expectedCamera.W, cameraObject.Transform.LocalRotation.W, 5);
        if (mouseX != 0f)
            Assert.Equal(mouseX > 0f, player.Transform.Forward.X > 0f);
    }

    [Fact]
    public void GeneratedPlayerScriptRendersPublicFieldsInInspector()
    {
        IEngineContext context = ApplicationBuilder.Create().AddLoggerSink(new MemoryLogSink()).Build().Context;
        using ScriptingModule scripting = new(_root);
        scripting.Initialize(context);
        Assert.True(scripting.LastCompileSuccessful, string.Join(Environment.NewLine, scripting.LastErrors));
        SceneModel scene = new("Script inspector");
        scripting.AttachScene(scene);
        Script script = (Script)scene.CreateObject("Player").AddComponent("PlayerController");
        Assert.NotEqual(typeof(Script), script.GetType());
        Type inspectorType = typeof(Engine.Editor.Inspection.ComponentPropertyInspector).Assembly.GetType("Engine.Editor.Inspection.ScriptComponentInspector")!;
        inspectorType.GetMethod("Register", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
        IReadOnlyList<MemberInfo> members = (IReadOnlyList<MemberInfo>)inspectorType
            .GetMethod("GetInstanceMembers", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [script.Instance!.GetType()])!;
        Assert.Contains(members, member => member.Name == "MoveSpeed" && member is FieldInfo);
        Assert.Contains(members, member => member.Name == "JumpImpulse" && member is FieldInfo);
        Assert.Contains(members, member => member.Name == "MouseSensitivity" && member is FieldInfo);
        IntPtr imGuiContext = ImGui.CreateContext();
        try
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.DisplaySize = new System.Numerics.Vector2(800f, 1200f);
            io.DeltaTime = 1f / 60f;
            io.Fonts.AddFontDefault();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
            ImGui.NewFrame();
            ImGui.Begin("Inspector test");
            using Engine.Editor.Documents.SceneDocument document = new(new Engine.Editor.Documents.DelegatingSceneHost(scene), scene);
            using Engine.Editor.EditorContext editorContext = new(document, _root);
            bool drawn = (bool)typeof(Engine.Editor.Inspection.ComponentInspectorHooks)
                .GetMethod("TryDraw", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [editorContext, script])!;
            Assert.True(drawn);
            ImGui.End();
            ImGui.Render();
        }
        finally
        {
            ImGui.DestroyContext(imGuiContext);
        }
    }

    public void Dispose() => Directory.Delete(_root, true);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Engine.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Engine repository not found.");
    }

    public class InputWindowProxy : DispatchProxy
    {
        public KeyboardState Keyboard { get; set; } = new();
        public MouseState Mouse { get; } = new();
        private CursorMode _cursorMode;

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            switch (method!.Name)
            {
                case "get_Keyboard": return Keyboard;
                case "get_Mouse": return Mouse;
                case "get_CursorMode": return _cursorMode;
                case "set_CursorMode": _cursorMode = (CursorMode)arguments![0]!; return null;
                default: return method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
            }
        }
    }
}
