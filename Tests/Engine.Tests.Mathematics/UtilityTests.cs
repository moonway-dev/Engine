using Engine.Mathematics;
using Xunit;

namespace Engine.Tests.Mathematics;

public class UtilityTests
{
    [Fact]
    public void MathUtil_Clamp()
    {
        Assert.Equal(5f, MathUtil.Clamp(3f, 5f, 10f));
        Assert.Equal(10f, MathUtil.Clamp(42f, 5f, 10f));
        Assert.Equal(7f, MathUtil.Clamp(7f, 5f, 10f));
        Assert.Equal(3, MathUtil.Clamp(3, 1, 5));
    }

    [Fact]
    public void MathUtil_LerpInverseLerpRemap()
    {
        Assert.Equal(7.5f, MathUtil.Lerp(5f, 10f, 0.5f), 6);
        Assert.Equal(0.5f, MathUtil.InverseLerp(5f, 10f, 7.5f), 5);
        Assert.Equal(100f, MathUtil.Remap(7.5f, 5f, 10f, 90f, 110f), 4);
    }

    [Fact]
    public void MathUtil_WrapAngle()
    {
        Assert.Equal(0f, MathUtil.WrapAngle(0f), 6);
        Assert.Equal(MathUtil.HalfPi, MathUtil.WrapAngle(MathUtil.HalfPi), 6);
        Assert.InRange(MathUtil.WrapAngle(MathUtil.TwoPi + 0.5f), -MathUtil.Pi, MathUtil.Pi);
        Assert.True(MathUtil.Approximately(MathUtil.WrapAngle(-MathUtil.TwoPi), 0f));
    }

    [Fact]
    public void MathUtil_MoveTowards()
    {
        Assert.Equal(3f, MathUtil.MoveTowards(1f, 3f, 2f), 6);
        Assert.Equal(2.5f, MathUtil.MoveTowards(1f, 3f, 1.5f), 6);
        Assert.Equal(3f, MathUtil.MoveTowards(3f, 1f, 0f));
    }

    [Fact]
    public void Color_ParseHex()
    {
        Assert.True(Color.TryParse("#FF0000", out Color red));
        Assert.Equal(Color.Red, red);
        Assert.True(Color.TryParse("#00FF00FF", out Color opaqueGreen));
        Assert.Equal(Color.Green, opaqueGreen);
        Assert.True(Color.TryParse("336699", out Color withoutHash));
        Assert.Equal(new Color(0x33, 0x66, 0x99, 255), withoutHash);
        Assert.False(Color.TryParse("nope", out _));
        Assert.Throws<FormatException>(() => Color.Parse("zzz"));
    }

    [Fact]
    public void Color_LerpEndpoints()
    {
        Color result = Color.Lerp(Color.Black, Color.White, 0.5f);
        Assert.Equal(0.5f, result.R, 5);
        Assert.Equal(0.5f, result.G, 5);
        Assert.Equal(0.5f, result.B, 5);
        Assert.Equal(1f, result.A, 5);
    }

    [Fact]
    public void Rect_ContainsAndIntersects()
    {
        Rect rect = new(0f, 0f, 10f, 10f);
        Assert.True(rect.Contains(new Vector2(5f, 5f)));
        Assert.False(rect.Contains(new Vector2(11f, 5f)));
        Assert.True(rect.Intersects(new Rect(5f, 5f, 10f, 10f)));
        Assert.False(rect.Intersects(new Rect(20f, 20f, 10f, 10f)));
        Assert.Equal(new Rect(0f, 0f, 5f, 5f), rect.Intersect(new Rect(0f, 0f, 5f, 5f)));
        Assert.Equal(new Vector2(5f, 5f), rect.Center);
    }

    [Fact]
    public void Size2D_AreaAndRatio()
    {
        Size2D size = new(1920, 1080);
        Assert.Equal(2073600, size.Area);
        Assert.Equal(16f / 9f, size.AspectRatio, 4);
        Assert.Equal(new Size2D(800, 1080), size.WithWidth(800));
    }

    [Fact]
    public void FastRandom_SameSeed_ProducesSameSequence()
    {
        FastRandom first = new(42);
        FastRandom second = new(42);
        for (int i = 0; i < 100; i++)
            Assert.Equal(first.NextFloat(), second.NextFloat());
    }

    [Fact]
    public void FastRandom_FloatInBounds()
    {
        FastRandom random = new(12345);
        for (int i = 0; i < 1000; i++)
        {
            float value = random.NextFloat(-3f, 7f);
            Assert.InRange(value, -3f, 7f);
        }
    }

    [Fact]
    public void FastRandom_UnitVectorsAreNormalized()
    {
        FastRandom random = new(777);
        for (int i = 0; i < 100; i++)
            Assert.Equal(1f, random.NextVector3OnUnitSphere().Length, 4);
    }
}
