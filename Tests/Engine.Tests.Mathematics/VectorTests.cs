using Engine.Mathematics;
using Xunit;

namespace Engine.Tests.Mathematics;

public class VectorTests
{
    [Fact]
    public void Vector3_Cross_KnownValues()
    {
        Vector3 result = Vector3.Cross(Vector3.UnitX, Vector3.UnitY);
        MathAssert.Equal(new Vector3(0f, 0f, 1f), result);
        Vector3 reversed = Vector3.Cross(Vector3.UnitY, Vector3.UnitX);
        MathAssert.Equal(new Vector3(0f, 0f, -1f), reversed);
    }

    [Fact]
    public void Vector3_Dot_OrthogonalIsZero()
    {
        Assert.Equal(0f, Vector3.Dot(Vector3.UnitX, Vector3.UnitY), 6);
        Assert.Equal(1f, Vector3.Dot(Vector3.UnitZ, Vector3.UnitZ), 6);
    }

    [Fact]
    public void Vector3_Normalize_ProducesUnitLength()
    {
        Vector3 normalized = new Vector3(3f, 4f, 0f).Normalized;
        MathAssert.Equal(new Vector3(0.6f, 0.8f, 0f), normalized);
        Assert.Equal(1f, normalized.Length, 5);
        MathAssert.Equal(Vector3.Zero, Vector3.Zero.Normalized);
    }

    [Fact]
    public void Vector3_Distance_KnownValues()
    {
        Assert.Equal(5f, Vector3.Distance(new Vector3(0f, 0f, 0f), new Vector3(3f, 4f, 0f)), 5);
        Assert.Equal(25f, Vector3.DistanceSquared(Vector3.Zero, new Vector3(3f, 4f, 0f)), 5);
    }

    [Fact]
    public void Vector3_TransformPoint_TranslatesByMatrix()
    {
        Matrix4x4 translation = Matrix4x4.CreateTranslation(new Vector3(10f, 20f, 30f));
        Vector3 result = Vector3.TransformPoint(new Vector3(1f, 2f, 3f), translation);
        MathAssert.Equal(new Vector3(11f, 22f, 33f), result);
    }

    [Fact]
    public void Vector3_TransformDirection_IgnoresTranslation()
    {
        Matrix4x4 combined = Matrix4x4.CreateTranslation(new Vector3(10f, 0f, 0f)) * Matrix4x4.CreateRotationY(MathUtil.HalfPi);
        Vector3 result = Vector3.TransformDirection(Vector3.UnitX, combined);
        MathAssert.Equal(new Vector3(0f, 0f, -1f), result);
    }

    [Fact]
    public void Vector3_Rotate_QuaternionRotation()
    {
        Quaternion rotation = Quaternion.FromAxisAngle(Vector3.UnitY, MathUtil.HalfPi);
        MathAssert.Equal(new Vector3(0f, 0f, -1f), Vector3.UnitX.RotateBy(rotation));
        MathAssert.Equal(new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, -1f).RotateBy(rotation));
    }

    [Fact]
    public void Vector2_Operators_WorkAsExpected()
    {
        Vector2 a = new(2f, 3f);
        Assert.Equal(new Vector2(4f, 6f), a * 2f);
        Assert.Equal(new Vector2(1f, 1.5f), a / 2f);
        Assert.Equal(new Vector2(5f, 7f), a + new Vector2(3f, 4f));
        Assert.Equal(new Vector2(-2f, -3f), -a);
    }

    [Fact]
    public void Vector4_Transform_ByMatrix()
    {
        Matrix4x4 scale = Matrix4x4.CreateScale(2f);
        Vector4 result = Vector4.Transform(new Vector4(1f, 2f, 3f, 1f), scale);
        Assert.Equal(new Vector4(2f, 4f, 6f, 1f), result);
    }

    [Fact]
    public void Lerp_And_Clamp()
    {
        MathAssert.Equal(new Vector3(1.5f, 1.5f, 1.5f), Vector3.Lerp(Vector3.Zero, Vector3.One * 3f, 0.5f));
        MathAssert.Equal(new Vector2(2f, 1f), Vector2.Clamp(new Vector2(10f, -10f), new Vector2(1f, 1f), new Vector2(2f, 2f)));
    }
}
