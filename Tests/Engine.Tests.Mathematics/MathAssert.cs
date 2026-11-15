using Engine.Mathematics;
using Xunit;

namespace Engine.Tests.Mathematics;

public static class MathAssert
{
    public static void Equal(Vector2 expected, Vector2 actual, float tolerance = 1e-5f)
    {
        Assert.True(
            MathF.Abs(expected.X - actual.X) <= tolerance && MathF.Abs(expected.Y - actual.Y) <= tolerance,
            $"Expected {expected}, actual {actual} (tolerance {tolerance}).");
    }

    public static void Equal(Vector3 expected, Vector3 actual, float tolerance = 1e-5f)
    {
        Assert.True(
            MathF.Abs(expected.X - actual.X) <= tolerance &&
            MathF.Abs(expected.Y - actual.Y) <= tolerance &&
            MathF.Abs(expected.Z - actual.Z) <= tolerance,
            $"Expected {expected}, actual {actual} (tolerance {tolerance}).");
    }

    public static void Equal(Vector4 expected, Vector4 actual, float tolerance = 1e-5f)
    {
        Assert.True(
            MathF.Abs(expected.X - actual.X) <= tolerance &&
            MathF.Abs(expected.Y - actual.Y) <= tolerance &&
            MathF.Abs(expected.Z - actual.Z) <= tolerance &&
            MathF.Abs(expected.W - actual.W) <= tolerance,
            $"Expected {expected}, actual {actual} (tolerance {tolerance}).");
    }

    public static void Equal(Quaternion expected, Quaternion actual, float tolerance = 1e-5f)
    {
        Assert.True(
            MathF.Abs(expected.X - actual.X) <= tolerance &&
            MathF.Abs(expected.Y - actual.Y) <= tolerance &&
            MathF.Abs(expected.Z - actual.Z) <= tolerance &&
            MathF.Abs(expected.W - actual.W) <= tolerance,
            $"Expected {expected}, actual {actual} (tolerance {tolerance}).");
    }
}
