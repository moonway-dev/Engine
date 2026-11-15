using Engine.Mathematics;

namespace Engine.Rendering;

public sealed class Camera
{
    private float _fieldOfViewY = MathUtil.DegToRad * 60f;

    public Vector3 Position { get; set; } = new(0f, 0f, 5f);
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public float NearPlane { get; set; } = 0.1f;
    public float FarPlane { get; set; } = 500f;
    public float AspectRatio { get; set; } = 16f / 9f;

    public float FieldOfViewY
    {
        get => _fieldOfViewY;
        set
        {
            if (value <= 0f || value >= MathUtil.Pi)
                throw new ArgumentOutOfRangeException(nameof(value));
            _fieldOfViewY = value;
        }
    }

    public Vector3 Forward => Vector3.Forward.RotateBy(Rotation);
    public Vector3 Right => Vector3.Right.RotateBy(Rotation);
    public Vector3 Up => Vector3.Up.RotateBy(Rotation);

    public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Position + Forward, Up);

    public Matrix4x4 ProjectionMatrix
    {
        get
        {
            Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(FieldOfViewY, AspectRatio, NearPlane, FarPlane);
            projection.M22 = -projection.M22;
            return projection;
        }
    }

    public Matrix4x4 ViewProjection => ProjectionMatrix * ViewMatrix;

    public void LookAt(Vector3 target) => LookAt(target, Vector3.Up);

    public void LookAt(Vector3 target, Vector3 up)
    {
        Vector3 forward = (target - Position).Normalized;
        if (forward.Length < MathUtil.Epsilon)
            return;
        Rotation = Quaternion.LookRotation(forward, up);
    }
}
