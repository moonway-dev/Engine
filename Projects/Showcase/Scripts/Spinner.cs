using Engine.Mathematics;
using Engine.Scripting;

public sealed class Spinner : ScriptBehaviour
{
    [InspectorRange(0f, 720f)]
    public float DegreesPerSecond = 90f;

    [InspectorTooltip("Rotation axis in local space.")]
    public Vector3 Axis = Vector3.UnitY;

    public override void OnUpdate()
    {
        Transform.LocalRotation =
            Quaternion.FromAxisAngle(Axis, ScriptTime.TotalTime * MathUtil.DegToRad * DegreesPerSecond) *
            Quaternion.FromAxisAngle(Vector3.UnitX, ScriptTime.TotalTime * 0.3f);
    }
}
