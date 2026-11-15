namespace Engine.Mathematics;

public struct Rect : IEquatable<Rect>
{
    public float X;
    public float Y;
    public float Width;
    public float Height;

    public static Rect Empty => default;

    public Rect(float width, float height)
    {
        X = 0f;
        Y = 0f;
        Width = width;
        Height = height;
    }

    public Rect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public readonly float Left => X;
    public readonly float Right => X + Width;
    public readonly float Top => Y;
    public readonly float Bottom => Y + Height;
    public readonly bool IsEmpty => Width <= 0f || Height <= 0f;
    public readonly Vector2 Center => new(X + Width * 0.5f, Y + Height * 0.5f);
    public readonly Vector2 Size => new(Width, Height);
    public readonly Vector2 Position => new(X, Y);

    public readonly bool Contains(Vector2 point) =>
        point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;

    public readonly bool Contains(Rect rect) =>
        rect.X >= X && rect.Y >= Y && rect.Right <= Right && rect.Bottom <= Bottom;

    public readonly bool Intersects(Rect rect) =>
        rect.X <= Right && rect.Right >= X && rect.Y <= Bottom && rect.Bottom >= Top;

    public readonly Rect Intersect(Rect rect)
    {
        float left = MathF.Max(X, rect.X);
        float top = MathF.Max(Y, rect.Y);
        float right = MathF.Min(Right, rect.Right);
        float bottom = MathF.Min(Bottom, rect.Bottom);
        return right >= left && bottom >= top ? new Rect(left, top, right - left, bottom - top) : Empty;
    }

    public readonly Rect Offset(Vector2 offset) => new(X + offset.X, Y + offset.Y, Width, Height);

    public readonly Rect Inflate(float amount) => new(X - amount, Y - amount, Width + amount * 2f, Height + amount * 2f);

    public static bool operator ==(Rect a, Rect b) => a.Equals(b);
    public static bool operator !=(Rect a, Rect b) => !a.Equals(b);

    public bool Equals(Rect other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    public override bool Equals(object? obj) => obj is Rect other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    public override string ToString() => $"({X}, {Y}, {Width}, {Height})";
}
