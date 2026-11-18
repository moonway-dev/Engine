namespace Engine.Rendering;

internal sealed class RayTracingMaterialTexturePool
{
    private readonly int _defaultTextureOffset;
    private readonly Dictionary<Material, uint> _offsets = [];
    private int _allocatedSlots;

    public RayTracingMaterialTexturePool(int capacity)
    {
        if (capacity < RayTracingTextureCapacity.TexturesPerMaterial
            || capacity % RayTracingTextureCapacity.TexturesPerMaterial != 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _defaultTextureOffset = capacity - RayTracingTextureCapacity.TexturesPerMaterial;
    }

    public int DefaultTextureOffset => _defaultTextureOffset;

    public (uint Offset, bool AllocateTextures) Assign(Material material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (_offsets.TryGetValue(material, out uint existingOffset))
            return (existingOffset, false);
        if (_allocatedSlots * RayTracingTextureCapacity.TexturesPerMaterial < _defaultTextureOffset)
        {
            uint offset = (uint)(_allocatedSlots * RayTracingTextureCapacity.TexturesPerMaterial);
            _allocatedSlots++;
            _offsets.Add(material, offset);
            return (offset, true);
        }
        uint fallbackOffset = (uint)_defaultTextureOffset;
        _offsets.Add(material, fallbackOffset);
        return (fallbackOffset, false);
    }
}
