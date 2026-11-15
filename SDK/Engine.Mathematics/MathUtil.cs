namespace Engine.Mathematics;

public static class MathUtil
{
    public const float Pi = MathF.PI;
    public const float TwoPi = MathF.PI * 2f;
    public const float HalfPi = MathF.PI * 0.5f;
    public const float Epsilon = 1e-6f;
    public const float DegToRad = MathF.PI / 180f;
    public const float RadToDeg = 180f / MathF.PI;

    public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public static float InverseLerp(float a, float b, float value) =>
        MathF.Abs(b - a) < Epsilon ? 0f : (value - a) / (b - a);

    public static float Remap(float value, float fromMin, float fromMax, float toMin, float toMax) =>
        Lerp(toMin, toMax, InverseLerp(fromMin, fromMax, value));

    public static float SmoothStep(float a, float b, float t)
    {
        t = Clamp(t, 0f, 1f);
        t = t * t * (3f - 2f * t);
        return Lerp(a, b, t);
    }

    public static float MoveTowards(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta)
            return target;
        return current + MathF.Sign(target - current) * maxDelta;
    }

    public static bool Approximately(float a, float b) =>
        MathF.Abs(a - b) <= Epsilon * MathF.Max(1f, MathF.Max(MathF.Abs(a), MathF.Abs(b)));

    public static float WrapAngle(float radians)
    {
        float wrapped = radians % TwoPi;
        if (wrapped > Pi)
            wrapped -= TwoPi;
        else if (wrapped < -Pi)
            wrapped += TwoPi;
        return wrapped;
    }
}
