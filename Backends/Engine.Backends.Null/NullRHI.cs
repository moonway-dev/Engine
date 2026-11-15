using Engine.Core;
using Engine.Mathematics;
using Engine.Platform;
using Engine.RHI;

namespace Engine.Backends.Null;

public sealed class NullRHI : IRHI
{
    private readonly ILogger _log;
    private readonly NullDevice _device = new();
    private NullSwapchain? _swapchain;
    private bool _frameActive;
    private bool _loggedFrame;

    public string Name => "Null Renderer";

    public GraphicsBackend Backend => GraphicsBackend.Null;

    public IDevice Device => _device;

    public ISwapchain Swapchain => _swapchain ?? throw new InvalidOperationException("Null RHI is not initialized.");

    public uint CurrentImageIndex { get; private set; }

    public uint CurrentFrameSlot { get; private set; }

    public uint FramesInFlight { get; private set; } = 1;

    public NullRHI(ILogger log)
    {
        _log = log;
    }

    public void Initialize(RHIConfiguration configuration, SurfaceSource? surfaceSource)
    {
        _swapchain = new NullSwapchain(configuration.HeadlessExtent, configuration.VSync);
        FramesInFlight = Math.Max(1u, configuration.FramesInFlight);
        _log.Debug($"Null RHI initialized ({configuration.HeadlessExtent.Width}x{configuration.HeadlessExtent.Height}, vsync={configuration.VSync})");
    }

    public FrameStatus BeginFrame()
    {
        if (_swapchain is null)
            throw new InvalidOperationException("Null RHI is not initialized.");
        if (_swapchain.Extent.Width == 0 || _swapchain.Extent.Height == 0)
            return FrameStatus.Skipped;
        _frameActive = true;
        CurrentImageIndex = _swapchain.AcquireImage();
        if (!_loggedFrame)
        {
            _log.Trace("Null frame rendering started");
            _loggedFrame = true;
        }
        return FrameStatus.Ready;
    }

    public void ClearFramebuffer(ICommandBuffer commandBuffer, in Vector4 color)
    {
        if (!_frameActive)
            throw new InvalidOperationException("BeginFrame must be called before ClearFramebuffer.");
        if (commandBuffer is NullCommandBuffer nullCommandBuffer)
        {
            nullCommandBuffer.BeginRendering(new RenderPassInfo
            {
                ColorAttachments =
                [
                    new ColorAttachmentInfo
                    {
                        Texture = _swapchain!.GetRenderTarget(CurrentImageIndex),
                        LoadOp = AttachmentLoadOp.Clear,
                        ClearColor = color,
                    },
                ],
                RenderArea = new Rect(0, 0, _swapchain.Extent.Width, _swapchain.Extent.Height),
            });
            nullCommandBuffer.EndRendering();
        }
    }

    public void EndFrame(ICommandBuffer? commandBuffer)
    {
        if (!_frameActive)
            throw new InvalidOperationException("BeginFrame must be called before EndFrame.");
        _frameActive = false;
        _swapchain!.Present();
        CurrentFrameSlot = (CurrentFrameSlot + 1) % FramesInFlight;
    }

    public byte[]? ReadSwapchainPixels() => null;

    public bool VSync { get; set; } = true;

    public ICommandBuffer GetFrameCommandBuffer() => _device.CreateCommandBuffer();

    public byte[]? ReadAllSwapchainPixels() => null;

    public uint SwapchainImageCount => 0;

    public byte[]? ReadTexturePixels(ITexture texture, ImageLayout currentLayout) => null;

    public void WaitIdle()
    {
    }

    public void Dispose()
    {
        _swapchain?.Dispose();
        _swapchain = null;
        _device.Dispose();
    }
}
