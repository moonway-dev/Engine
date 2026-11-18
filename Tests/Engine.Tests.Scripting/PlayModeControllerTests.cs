namespace Engine.Tests.Scripting;

using Engine.Editor;
using Engine.Editor.Documents;
using Engine.Scene;
using SceneModel = Engine.Scene.Scene;
using Xunit;

public sealed class PlayModeControllerTests : IDisposable
{
    private readonly string _root;
    private readonly StubHost _host = new();
    private readonly SceneModel _scene = new("PlayMode");

    public PlayModeControllerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "engine-play-mode-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Assets"));
        _scene.CreateObject("Ground");
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

    private PlayModeController CreateController(Engine.Physics.PhysicsModule? physics = null)
    {
        SceneDocument document = new(_host, _scene, Path.Combine(_root, "scene.json"));
        EditorContext context = new(document, _root);
        return new PlayModeController(context, null, physics);
    }

    [Fact]
    public void Play_TogglesPhysicsSimulation()
    {
        Engine.Physics.PhysicsModule physics = new();
        Assert.True(physics.SimulationActive);
        PlayModeController controller = CreateController(physics);
        controller.EnterPlay();
        Assert.True(physics.SimulationActive);
        controller.Pause();
        Assert.False(physics.SimulationActive);
        controller.Resume();
        Assert.True(physics.SimulationActive);
        controller.ExitPlay();
        Assert.False(physics.SimulationActive);
    }

    [Fact]
    public void EnterPlay_ClonesSceneAndSwapsActive()
    {
        PlayModeController controller = CreateController();
        Assert.Equal(EditorPlayState.Stopped, controller.State);

        controller.EnterPlay();
        Assert.Equal(EditorPlayState.Playing, controller.State);
        Assert.NotSame(_scene, _host.ActiveScene);
        Assert.Single(_host.ActiveScene!.RootObjects);
        Assert.Equal(_host.ActiveScene!.RootObjects[0].Name, _scene.RootObjects[0].Name);

        controller.ExitPlay();
        Assert.Equal(EditorPlayState.Stopped, controller.State);
        Assert.Same(_scene, _host.ActiveScene);
    }

    [Fact]
    public void EnterPlay_PreservesSelectionIds()
    {
        SceneDocument document = new(_host, _scene, Path.Combine(_root, "scene.json"));
        EditorContext context = new(document, _root);
        PlayModeController controller = new(context, null);
        document.Select(_scene.RootObjects[0]);
        Guid selected = _scene.RootObjects[0].Id;

        controller.EnterPlay();
        Assert.NotNull(document.SelectedObject);
        Assert.Equal(selected, document.SelectedObjectId);

        controller.ExitPlay();
        Assert.Equal(selected, document.SelectedObjectId);
    }

    [Fact]
    public void PauseAndStep_Transitions()
    {
        PlayModeController controller = CreateController();
        controller.EnterPlay();
        controller.Pause();
        Assert.Equal(EditorPlayState.Paused, controller.State);
        controller.Resume();
        Assert.Equal(EditorPlayState.Playing, controller.State);
        controller.Pause();
        controller.Step();
        Assert.Equal(EditorPlayState.Paused, controller.State);
        controller.ExitPlay();
        Assert.Equal(EditorPlayState.Stopped, controller.State);
    }

    private sealed class StubHost : ISceneHost
    {
        public SceneModel? ActiveScene { get; private set; }

        public event EventHandler<SceneChangedEventArgs>? ActiveSceneChanged;

        public void SetActiveScene(SceneModel scene)
        {
            SceneModel? previous = ActiveScene;
            ActiveScene = scene;
            ActiveSceneChanged?.Invoke(this, new SceneChangedEventArgs(previous, scene));
        }
    }
}
