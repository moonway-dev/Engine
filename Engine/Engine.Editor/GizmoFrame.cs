using System.Runtime.CompilerServices;
using Engine.Mathematics;

[assembly: InternalsVisibleTo("Engine.Tests.Rendering")]

namespace Engine.Editor;

public enum TransformSpace
{
    Local,
    Global,
}

internal readonly record struct GizmoFrame(TransformMode Mode, TransformSpace Space, Quaternion Rotation)
{
    public TransformSpace EffectiveSpace => Mode == TransformMode.Scale ? TransformSpace.Local : Space;

    public Vector3 Axis(int index)
    {
        Vector3 axis = index switch
        {
            0 => Vector3.UnitX,
            1 => Vector3.UnitY,
            2 => Vector3.UnitZ,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
        return EffectiveSpace == TransformSpace.Local ? Rotation.Rotate(axis).Normalized : axis;
    }

    public Vector3 Translate(Vector3 start, int axis, float distance) => start + Axis(axis) * distance;

    public Quaternion Rotate(Quaternion start, int axis, float angle) =>
        Quaternion.FromAxisAngle(Axis(axis), angle) * start;
}
