using Engine.Mathematics;

namespace Engine.RHI;

public interface ISwapchain : IDisposable
{
    TextureFormat Format { get; }

    Size2D Extent { get; }

    uint ImageCount { get; }

    bool VSync { get; set; }

    ITexture GetRenderTarget(uint imageIndex);

    void Resize(Size2D newExtent);
}
