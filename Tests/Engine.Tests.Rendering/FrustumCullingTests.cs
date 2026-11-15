using Engine.Mathematics;
using Engine.Rendering;
using Xunit;

namespace Engine.Tests.Rendering;

public class FrustumCullingTests
{
    [Theory]
    [InlineData(0f, 0f, 0.5f, true)]
    [InlineData(2f, 0f, 0.5f, false)]
    [InlineData(-2f, 0f, 0.5f, false)]
    [InlineData(0f, 2f, 0.5f, false)]
    [InlineData(0f, -2f, 0.5f, false)]
    [InlineData(0f, 0f, -1f, false)]
    [InlineData(0f, 0f, 2f, false)]
    [InlineData(1.1f, 0f, 0.5f, true)]
    public void ClipBounds_AreConservative(float x, float y, float z, bool visible)
    {
        Vector3 center = new(x, y, z);
        Vector3 radius = new(0.25f, 0.25f, 0.25f);
        Assert.Equal(visible, FrustumCulling.IsVisible(center - radius, center + radius, Matrix4x4.Identity));
    }

    [Fact]
    public void NonFiniteTransform_IsNotCulled()
    {
        Matrix4x4 matrix = Matrix4x4.Identity;
        matrix.M11 = float.NaN;
        Assert.True(FrustumCulling.IsVisible(Vector3.Zero, Vector3.One, matrix));
    }

    [Fact]
    public void RotatedNegativeNonUniformScale_RemainsConservative()
    {
        Matrix4x4 transform = Matrix4x4.CreateTranslation(new Vector3(0f, 0f, 0.5f))
            * Matrix4x4.CreateRotationZ(0.7f)
            * Matrix4x4.CreateScale(new Vector3(-2f, 0.5f, 0.25f));
        Assert.True(FrustumCulling.IsVisible(new Vector3(-0.25f), new Vector3(0.25f), transform));
        transform = Matrix4x4.CreateTranslation(new Vector3(10f, 0f, 0f)) * transform;
        Assert.False(FrustumCulling.IsVisible(new Vector3(-0.25f), new Vector3(0.25f), transform));
    }

    [Fact]
    public void BoundsContainingFrustum_AreNotCulled()
    {
        Assert.True(FrustumCulling.IsVisible(new Vector3(-10f), new Vector3(10f), Matrix4x4.Identity));
    }
}
