using Engine.Mathematics;
using Engine.RHI;

namespace Engine.Backends.Null;

public sealed class NullSwapchain : ISwapchain
{
    private Size2D _extent;
    private uint _acquiredIndex;

    public NullSwapchain(Size2D extent, bool vsync)
    {
        _extent = extent;
        VSync = vsync;
    }

    public TextureFormat Format => TextureFormat.B8G8R8A8Srgb;

    public Size2D Extent => _extent;

    public uint ImageCount => 2;

    public bool VSync { get; set; }

    public ITexture GetRenderTarget(uint imageIndex) => new NullTexture(new TextureDescription
    {
        Extent = _extent,
        Format = Format,
    });

    public void Resize(Size2D newExtent) => _extent = newExtent;

    internal uint AcquireImage() => ++_acquiredIndex;

    internal void Present()
    {
    }

    public void Dispose()
    {
    }
}
