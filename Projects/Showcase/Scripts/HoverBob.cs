using Engine.Mathematics;
using Engine.Scripting;

public sealed class HoverBob : ScriptBehaviour
{
    [InspectorRange(0.05f, 2f)]
    public float Amplitude = 0.25f;

    [InspectorRange(0.1f, 6f)]
    public float Frequency = 1.2f;

    private Vector3 _origin;

    public override void OnStart()
    {
        _origin = Transform.Position;
    }

    public override void OnUpdate()
    {
        float offset = MathF.Sin(ScriptTime.TotalTime * Frequency * MathF.PI) * Amplitude;
        Transform.Position = _origin + Vector3.UnitY * offset;
    }
}
