using Engine.RHI;

namespace Engine.Rendering;

public sealed class RayTracingFrameSlotReadiness
{
    private readonly IAccelerationStructure?[] _submittedStructures;

    public RayTracingFrameSlotReadiness(int framesInFlight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framesInFlight);
        _submittedStructures = new IAccelerationStructure[framesInFlight];
    }

    public bool CanTrace(int slot, IAccelerationStructure? structure)
    {
        ValidateSlot(slot);
        return structure is { IsBuilt: true } && ReferenceEquals(_submittedStructures[slot], structure);
    }

    public void MarkSubmitted(int slot, IAccelerationStructure structure)
    {
        ValidateSlot(slot);
        ArgumentNullException.ThrowIfNull(structure);
        _submittedStructures[slot] = structure;
    }

    public void Invalidate(int slot)
    {
        ValidateSlot(slot);
        _submittedStructures[slot] = null;
    }

    private void ValidateSlot(int slot)
    {
        if ((uint)slot >= (uint)_submittedStructures.Length)
            throw new ArgumentOutOfRangeException(nameof(slot));
    }
}
