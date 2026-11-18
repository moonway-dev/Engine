namespace Engine.RHI;

public sealed class MemoryBlockAllocator
{
    public const ulong DefaultAlignment = 256;
    public const ulong MinBlockSplitSize = 4096;

    private sealed class FreeRange : IComparable<FreeRange>
    {
        public ulong Offset;
        public ulong Size;

        public int CompareTo(FreeRange? other) => Offset.CompareTo(other?.Offset ?? 0);
    }

    private readonly List<FreeRange> _freeRanges = [];
    private readonly List<FreeRange> _freeRangePool = [];
    private readonly Dictionary<ulong, (ulong ReservedOffset, ulong ReservedSize, ulong RequestedSize)> _allocations = [];
    private readonly object _lock = new();
    private readonly int _blockIndex;

    public ulong Capacity { get; }

    public ulong Used { get; private set; }

    public MemoryBlockAllocator(int blockIndex, ulong capacity)
    {
        _blockIndex = blockIndex;
        Capacity = capacity;
        _freeRanges.Add(new FreeRange { Offset = 0, Size = capacity });
    }

    public bool TryAllocate(ulong size, ulong alignment, out ulong offset)
    {
        if (alignment == 0)
            throw new ArgumentOutOfRangeException(nameof(alignment));
        if (size == 0)
        {
            offset = 0;
            return true;
        }
        lock (_lock)
        {
            for (int i = 0; i < _freeRanges.Count; i++)
            {
                FreeRange range = _freeRanges[i];
                ulong alignedOffset = AlignUp(range.Offset, alignment);
                ulong padding = alignedOffset - range.Offset;
                ulong requiredTotal = checked(padding + size);
                if (requiredTotal > range.Size)
                    continue;

                offset = alignedOffset;
                _allocations.Add(offset, (range.Offset, requiredTotal, size));
                Used += requiredTotal;
                if (requiredTotal == range.Size)
                {
                    RemoveRange(i);
                }
                else
                {
                    range.Offset = alignedOffset + size;
                    range.Size -= requiredTotal;
                }
                return true;
            }
        }
        offset = 0;
        return false;
    }

    public void Free(ulong offset, ulong size)
    {
        if (size == 0)
            return;
        lock (_lock)
        {
            if (!_allocations.Remove(offset, out (ulong ReservedOffset, ulong ReservedSize, ulong RequestedSize) allocation))
                throw new ArgumentException("The offset does not identify a live allocation.", nameof(offset));
            if (allocation.RequestedSize != size)
            {
                _allocations.Add(offset, allocation);
                throw new ArgumentException("The size does not match the live allocation.", nameof(size));
            }

            ulong freeOffset = allocation.ReservedOffset;
            ulong freeSize = allocation.ReservedSize;
            ulong end = checked(freeOffset + freeSize);
            int insertIndex = 0;
            while (insertIndex < _freeRanges.Count && _freeRanges[insertIndex].Offset < freeOffset)
                insertIndex++;

            FreeRange? previous = insertIndex > 0 ? _freeRanges[insertIndex - 1] : null;
            if (previous is not null && previous.Offset + previous.Size == freeOffset)
            {
                previous.Size += freeSize;
                if (insertIndex < _freeRanges.Count && _freeRanges[insertIndex].Offset == end)
                {
                    previous.Size += _freeRanges[insertIndex].Size;
                    RemoveRange(insertIndex);
                }
            }
            else if (insertIndex < _freeRanges.Count && _freeRanges[insertIndex].Offset == end)
            {
                _freeRanges[insertIndex].Offset = freeOffset;
                _freeRanges[insertIndex].Size += freeSize;
            }
            else
            {
                FreeRange range = RentRange(freeOffset, freeSize);
                _freeRanges.Insert(insertIndex, range);
            }
            Used -= freeSize;
        }
    }

    public ulong LargestFreeRange()
    {
        lock (_lock)
        {
            ulong largest = 0;
            foreach (FreeRange range in _freeRanges)
                largest = Math.Max(largest, range.Size);
            return largest;
        }
    }

    public int FreeRangeCount
    {
        get
        {
            lock (_lock)
                return _freeRanges.Count;
        }
    }

    private static ulong AlignUp(ulong value, ulong alignment)
    {
        ulong remainder = value % alignment;
        return remainder == 0 ? value : value + (alignment - remainder);
    }

    private FreeRange RentRange(ulong offset, ulong size)
    {
        if (_freeRangePool.Count > 0)
        {
            FreeRange range = _freeRangePool[^1];
            _freeRangePool.RemoveAt(_freeRangePool.Count - 1);
            range.Offset = offset;
            range.Size = size;
            return range;
        }
        return new FreeRange { Offset = offset, Size = size };
    }

    private void RemoveRange(int index)
    {
        FreeRange range = _freeRanges[index];
        _freeRanges.RemoveAt(index);
        if (_freeRangePool.Count < 64)
            _freeRangePool.Add(range);
    }
}

public static class MemoryBlockAllocatorExtensions
{
    public static bool TryAllocate(this MemoryBlockAllocator allocator, ulong size, out ulong offset) =>
        allocator.TryAllocate(size, MemoryBlockAllocator.DefaultAlignment, out offset);
}
