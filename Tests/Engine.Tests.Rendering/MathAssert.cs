using Engine.Mathematics;
using Xunit;

namespace Engine.Tests.Rendering;

public static class MathAssert
{
    public static void Equal(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
    {
        Assert.True(
            MathF.Abs(expected.X - actual.X) <= tolerance &&
            MathF.Abs(expected.Y - actual.Y) <= tolerance &&
            MathF.Abs(expected.Z - actual.Z) <= tolerance,
            $"Expected {expected}, actual {actual} (tolerance {tolerance}).");
    }
}
