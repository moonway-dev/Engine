using Engine.Core;

namespace Engine.Platform;

public sealed class PlatformModule : IUpdatableModule
{
    private readonly WindowDescription _description;
    private GlfwPlatform? _platform;
    private IWindow? _window;

    public string Name => "Platform";

    public PlatformModule(WindowDescription description)
    {
        _description = description;
    }

    public void Initialize(IEngineContext context)
    {
        _platform = new GlfwPlatform(context.Logging.CreateLogger("Platform"));
        _platform.Initialize();
        context.Services.RegisterSingleton<IPlatform>(_platform);
        IWindow window = _platform.CreateWindow(_description);
        context.Services.RegisterSingleton<IWindow>(window);
        window.CloseRequested += () => context.RequestExit();
        _window = window;
    }

    public void Update(in FrameTime frameTime)
    {
        _platform!.UpdateWindowInputs();
        _platform.PollEvents();
        HandleAltEnterFullscreen();
    }

    private void HandleAltEnterFullscreen()
    {
        if (_window is not { } window)
            return;
        KeyboardState keyboard = window.Keyboard;
        bool alt = keyboard.IsDown(KeyCode.LeftAlt) || keyboard.IsDown(KeyCode.RightAlt);
        if (!alt || !keyboard.IsPressed(KeyCode.Enter))
            return;
        window.SetFullscreen(window.FullscreenMode == FullscreenMode.Windowed
            ? FullscreenMode.Borderless
            : FullscreenMode.Windowed);
    }

    public void Shutdown() => _platform?.Dispose();
}
