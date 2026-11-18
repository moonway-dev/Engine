using Engine.RHI;
using Engine.Mathematics;
using Engine.Rendering;
using Xunit;

namespace Engine.Tests.Rendering;

public class RayTracingPolicyTests
{
    private static FeatureInfo Features(bool accelerationStructures, bool rayTracing) => new()
    {
        DeviceName = "test device",
        VendorName = "test vendor",
        ApiVersion = "1.0",
        DedicatedVideoMemory = 0,
        SupportsCompute = true,
        SupportsRayTracing = rayTracing,
        SupportsAccelerationStructures = accelerationStructures,
        SupportsMeshShaders = false,
    };

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Auto_UsesRasterWhenEitherRequiredFeatureIsMissing(bool accelerationStructures, bool rayTracing)
    {
        Assert.False(RayTracingPolicy.ShouldEnable(RayTracingMode.Auto, Features(accelerationStructures, rayTracing)));
    }

    [Fact]
    public void Auto_EnablesOnlyWhenBothRequiredFeaturesAreAvailable()
    {
        Assert.True(RayTracingPolicy.ShouldEnable(RayTracingMode.Auto, Features(true, true)));
    }

    [Fact]
    public void Off_DisablesRayTracingEvenWhenSupported()
    {
        Assert.False(RayTracingPolicy.ShouldEnable(RayTracingMode.Off, Features(true, true)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Required_ThrowsWhenEitherRequiredFeatureIsMissing(bool accelerationStructures, bool rayTracing)
    {
        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            RayTracingPolicy.ShouldEnable(RayTracingMode.Required, Features(accelerationStructures, rayTracing)));
        Assert.Contains("both acceleration structures and ray-tracing pipelines", exception.Message);
    }

    [Fact]
    public void Required_EnablesWhenBothRequiredFeaturesAreAvailable()
    {
        Assert.True(RayTracingPolicy.ShouldEnable(RayTracingMode.Required, Features(true, true)));
    }

    [Fact]
    public void FrameSlotReadiness_RequiresSubmittedBuiltStructureForSameSlot()
    {
        RayTracingFrameSlotReadiness readiness = new(2);
        TrackedAccelerationStructure first = new(AccelerationStructureType.TopLevel, true);
        TrackedAccelerationStructure second = new(AccelerationStructureType.TopLevel, true);

        Assert.False(readiness.CanTrace(0, first));
        readiness.MarkSubmitted(0, first);
        Assert.True(readiness.CanTrace(0, first));
        Assert.False(readiness.CanTrace(1, first));
        Assert.False(readiness.CanTrace(0, second));
        readiness.Invalidate(0);
        Assert.False(readiness.CanTrace(0, first));
    }

    [Fact]
    public void FrameSlotReadiness_DoesNotTraceUntilSubmittedBuildCompletes()
    {
        RayTracingFrameSlotReadiness readiness = new(1);
        TrackedAccelerationStructure structure = new(AccelerationStructureType.TopLevel);

        readiness.MarkSubmitted(0, structure);
        Assert.False(readiness.CanTrace(0, structure));
        structure.IsBuilt = true;
        Assert.True(readiness.CanTrace(0, structure));
    }

    private sealed class TrackedAccelerationStructure(AccelerationStructureType type, bool built = false) : IAccelerationStructure
    {
        public AccelerationStructureType Type { get; } = type;
        public ulong Size => 64;
        public ulong DeviceAddress => 1;
        public bool IsBuilt { get; set; } = built;
        public void Dispose() { }
    }
}
