using Engine.Core;

namespace Engine.Platform;

public sealed class PlatformModule : IUpdatableModule
{
    private readonly WindowDescription _description;
    private GlfwPlatform? _platform;

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
    }

    public void Update(in FrameTime frameTime)
    {
        _platform!.UpdateWindowInputs();
        _platform.PollEvents();
    }

    public void Shutdown() => _platform?.Dispose();
}
