using Engine.Core;
using Engine.Mathematics;
using Engine.Platform;

namespace Engine.RHI;

public sealed class RhiModule : IEngineModule
{
    private readonly IRhiFactory _factory;
    private readonly RHIConfiguration _configuration;
    private readonly Action<RhiModule, IDevice>? _deviceConfigured;
    private IRHI? _rhi;
    private IWindow? _window;
    private ILogger? _log;

    public string Name => "RHI";

    public Vector4 ClearColor { get; set; } = new(0.1f, 0.15f, 0.25f, 1f);

    public bool RenderFrames { get; set; } = true;

    public IRHI? Instance => _rhi;

    public RhiModule(IRhiFactory factory, RHIConfiguration? configuration = null, Action<RhiModule, IDevice>? deviceConfigured = null)
    {
        _factory = factory;
        _configuration = configuration ?? new RHIConfiguration();
        _deviceConfigured = deviceConfigured;
    }

    public void Initialize(IEngineContext context)
    {
        _log = context.Logging.CreateLogger("RHI");
        SurfaceSource? surfaceSource = null;
        if (context.Services.TryResolve<IWindow>(out IWindow? window))
        {
            _window = window;
            surfaceSource = window.GetSurfaceSource();
        }
        else
            _log.Warning("No primary window registered, initializing RHI headless");

        _rhi = _factory.Create(_log);
        _rhi.Initialize(_configuration, surfaceSource);
        if (_window is not null)
            _window.Resized += OnWindowResized;
        context.Services.RegisterSingleton<IRHI>(_rhi);
        context.Services.RegisterSingleton<IDevice>(_rhi.Device);

        FeatureInfo features = _rhi.Device.Features;
        _log.Information(
            $"RHI '{_rhi.Name}' initialized: {features.DeviceName} ({features.ApiVersion}), " +
            $"dedicated memory {features.DedicatedVideoMemory / (1024.0 * 1024.0):F0} MB, " +
            $"ray tracing {(features.SupportsRayTracing ? "available" : "unavailable")}");
        _deviceConfigured?.Invoke(this, _rhi.Device);
    }

    private void OnWindowResized(Size2D size) => _rhi?.Swapchain.Resize(size);

    public void Shutdown()
    {
        if (_window is not null)
            _window.Resized -= OnWindowResized;
        _window = null;
        _rhi?.Dispose();
        _rhi = null;
    }
}

public sealed class RhiRenderModule : IUpdatableModule
{
    private readonly RhiModule _rhiModule;
    private IRHI? _rhi;

    public string Name => "RHI Render";

    public Vector4 ClearColor { get; set; } = new(0.1f, 0.15f, 0.25f, 1f);

    public RhiRenderModule(RhiModule rhiModule)
    {
        _rhiModule = rhiModule;
    }

    public void Initialize(IEngineContext context)
    {
        _rhi = _rhiModule.Instance;
        _rhiModule.RenderFrames = false;
    }

    public void Update(in FrameTime frameTime)
    {
        if (_rhi is null)
            return;
        if (_rhi.BeginFrame() is FrameStatus.Skipped)
            return;
        ICommandBuffer commandBuffer = _rhi.Device.CreateCommandBuffer();
        commandBuffer.Begin();
        _rhi.ClearFramebuffer(commandBuffer, ClearColor);
        commandBuffer.End();
        _rhi.EndFrame(commandBuffer);
    }

    public void Shutdown()
    {
    }
}
