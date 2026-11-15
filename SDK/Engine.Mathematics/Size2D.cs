namespace Engine.Mathematics;

public struct Size2D : IEquatable<Size2D>
{
    public int Width;
    public int Height;

    public static Size2D Zero => default;

    public Size2D(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public readonly int Area => Width * Height;
    public readonly float AspectRatio => Height == 0 ? 0f : (float)Width / Height;

    public readonly Size2D WithWidth(int width) => new(width, Height);
    public readonly Size2D WithHeight(int height) => new(Width, height);
    public readonly Size2D ClampedTo(Size2D min, Size2D max) => new(
        MathUtil.Clamp(Width, min.Width, max.Width),
        MathUtil.Clamp(Height, min.Height, max.Height));

    public static Size2D operator *(Size2D size, float scale) => new((int)(size.Width * scale), (int)(size.Height * scale));
    public static bool operator ==(Size2D a, Size2D b) => a.Equals(b);
    public static bool operator !=(Size2D a, Size2D b) => !a.Equals(b);

    public bool Equals(Size2D other) => Width == other.Width && Height == other.Height;
    public override bool Equals(object? obj) => obj is Size2D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Width, Height);
    public override string ToString() => $"{Width}x{Height}";
}
