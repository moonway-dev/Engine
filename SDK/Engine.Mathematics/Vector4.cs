namespace Engine.Mathematics;

public struct Vector4 : IEquatable<Vector4>
{
    public float X;
    public float Y;
    public float Z;
    public float W;

    public static Vector4 Zero => default;
    public static Vector4 One => new(1f, 1f, 1f, 1f);
    public static Vector4 UnitX => new(1f, 0f, 0f, 0f);
    public static Vector4 UnitY => new(0f, 1f, 0f, 0f);
    public static Vector4 UnitZ => new(0f, 0f, 1f, 0f);
    public static Vector4 UnitW => new(0f, 0f, 0f, 1f);

    public Vector4(float value)
    {
        X = value;
        Y = value;
        Z = value;
        W = value;
    }

    public Vector4(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public Vector4(Vector3 value, float w)
    {
        X = value.X;
        Y = value.Y;
        Z = value.Z;
        W = w;
    }

    public Vector3 XYZ => new(X, Y, Z);

    public float Length => MathF.Sqrt(LengthSquared);
    public float LengthSquared => X * X + Y * Y + Z * Z + W * W;

    public Vector4 Normalized
    {
        get
        {
            float length = Length;
            return length > MathUtil.Epsilon ? this / length : Zero;
        }
    }

    public static float Dot(Vector4 a, Vector4 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
    public static Vector4 Min(Vector4 a, Vector4 b) => new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Min(a.Z, b.Z), MathF.Min(a.W, b.W));
    public static Vector4 Max(Vector4 a, Vector4 b) => new(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y), MathF.Max(a.Z, b.Z), MathF.Max(a.W, b.W));
    public static Vector4 Clamp(Vector4 value, Vector4 min, Vector4 max) => Min(Max(value, min), max);
    public static Vector4 Lerp(Vector4 a, Vector4 b, float t) => a + (b - a) * t;

    public static Vector4 Transform(Vector4 value, in Matrix4x4 matrix) => new(
        value.X * matrix.M11 + value.Y * matrix.M12 + value.Z * matrix.M13 + value.W * matrix.M14,
        value.X * matrix.M21 + value.Y * matrix.M22 + value.Z * matrix.M23 + value.W * matrix.M24,
        value.X * matrix.M31 + value.Y * matrix.M32 + value.Z * matrix.M33 + value.W * matrix.M34,
        value.X * matrix.M41 + value.Y * matrix.M42 + value.Z * matrix.M43 + value.W * matrix.M44);

    public void Normalize() => this = Normalized;

    public static Vector4 operator +(Vector4 a, Vector4 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    public static Vector4 operator -(Vector4 a, Vector4 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
    public static Vector4 operator -(Vector4 value) => new(-value.X, -value.Y, -value.Z, -value.W);
    public static Vector4 operator *(Vector4 a, Vector4 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
    public static Vector4 operator *(Vector4 value, float scalar) => new(value.X * scalar, value.Y * scalar, value.Z * scalar, value.W * scalar);
    public static Vector4 operator *(float scalar, Vector4 value) => value * scalar;
    public static Vector4 operator /(Vector4 value, float scalar) => new(value.X / scalar, value.Y / scalar, value.Z / scalar, value.W / scalar);
    public static bool operator ==(Vector4 a, Vector4 b) => a.Equals(b);
    public static bool operator !=(Vector4 a, Vector4 b) => !a.Equals(b);

    public bool Equals(Vector4 other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
    public override bool Equals(object? obj) => obj is Vector4 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
    public override string ToString() => $"({X}, {Y}, {Z}, {W})";
}
