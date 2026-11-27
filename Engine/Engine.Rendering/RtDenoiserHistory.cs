using Engine.Mathematics;
using Engine.RHI;

namespace Engine.Rendering;

internal sealed class RtDenoiserHistory : IDisposable
{
    private readonly bool _ownsColor;
    public TemporalHistory Color { get; }
    public TemporalHistory Moments { get; } = new();
    public TemporalHistory Geometry { get; } = new();
    public Size2D Extent { get; private set; }
    public Size2D SourceExtent { get; private set; }
    public bool IsValid => Color.IsValid && Moments.IsValid && Geometry.IsValid;
    public bool Recorded { get; set; }

    public RtDenoiserHistory(TemporalHistory? color = null)
    {
        _ownsColor = color is null;
        Color = color ?? new();
    }

    public void Recreate(IDevice device, Size2D extent)
    {
        if (!Color.IsAllocated || _ownsColor || (int)Color.ReadTexture.Width != extent.Width || (int)Color.ReadTexture.Height != extent.Height)
            Color.Recreate(device, extent);
        Moments.Recreate(device, extent);
        Geometry.Recreate(device, extent);
        Extent = extent;
        SourceExtent = default;
        Recorded = false;
        Invalidate();
    }

    public void Prepare(Size2D sourceExtent)
    {
        Recorded = false;
        if (SourceExtent != sourceExtent)
        {
            Invalidate();
            SourceExtent = sourceExtent;
        }
    }

    public void Commit()
    {
        if (!Recorded)
        {
            Invalidate();
            return;
        }
        Color.Commit();
        Moments.Commit();
        Geometry.Commit();
        Recorded = false;
    }

    public void Invalidate()
    {
        Color.Invalidate();
        Moments.Invalidate();
        Geometry.Invalidate();
    }

    public void Dispose()
    {
        if (_ownsColor)
            Color.Dispose();
        Moments.Dispose();
        Geometry.Dispose();
    }
}
