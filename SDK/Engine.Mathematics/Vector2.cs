namespace Engine.Mathematics;

public struct Vector2 : IEquatable<Vector2>
{
    public float X;
    public float Y;

    public static Vector2 Zero => default;
    public static Vector2 One => new(1f, 1f);
    public static Vector2 UnitX => new(1f, 0f);
    public static Vector2 UnitY => new(0f, 1f);

    public Vector2(float value)
    {
        X = value;
        Y = value;
    }

    public Vector2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public float Length => MathF.Sqrt(LengthSquared);
    public float LengthSquared => X * X + Y * Y;

    public Vector2 Normalized
    {
        get
        {
            float length = Length;
            return length > MathUtil.Epsilon ? this / length : Zero;
        }
    }

    public static float Dot(Vector2 a, Vector2 b) => a.X * b.X + a.Y * b.Y;
    public static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    public static float Distance(Vector2 a, Vector2 b) => (a - b).Length;
    public static float DistanceSquared(Vector2 a, Vector2 b) => (a - b).LengthSquared;
    public static Vector2 Min(Vector2 a, Vector2 b) => new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y));
    public static Vector2 Max(Vector2 a, Vector2 b) => new(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));
    public static Vector2 Abs(Vector2 value) => new(MathF.Abs(value.X), MathF.Abs(value.Y));
    public static Vector2 Clamp(Vector2 value, Vector2 min, Vector2 max) => Min(Max(value, min), max);
    public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * t;

    public static Vector2 Transform(Vector2 value, in Matrix4x4 matrix) => new(
        value.X * matrix.M11 + value.Y * matrix.M12 + matrix.M14,
        value.X * matrix.M21 + value.Y * matrix.M22 + matrix.M24);

    public void Normalize() => this = Normalized;

    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2 operator -(Vector2 value) => new(-value.X, -value.Y);
    public static Vector2 operator *(Vector2 a, Vector2 b) => new(a.X * b.X, a.Y * b.Y);
    public static Vector2 operator *(Vector2 value, float scalar) => new(value.X * scalar, value.Y * scalar);
    public static Vector2 operator *(float scalar, Vector2 value) => value * scalar;
    public static Vector2 operator /(Vector2 value, float scalar) => new(value.X / scalar, value.Y / scalar);
    public static bool operator ==(Vector2 a, Vector2 b) => a.Equals(b);
    public static bool operator !=(Vector2 a, Vector2 b) => !a.Equals(b);

    public bool Equals(Vector2 other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Vector2 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X}, {Y})";
}
