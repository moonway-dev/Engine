using Engine.Mathematics;
using Engine.Rendering;
using Xunit;

namespace Engine.Tests.Rendering;

public sealed class RayTracingMaterialTexturePoolTests
{
    [Fact]
    public void Assign_ReusesTextureSetForSameMaterial()
    {
        RayTracingMaterialTexturePool pool = new(24);
        Material material = new();

        (uint firstOffset, bool firstAllocation) = pool.Assign(material);
        (uint repeatedOffset, bool repeatedAllocation) = pool.Assign(material);

        Assert.Equal(0u, firstOffset);
        Assert.True(firstAllocation);
        Assert.Equal(firstOffset, repeatedOffset);
        Assert.False(repeatedAllocation);
    }

    [Fact]
    public void Assign_MoreThanSixteenMaterialsKeepsEveryOffsetInsideCapacity()
    {
        const int capacity = 120;
        RayTracingMaterialTexturePool pool = new(capacity);
        Material[] materials = Enumerable.Range(0, 20).Select(_ => new Material()).ToArray();

        (uint Offset, bool AllocateTextures)[] assignments = materials.Select(pool.Assign).ToArray();

        Assert.Equal(0u, assignments[0].Offset);
        Assert.Equal(96u, assignments[16].Offset);
        Assert.Equal(102u, assignments[17].Offset);
        Assert.Equal(108u, assignments[18].Offset);
        Assert.Equal(114u, assignments[19].Offset);
        Assert.All(assignments, assignment => Assert.InRange(assignment.Offset, 0u, capacity - 6u));
        Assert.All(assignments.Take(19), assignment => Assert.True(assignment.AllocateTextures));
        Assert.False(assignments[19].AllocateTextures);
    }

    [Fact]
    public void Assign_OverflowUsesReservedDefaultSetAndPreservesMaterialData()
    {
        RayTracingMaterialTexturePool pool = new(18);
        Material material = new() { AlbedoColor = new Vector3(0.1f, 0.2f, 0.3f), Metallic = 0.75f, Roughness = 0.2f };

        Assert.True(pool.Assign(new Material()).AllocateTextures);
        Assert.True(pool.Assign(new Material()).AllocateTextures);
        (uint offset, bool allocateTextures) = pool.Assign(material);

        Assert.Equal(12u, offset);
        Assert.False(allocateTextures);
        Assert.Equal(new Vector3(0.1f, 0.2f, 0.3f), material.ToData().AlbedoColor);
        Assert.Equal(0.75f, material.ToData().Metallic);
        Assert.Equal(0.2f, material.ToData().Roughness);
    }
}
