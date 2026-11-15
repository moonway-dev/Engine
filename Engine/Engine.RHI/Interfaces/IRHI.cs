using Engine.Mathematics;
using Engine.Platform;

namespace Engine.RHI;

public enum FrameStatus
{
    Ready,
    Skipped,
}

public interface IRHI : IDisposable
{
    string Name { get; }

    GraphicsBackend Backend { get; }

    IDevice Device { get; }

    ISwapchain Swapchain { get; }

    uint CurrentImageIndex { get; }

    uint CurrentFrameSlot { get; }

    uint FramesInFlight { get; }

    void Initialize(RHIConfiguration configuration, SurfaceSource? surfaceSource);

    FrameStatus BeginFrame();

    void ClearFramebuffer(ICommandBuffer commandBuffer, in Vector4 color);

    void EndFrame(ICommandBuffer? commandBuffer);

    bool VSync { get; set; }

    byte[]? ReadSwapchainPixels();

    ICommandBuffer GetFrameCommandBuffer();

    byte[]? ReadAllSwapchainPixels();

    uint SwapchainImageCount { get; }

    byte[]? ReadTexturePixels(ITexture texture, ImageLayout currentLayout);

    void WaitIdle();
}
