using Engine.RHI;

namespace Engine.Rendering;

public sealed class FrameUniformRing : IDisposable
{
    private readonly IDevice _device;
    private readonly IBuffer _buffer;
    private readonly IntPtr _mapped;
    private readonly byte[]? _cpuBuffer;
    private readonly ulong _bytesPerFrame;
    private readonly int _frameCount;
    private ulong _frameUsage;
    private int _frameSlot;
    private ulong _generation;
    private bool _frameStarted;
    private bool _disposed;

    public IBuffer Buffer => _buffer;

    public ulong Capacity { get; }

    public int FramesInFlight => _frameCount;

    public int CurrentFrameSlot => _frameSlot;

    public FrameUniformRing(IDevice device, int framesInFlight, ulong bytesPerFrame = 256 * 1024)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (bytesPerFrame == 0 || bytesPerFrame > int.MaxValue || bytesPerFrame % 256 != 0)
            throw new ArgumentOutOfRangeException(nameof(bytesPerFrame));
        _device = device;
        _frameCount = Math.Max(1, framesInFlight);
        _bytesPerFrame = bytesPerFrame;
        Capacity = checked(bytesPerFrame * (ulong)_frameCount);
        if (Capacity > (ulong)nint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(bytesPerFrame));
        _buffer = device.CreateBuffer(new BufferDescription
        {
            Size = Capacity,
            Usage = BufferUsage.Uniform | BufferUsage.TransferDestination,
            Memory = MemoryUsage.CpuToGpu,
        });
        try
        {
            _mapped = _buffer.Map();
            if (_mapped == IntPtr.Zero)
                _cpuBuffer = new byte[(int)bytesPerFrame];
        }
        catch
        {
            _buffer.Dispose();
            throw;
        }
    }

    public void BeginFrame(int frameSlot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)frameSlot >= (uint)_frameCount)
            throw new ArgumentOutOfRangeException(nameof(frameSlot));
        _frameSlot = frameSlot;
        _frameUsage = 0;
        _generation++;
        _frameStarted = true;
    }

    public FrameAllocation Allocate(int byteCount)
    {
        EnsureFrame();
        if (byteCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        ulong aligned = ((ulong)byteCount + 255) & ~255UL;
        if (aligned > _bytesPerFrame - _frameUsage)
            throw new InvalidOperationException("Frame uniform ring overflow, increase bytes per frame.");
        ulong localOffset = _frameUsage;
        _frameUsage += aligned;
        ulong bufferOffset = (ulong)_frameSlot * _bytesPerFrame + localOffset;
        return new FrameAllocation(bufferOffset, localOffset, (ulong)byteCount, aligned)
        {
            Owner = this,
            Generation = _generation,
        };
    }

    public unsafe void Write(FrameAllocation allocation, ReadOnlySpan<byte> data)
    {
        EnsureFrame();
        if (!ReferenceEquals(allocation.Owner, this) || allocation.Generation != _generation
            || allocation.ByteCount == 0 || allocation.ByteCount > _bytesPerFrame
            || allocation.LocalOffset % 256 != 0
            || allocation.AlignedCount != ((allocation.ByteCount + 255) & ~255UL)
            || allocation.LocalOffset > _frameUsage
            || allocation.AlignedCount > _frameUsage - allocation.LocalOffset
            || allocation.Offset != (ulong)_frameSlot * _bytesPerFrame + allocation.LocalOffset)
            throw new ArgumentException("Allocation does not belong to the current frame range.", nameof(allocation));
        if ((ulong)data.Length > allocation.ByteCount)
            throw new ArgumentOutOfRangeException(nameof(data));
        if (_mapped != IntPtr.Zero)
            data.CopyTo(new Span<byte>((byte*)_mapped + (nint)allocation.Offset, data.Length));
        else
            data.CopyTo(_cpuBuffer.AsSpan((int)allocation.LocalOffset, data.Length));
    }

    public void FlushFrame()
    {
        EnsureFrame();
        if (_mapped != IntPtr.Zero || _frameUsage == 0)
            return;
        ulong destinationOffset = (ulong)_frameSlot * _bytesPerFrame;
        _device.UploadBufferData(_buffer, _cpuBuffer.AsSpan(0, (int)_frameUsage), destinationOffset);
    }

    private void EnsureFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_frameStarted)
            throw new InvalidOperationException("BeginFrame must be called before accessing frame uniforms.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            if (_mapped != IntPtr.Zero)
                _buffer.Unmap();
        }
        finally
        {
            _buffer.Dispose();
        }
    }
}

public readonly record struct FrameAllocation(ulong Offset, ulong LocalOffset, ulong ByteCount, ulong AlignedCount)
{
    internal FrameUniformRing? Owner { get; init; }
    internal ulong Generation { get; init; }
}
