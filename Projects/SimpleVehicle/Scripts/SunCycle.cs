using Engine.Mathematics;
using Engine.Scene;
using Engine.Scripting;

public sealed class SunCycle : ScriptBehaviour
{
    [InspectorTooltip("Length of a full day-night cycle in seconds.")]
    [InspectorRange(10f, 600f)]
    public float DayLengthSeconds = 240f;
    [InspectorTooltip("Starting time of day: 0 - sunrise, 0.25 - noon, 0.5 - sunset, 0.75 - midnight.")]
    [InspectorRange(0f, 1f)]
    public float StartTimeOfDay = 0.1f;
    [InspectorTooltip("Azimuth of the sun path in degrees; 0 rises over -Z, 90 over +X.")]
    [InspectorRange(-180f, 180f)]
    public float AzimuthDegrees = 25f;
    [InspectorTooltip("Freezes the sun at its current position.")]
    public bool Paused;
    [InspectorTooltip("Directional light intensity at full day.")]
    [InspectorRange(0f, 10f)]
    public float DayIntensity = 2.5f;
    [InspectorTooltip("Directional light intensity kept after the sun drops below the horizon.")]
    [InspectorRange(0f, 2f)]
    public float NightIntensity = 0.08f;

    private float _elapsed;
    private Engine.Scene.Light? _light;

    public override void OnStart()
    {
        _elapsed = MathUtil.Clamp(StartTimeOfDay, 0f, 1f) * MathF.Max(DayLengthSeconds, 0.1f);
        _light = Owner.GetComponent<Engine.Scene.Light>();
        Apply();
    }

    public override void OnUpdate()
    {
        if (!Paused)
            _elapsed += ScriptTime.DeltaTime;
        Apply();
    }

    private void Apply()
    {
        float dayLength = MathF.Max(DayLengthSeconds, 0.1f);
        float angle = (_elapsed / dayLength) * MathF.PI * 2f;
        float azimuth = AzimuthDegrees * MathUtil.DegToRad;
        Transform.Rotation = Quaternion.FromAxisAngle(Vector3.UnitY, azimuth)
            * Quaternion.FromAxisAngle(Vector3.UnitX, -angle);
        if (_light is not null)
        {
            float elevation = MathF.Sin(angle);
            float dayBlend = MathUtil.SmoothStep(0f, 1f, MathUtil.InverseLerp(-0.12f, 0.12f, elevation));
            _light.Intensity = MathUtil.Lerp(NightIntensity, DayIntensity, dayBlend);
        }
    }
}
