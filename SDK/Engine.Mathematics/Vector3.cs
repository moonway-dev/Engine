namespace Engine.Mathematics;

public struct Vector3 : IEquatable<Vector3>
{
    public float X;
    public float Y;
    public float Z;

    public static Vector3 Zero => default;
    public static Vector3 One => new(1f, 1f, 1f);
    public static Vector3 UnitX => new(1f, 0f, 0f);
    public static Vector3 UnitY => new(0f, 1f, 0f);
    public static Vector3 UnitZ => new(0f, 0f, 1f);
    public static Vector3 Forward => new(0f, 0f, -1f);
    public static Vector3 Backward => new(0f, 0f, 1f);
    public static Vector3 Up => new(0f, 1f, 0f);
    public static Vector3 Down => new(0f, -1f, 0f);
    public static Vector3 Right => new(1f, 0f, 0f);
    public static Vector3 Left => new(-1f, 0f, 0f);

    public Vector3(float value)
    {
        X = value;
        Y = value;
        Z = value;
    }

    public Vector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public Vector3(Vector2 value, float z)
    {
        X = value.X;
        Y = value.Y;
        Z = z;
    }

    public Vector2 XY => new(X, Y);

    public float Length => MathF.Sqrt(LengthSquared);
    public float LengthSquared => X * X + Y * Y + Z * Z;

    public Vector3 Normalized
    {
        get
        {
            float length = Length;
            return length > MathUtil.Epsilon ? this / length : Zero;
        }
    }

    public static float Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vector3 Cross(Vector3 a, Vector3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public static float Distance(Vector3 a, Vector3 b) => (a - b).Length;
    public static float DistanceSquared(Vector3 a, Vector3 b) => (a - b).LengthSquared;
    public static Vector3 Min(Vector3 a, Vector3 b) => new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Min(a.Z, b.Z));
    public static Vector3 Max(Vector3 a, Vector3 b) => new(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y), MathF.Max(a.Z, b.Z));
    public static Vector3 Abs(Vector3 value) => new(MathF.Abs(value.X), MathF.Abs(value.Y), MathF.Abs(value.Z));
    public static Vector3 Clamp(Vector3 value, Vector3 min, Vector3 max) => Min(Max(value, min), max);
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;

    public static float AngleBetween(Vector3 a, Vector3 b)
    {
        float length = a.Length * b.Length;
        if (length < MathUtil.Epsilon)
            return 0f;
        return MathF.Acos(MathUtil.Clamp(Dot(a, b) / length, -1f, 1f));
    }

    public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDelta)
    {
        Vector3 delta = target - current;
        float distance = delta.Length;
        if (distance <= maxDelta || distance < MathUtil.Epsilon)
            return target;
        return current + delta / distance * maxDelta;
    }

    public static Vector3 TransformDirection(Vector3 value, in Matrix4x4 matrix) => new(
        value.X * matrix.M11 + value.Y * matrix.M12 + value.Z * matrix.M13,
        value.X * matrix.M21 + value.Y * matrix.M22 + value.Z * matrix.M23,
        value.X * matrix.M31 + value.Y * matrix.M32 + value.Z * matrix.M33);

    public static Vector3 TransformPoint(Vector3 value, in Matrix4x4 matrix) => new(
        value.X * matrix.M11 + value.Y * matrix.M12 + value.Z * matrix.M13 + matrix.M14,
        value.X * matrix.M21 + value.Y * matrix.M22 + value.Z * matrix.M23 + matrix.M24,
        value.X * matrix.M31 + value.Y * matrix.M32 + value.Z * matrix.M33 + matrix.M34);

    public static Vector3 Rotate(Vector3 value, Quaternion rotation) => value.RotateBy(rotation);

    public void Normalize() => this = Normalized;

    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3 operator -(Vector3 value) => new(-value.X, -value.Y, -value.Z);
    public static Vector3 operator *(Vector3 a, Vector3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    public static Vector3 operator *(Vector3 value, float scalar) => new(value.X * scalar, value.Y * scalar, value.Z * scalar);
    public static Vector3 operator *(float scalar, Vector3 value) => value * scalar;
    public static Vector3 operator /(Vector3 value, float scalar) => new(value.X / scalar, value.Y / scalar, value.Z / scalar);
    public static bool operator ==(Vector3 a, Vector3 b) => a.Equals(b);
    public static bool operator !=(Vector3 a, Vector3 b) => !a.Equals(b);

    public bool Equals(Vector3 other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is Vector3 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => $"({X}, {Y}, {Z})";
}
