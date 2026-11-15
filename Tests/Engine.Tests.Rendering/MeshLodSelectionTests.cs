using Engine.Rendering;
using Xunit;

namespace Engine.Tests.Rendering;

public class MeshLodSelectionTests
{
    [Fact]
    public void Select_DistanceResolutionAndScale_ChangeLevel()
    {
        float[] errors = [0, 0.01f, 0.03f, 0.1f];
        Assert.Equal(0, MeshLodSelection.Select(errors, 200, 1, 1));
        Assert.Equal(1, MeshLodSelection.Select(errors, 50, 1, 1));
        Assert.Equal(2, MeshLodSelection.Select(errors, 20, 1, 1));
        Assert.Equal(3, MeshLodSelection.Select(errors, 5, 1, 1));
        Assert.Equal(MeshLodSelection.Select(errors, 50, 2, 1), MeshLodSelection.Select(errors, 50, -2, 1));
    }

    [Fact]
    public void Select_Hysteresis_KeepsPreviousLevelNearThreshold()
    {
        float[] errors = [0, 0.01f];
        Assert.Equal(0, MeshLodSelection.Select(errors, 100, 1, 1, 0));
        Assert.Equal(1, MeshLodSelection.Select(errors, 100, 1, 1, 1));
        Assert.Equal(1, MeshLodSelection.Select(errors, 80, 1, 1, 0));
        Assert.Equal(0, MeshLodSelection.Select(errors, 120, 1, 1, 1));
    }

    [Fact]
    public void Select_InvalidProjection_UsesOriginal()
    {
        float[] errors = [0, 0.01f];
        Assert.Equal(0, MeshLodSelection.Select(errors, float.NaN, 1, 1));
        Assert.Equal(0, MeshLodSelection.Select(errors, 1, float.PositiveInfinity, 1));
        Assert.Equal(0, MeshLodSelection.Select(errors, 1, 1, 0));
    }
}
