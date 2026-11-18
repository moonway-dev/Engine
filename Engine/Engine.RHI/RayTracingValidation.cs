namespace Engine.RHI;

public static class RayTracingValidation
{
    public static void ValidateBuild(AccelerationStructureBuildDescription description, RayTracingLimits limits)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(description.Geometries);
        if (description.Geometries.Count == 0 || !Enum.IsDefined(description.Type)
            || (ulong)description.Geometries.Count > limits.MaxGeometryCount)
            throw new ArgumentException("Invalid acceleration structure geometry count or type.", nameof(description));
        if ((description.Flags & ~(AccelerationStructureBuildFlags.AllowUpdate | AccelerationStructureBuildFlags.PreferFastBuild | AccelerationStructureBuildFlags.PreferFastTrace)) != 0
            || description.Flags.HasFlag(AccelerationStructureBuildFlags.PreferFastBuild) && description.Flags.HasFlag(AccelerationStructureBuildFlags.PreferFastTrace))
            throw new ArgumentException("Invalid acceleration structure build flags.", nameof(description));
        if (description.Type == AccelerationStructureType.TopLevel
            && (description.Geometries.Count != 1 || description.Geometries[0] is not InstanceGeometry))
            throw new ArgumentException("A TLAS requires exactly one instance geometry.", nameof(description));
        ulong totalPrimitives = 0;
        foreach (AccelerationStructureGeometry geometry in description.Geometries)
        {
            ArgumentNullException.ThrowIfNull(geometry);
            if ((geometry.Flags & ~(AccelerationStructureGeometryFlags.Opaque | AccelerationStructureGeometryFlags.NoDuplicateAnyHit)) != 0)
                throw new ArgumentException("Invalid geometry flags.", nameof(description));
            totalPrimitives = checked(totalPrimitives + geometry.PrimitiveCount);
            switch (geometry)
            {
                case TriangleGeometry triangles when description.Type == AccelerationStructureType.BottomLevel:
                    if (triangles.VertexFormat != VertexFormat.Float3 || triangles.VertexStride < 12 || triangles.VertexStride % 4 != 0
                        || triangles.VertexCount < 3 || triangles.VertexOffset % 4 != 0)
                        throw new ArgumentException("Triangle vertices require Float3 positions, aligned stride and at least three vertices.");
                    ValidateInput(triangles.VertexBuffer, triangles.VertexOffset,
                        checked((ulong)(triangles.VertexCount - 1) * triangles.VertexStride + 12));
                    if (triangles.IndexBuffer is not null)
                    {
                        if (!Enum.IsDefined(triangles.IndexType) || triangles.IndexCount < 3 || triangles.IndexCount % 3 != 0)
                            throw new ArgumentException("Triangle index count must be a positive multiple of three.");
                        uint indexSize = triangles.IndexType == IndexType.UInt32 ? 4u : 2u;
                        if (triangles.IndexOffset % indexSize != 0)
                            throw new ArgumentException("Index offset is not aligned.");
                        ValidateInput(triangles.IndexBuffer, triangles.IndexOffset, checked((ulong)triangles.IndexCount * indexSize));
                    }
                    else if (triangles.VertexCount % 3 != 0)
                        throw new ArgumentException("Nonindexed triangle vertex count must be a multiple of three.");
                    break;
                case AabbGeometry aabb when description.Type == AccelerationStructureType.BottomLevel:
                    if (aabb.Count == 0 || aabb.Stride < 24 || aabb.Stride % 8 != 0 || aabb.Offset % 8 != 0)
                        throw new ArgumentException("AABBs require aligned float3 min/max pairs.");
                    ValidateInput(aabb.Buffer, aabb.Offset, checked((ulong)(aabb.Count - 1) * aabb.Stride + 24));
                    break;
                case InstanceGeometry instances when description.Type == AccelerationStructureType.TopLevel:
                    if (instances.Offset % 16 != 0 || instances.Count > limits.MaxInstanceCount)
                        throw new ArgumentException("Invalid instance offset or count.");
                    ValidateInput(instances.Buffer, instances.Offset, checked((ulong)instances.Count * 64));
                    foreach (IAccelerationStructure bottomLevel in instances.BottomLevels)
                    {
                        if (bottomLevel.Type != AccelerationStructureType.BottomLevel || bottomLevel.DeviceAddress == 0)
                            throw new ArgumentException("TLAS references must be bottom-level acceleration structures.");
                    }
                    break;
                default:
                    throw new ArgumentException("Geometry does not match acceleration structure type.");
            }
        }
        if (description.Type == AccelerationStructureType.BottomLevel && totalPrimitives > limits.MaxPrimitiveCount)
            throw new ArgumentException("Acceleration structure primitive limit exceeded.");
    }

    public static void ValidateUpdate(AccelerationStructureBuildDescription previous, AccelerationStructureBuildDescription current)
    {
        if (!previous.Flags.HasFlag(AccelerationStructureBuildFlags.AllowUpdate) || previous.Type != current.Type
            || previous.Flags != current.Flags || previous.Geometries.Count != current.Geometries.Count)
            throw new ArgumentException("Update requires an initially updateable structure with unchanged type, flags and geometry count.");
        for (int index = 0; index < previous.Geometries.Count; index++)
        {
            AccelerationStructureGeometry before = previous.Geometries[index];
            AccelerationStructureGeometry after = current.Geometries[index];
            if (before.GetType() != after.GetType() || before.Flags != after.Flags || before.PrimitiveCount != after.PrimitiveCount)
                throw new ArgumentException("Update cannot change geometry type, flags or primitive count.");
            if (before is TriangleGeometry a && after is TriangleGeometry b
                && (a.VertexCount != b.VertexCount || a.VertexFormat != b.VertexFormat || a.IndexType != b.IndexType
                    || (a.IndexBuffer is null) != (b.IndexBuffer is null) || a.IndexCount != b.IndexCount
                    || !ReferenceEquals(a.IndexBuffer, b.IndexBuffer) || a.IndexOffset != b.IndexOffset))
                throw new ArgumentException("Update cannot change triangle topology or vertex/index formats.");
        }
    }

    public static void ValidateGroups(RayTracingPipelineDescription description, IReadOnlyList<ShaderStage> stages, RayTracingLimits limits)
    {
        if (description.Groups.Count == 0 || description.MaxRecursionDepth == 0 || description.MaxRecursionDepth > limits.MaxRayRecursionDepth)
            throw new ArgumentException("Invalid RT shader group count or recursion depth.");
        bool hasRayGeneration = false;
        foreach (RayTracingShaderGroup group in description.Groups)
        {
            if (group.Type == RayTracingShaderGroupType.General)
            {
                if (group.GeneralShader < 0 || group.GeneralShader >= stages.Count || group.ClosestHitShader != -1 || group.AnyHitShader != -1 || group.IntersectionShader != -1)
                    throw new ArgumentException("Invalid general shader group.");
                ShaderStage stage = stages[group.GeneralShader];
                if (stage is not (ShaderStage.RayGeneration or ShaderStage.Miss or ShaderStage.Callable))
                    throw new ArgumentException("General groups require raygen, miss or callable entries.");
                hasRayGeneration |= stage == ShaderStage.RayGeneration;
            }
            else if (group.Type is RayTracingShaderGroupType.TrianglesHit or RayTracingShaderGroupType.ProceduralHit)
            {
                if (group.GeneralShader != -1 || group.Type == RayTracingShaderGroupType.TrianglesHit && group.IntersectionShader != -1
                    || group.Type == RayTracingShaderGroupType.ProceduralHit && group.IntersectionShader < 0)
                    throw new ArgumentException("Invalid hit group.");
                ValidateStage(group.ClosestHitShader, ShaderStage.ClosestHit, stages);
                ValidateStage(group.AnyHitShader, ShaderStage.AnyHit, stages);
                ValidateStage(group.IntersectionShader, ShaderStage.Intersection, stages);
            }
            else
                throw new ArgumentException("Unknown shader group type.");
        }
        if (!hasRayGeneration)
            throw new ArgumentException("RT pipeline requires a ray-generation group.");
    }

    private static void ValidateStage(int index, ShaderStage expected, IReadOnlyList<ShaderStage> stages)
    {
        if (index < -1 || index >= stages.Count || index >= 0 && stages[index] != expected)
            throw new ArgumentException($"Shader group entry must be {expected} or unused.");
    }

    private static void ValidateInput(IBuffer buffer, ulong offset, ulong length)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (!buffer.Usage.HasFlag(BufferUsage.AccelerationStructureBuildInput | BufferUsage.DeviceAddress)
            || buffer.DeviceAddress == 0 || offset > buffer.Size || length > buffer.Size - offset)
            throw new ArgumentException("AS input requires a device-address build-input buffer and valid range.");
    }
}
