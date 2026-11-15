namespace Engine.Mathematics;

public struct Color : IEquatable<Color>
{
    public float R;
    public float G;
    public float B;
    public float A;

    public static Color White => new(1f, 1f, 1f);
    public static Color Black => new(0f, 0f, 0f);
    public static Color Red => new(1f, 0f, 0f);
    public static Color Green => new(0f, 1f, 0f);
    public static Color Blue => new(0f, 0f, 1f);
    public static Color Yellow => new(1f, 1f, 0f);
    public static Color Cyan => new(0f, 1f, 1f);
    public static Color Magenta => new(1f, 0f, 1f);
    public static Color Gray => new(0.5f, 0.5f, 0.5f);
    public static Color Transparent => new(0f, 0f, 0f, 0f);

    public Color(float r, float g, float b)
    {
        R = r;
        G = g;
        B = b;
        A = 1f;
    }

    public Color(float r, float g, float b, float a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public Color(byte r, byte g, byte b, byte a)
    {
        R = r / 255f;
        G = g / 255f;
        B = b / 255f;
        A = a / 255f;
    }

    public Color(Vector3 rgb)
    {
        R = rgb.X;
        G = rgb.Y;
        B = rgb.Z;
        A = 1f;
    }

    public Color(Vector4 rgba)
    {
        R = rgba.X;
        G = rgba.Y;
        B = rgba.Z;
        A = rgba.W;
    }

    public Vector3 ToVector3() => new(R, G, B);
    public Vector4 ToVector4() => new(R, G, B, A);

    public static Color Lerp(Color a, Color b, float t) => new(
        MathUtil.Lerp(a.R, b.R, t),
        MathUtil.Lerp(a.G, b.G, t),
        MathUtil.Lerp(a.B, b.B, t),
        MathUtil.Lerp(a.A, b.A, t));

    public static bool TryParse(string text, out Color color)
    {
        color = default;
        string value = text.TrimStart('#');
        if (value.Length is not (6 or 8) || !IsHexString(value))
            return false;
        byte r = ParseHexByte(value, 0);
        byte g = ParseHexByte(value, 2);
        byte b = ParseHexByte(value, 4);
        byte a = value.Length == 8 ? ParseHexByte(value, 6) : (byte)255;
        color = new Color(r, g, b, a);
        return true;
    }

    public static Color Parse(string text) =>
        TryParse(text, out Color color) ? color : throw new FormatException($"Invalid color format: '{text}'.");

    private static bool IsHexString(string value)
    {
        foreach (char c in value)
        {
            bool hex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!hex)
                return false;
        }
        return true;
    }

    private static byte ParseHexByte(string text, int offset) =>
        Convert.ToByte(text.Substring(offset, 2), 16);

    public static bool operator ==(Color a, Color b) => a.Equals(b);
    public static bool operator !=(Color a, Color b) => !a.Equals(b);

    public bool Equals(Color other) => R == other.R && G == other.G && B == other.B && A == other.A;
    public override bool Equals(object? obj) => obj is Color other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(R, G, B, A);
    public override string ToString() => $"({R}, {G}, {B}, {A})";
}
