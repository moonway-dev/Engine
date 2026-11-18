using System.Runtime.InteropServices;
using Engine.Mathematics;
using Engine.Rendering;
using Xunit;

namespace Engine.Tests.Rendering;

public class CascadedShadowTests
{
    [Fact]
    public void ShadowData_HasShaderCompatibleArraysAndOffsets()
    {
        Assert.Equal(496, Marshal.SizeOf<ShadowData>());
        Assert.Equal(256, Marshal.OffsetOf<ShadowData>(nameof(ShadowData.AtlasTiles)).ToInt32());
        Assert.Equal(320, Marshal.OffsetOf<ShadowData>(nameof(ShadowData.CascadeMetrics)).ToInt32());
        Assert.Equal(384, Marshal.OffsetOf<ShadowData>(nameof(ShadowData.SplitDepths)).ToInt32());
        Assert.Equal(480, Marshal.OffsetOf<ShadowData>(nameof(ShadowData.LightIndex)).ToInt32());
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.7f)]
    [InlineData(1f)]
    public void Splits_AreOrderedAndEndAtShadowDistance(float lambda)
    {
        float[] splits = CascadedShadows.SplitDepths(0.1f, 80f, lambda);
        Assert.Equal(4, splits.Length);
        Assert.Equal(80f, splits[^1]);
        float previous = 0.1f;
        foreach (float split in splits)
        {
            Assert.True(split > previous);
            previous = split;
        }
    }

    [Fact]
    public void Cascades_CoverFrustumIncludingTransitionOverlap()
    {
        Camera camera = new() { Position = new Vector3(3f, 2f, 7f), AspectRatio = 1.7f };
        camera.LookAt(new Vector3(-2f, 1f, -8f));
        CascadedShadowSettings settings = new();
        ShadowData shadows = CascadedShadows.Build(camera, Light.Directional(new Vector3(0.5f, -1f, 0.3f), Vector3.One), 2, settings, []);
        float[] splits = CascadedShadows.SplitDepths(camera.NearPlane, settings.Distance, settings.SplitLambda);
        for (int cascade = 0; cascade < 4; cascade++)
        {
            foreach (Vector3 corner in CascadedShadows.FrustumCorners(camera, shadows.CascadeMetrics[cascade].W, splits[cascade]))
            {
                Vector3 clip = Vector3.TransformPoint(corner, shadows.LightViewProjections[cascade]);
                Assert.InRange(clip.X, -1.00001f, 1.00001f);
                Assert.InRange(clip.Y, -1.00001f, 1.00001f);
                Assert.InRange(clip.Z, -0.00001f, 1.00001f);
            }
        }
        Assert.Equal(2, shadows.LightIndex);
        Assert.Equal(1f, shadows.LightDirectionAndStrength.W);
    }

    [Fact]
    public void CascadeScale_IsStableDuringCameraRotation()
    {
        Camera camera = new();
        Light light = Light.Directional(new Vector3(0.4f, -1f, 0.2f), Vector3.One);
        ShadowData first = CascadedShadows.Build(camera, light, 0, new(), []);
        camera.Rotation = Quaternion.FromAxisAngle(Vector3.Up, 0.5f);
        ShadowData second = CascadedShadows.Build(camera, light, 0, new(), []);
        for (int index = 0; index < 4; index++)
        {
            Assert.Equal(first.CascadeMetrics[index].X, second.CascadeMetrics[index].X, 5);
            Assert.Equal(first.CascadeMetrics[index].Z, second.CascadeMetrics[index].Z, 5);
        }
    }

    [Fact]
    public void OffCameraCaster_IsIncludedWhenItCanShadowReceivers()
    {
        Camera camera = new();
        Light light = Light.Directional(Vector3.Down, Vector3.One);
        ShadowCasterBounds caster = new(new Vector3(-1f, 100f, -5f), new Vector3(1f, 102f, -3f));
        ShadowData data = CascadedShadows.Build(camera, light, 0, new(), [caster]);
        Vector3 clip = Vector3.TransformPoint(new Vector3(0f, 101f, -4f), data.LightViewProjections[0]);
        Assert.InRange(clip.X, -1f, 1f);
        Assert.InRange(clip.Y, -1f, 1f);
        Assert.InRange(clip.Z, 0f, 1f);
    }

    [Fact]
    public void AtlasTiles_HaveGuttersAndNeverOverlap()
    {
        for (int index = 0; index < 4; index++)
        {
            Rect tile = CascadedShadows.Tile(index, 1024);
            Assert.InRange(tile.Left, 0f, CascadedShadows.AtlasSize(1024));
            Assert.InRange(tile.Right, 0f, CascadedShadows.AtlasSize(1024));
            for (int other = index + 1; other < 4; other++)
                Assert.False(tile.Intersects(CascadedShadows.Tile(other, 1024)));
        }
        Assert.True(CascadedShadows.Gutter > new CascadedShadowSettings().FilterRadius);
    }

    [Fact]
    public void NoLight_DisablesShadowsAndInvalidSettingsAreRejected()
    {
        ShadowData data = CascadedShadows.Build(new(), null, -1, new(), []);
        Assert.Equal(-1, data.LightIndex);
        Assert.Equal(0f, data.LightDirectionAndStrength.W);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CascadedShadowSettings { Resolution = 1000 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new CascadedShadowSettings { Distance = float.NaN }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new CascadedShadowSettings { FilterRadius = CascadedShadows.Gutter }.Validate());
    }
}
