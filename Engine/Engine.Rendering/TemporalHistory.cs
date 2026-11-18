using Engine.Mathematics;
using Engine.RHI;

namespace Engine.Rendering;

public sealed class TemporalHistory : IDisposable
{
    private readonly ITexture?[] _textures = new ITexture?[2];
    private readonly bool[] _initialized = new bool[2];
    private int _readIndex;
    private bool _disposed;

    public bool IsAllocated => _textures[0] is not null;

    public bool IsValid { get; private set; }

    public ITexture ReadTexture => _textures[_readIndex] ?? throw new InvalidOperationException("Temporal history is not allocated.");

    public ITexture WriteTexture => _textures[1 - _readIndex] ?? throw new InvalidOperationException("Temporal history is not allocated.");

    public bool ReadInitialized => _initialized[_readIndex];

    public bool WriteInitialized => _initialized[1 - _readIndex];

    public void Recreate(IDevice device, Size2D extent, TextureFormat format = TextureFormat.R16G16B16A16Float)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(device);
        if (extent.Width <= 0 || extent.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(extent));

        DisposeTextures();
        try
        {
            TextureDescription description = new()
            {
                Extent = extent,
                Format = format,
                Usage = TextureUsage.Sampled | TextureUsage.Storage,
            };
            _textures[0] = device.CreateTexture(description);
            _textures[1] = device.CreateTexture(description);
        }
        catch
        {
            DisposeTextures();
            throw;
        }
        _readIndex = 0;
        _initialized[0] = false;
        _initialized[1] = false;
        IsValid = false;
    }

    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsAllocated)
            throw new InvalidOperationException("Temporal history is not allocated.");
        int writeIndex = 1 - _readIndex;
        _initialized[writeIndex] = true;
        _readIndex = writeIndex;
        IsValid = true;
    }

    public void Invalidate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IsValid = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        DisposeTextures();
    }

    private void DisposeTextures()
    {
        _textures[0]?.Dispose();
        _textures[1]?.Dispose();
        _textures[0] = null;
        _textures[1] = null;
        _initialized[0] = false;
        _initialized[1] = false;
        IsValid = false;
    }
}

public static class TemporalJitter
{
    public static Vector2 Sample(uint index) => new(
        (RadicalInverse(index + 1, 2) - 0.5f) * 0.5f,
        (RadicalInverse(index + 1, 3) - 0.5f) * 0.5f);

    private static float RadicalInverse(uint index, uint basis)
    {
        float inverseBase = 1f / basis;
        float inverse = inverseBase;
        float result = 0f;
        while (index > 0)
        {
            result += index % basis * inverse;
            index /= basis;
            inverse *= inverseBase;
        }
        return result;
    }
}
