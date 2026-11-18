using Engine.RHI;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Engine.Backends.Vulkan;

internal unsafe sealed class VulkanAccelerationStructure : IAccelerationStructure
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanBuffer _storage;
    private readonly object _lifetimeLock = new();
    private int _retainedCount;
    private bool _disposeRequested;
    private bool _destroyed;
    private bool _isBuilt;
    private PendingBuild? _pendingBuild;
    private IDisposable[] _bottomLevelLeases = [];

    public AccelerationStructureKHR Handle { get; }
    public AccelerationStructureType Type { get; }
    public ulong Size { get; }
    public ulong DeviceAddress { get; }
    public bool IsBuilt
    {
        get
        {
            lock (_lifetimeLock)
                return _isBuilt;
        }
    }

    internal AccelerationStructureBuildDescription? BuildDescription { get; private set; }

    internal bool BelongsTo(VulkanDevice device) => ReferenceEquals(_device, device);

    public VulkanAccelerationStructure(Vk vk, VulkanDevice device, VulkanMemoryManager memory,
        AccelerationStructureType type, ulong size)
    {
        if (!device.SupportsAccelerationStructures)
            throw new NotSupportedException("Vulkan acceleration structures are unavailable on this device.");
        if (size == 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Acceleration structure storage size must be non-zero.");
        _vk = vk;
        _device = device;
        Type = type;
        Size = size;
        _storage = new VulkanBuffer(vk, device, memory, new BufferDescription
        {
            Size = size,
            Usage = BufferUsage.AccelerationStructureStorage | BufferUsage.DeviceAddress,
            Memory = MemoryUsage.GpuOnly,
        }, $"{type} acceleration structure storage ({size}B)");

        try
        {
            AccelerationStructureCreateInfoKHR createInfo = new()
            {
                SType = StructureType.AccelerationStructureCreateInfoKhr,
                Buffer = _storage.Handle,
                Offset = 0,
                Size = size,
                Type = type == AccelerationStructureType.BottomLevel
                    ? AccelerationStructureTypeKHR.BottomLevelKhr
                    : AccelerationStructureTypeKHR.TopLevelKhr,
            };
            VulkanHelpers.Throw(device.AccelerationStructureExtension.CreateAccelerationStructure(device.Handle, &createInfo, null, out AccelerationStructureKHR accelerationStructure),
                "vkCreateAccelerationStructureKHR");
            Handle = accelerationStructure;

            AccelerationStructureDeviceAddressInfoKHR addressInfo = new()
            {
                SType = StructureType.AccelerationStructureDeviceAddressInfoKhr,
                AccelerationStructure = accelerationStructure,
            };
            DeviceAddress = device.AccelerationStructureExtension.GetAccelerationStructureDeviceAddress(device.Handle, &addressInfo);
            if (DeviceAddress == 0)
                throw new VulkanException(Result.ErrorFeatureNotPresent, "vkGetAccelerationStructureDeviceAddressKHR returned zero");
        }
        catch
        {
            if (Handle.Handle != 0)
                device.AccelerationStructureExtension.DestroyAccelerationStructure(device.Handle, Handle, null);
            _storage.Dispose();
            throw;
        }
    }

    internal IDisposable Retain()
    {
        lock (_lifetimeLock)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            _retainedCount++;
            return new VulkanResourceLease(ReleaseRetained);
        }
    }

    internal PendingBuild BeginBuild(object owner, AccelerationStructureBuildDescription description, bool update)
    {
        lock (_lifetimeLock)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            if (_pendingBuild is not null)
                throw new InvalidOperationException("Acceleration structure already has a pending or submitted build that has not retired.");
            if (update)
            {
                if (!_isBuilt || BuildDescription is null)
                    throw new InvalidOperationException("Acceleration structure update requires a completed initial build.");
                RayTracingValidation.ValidateUpdate(BuildDescription, description);
            }
            else if (_isBuilt)
                throw new InvalidOperationException("An already-built acceleration structure must be updated, not rebuilt.");

            List<IDisposable> leases = [];
            try
            {
                foreach (VulkanAccelerationStructure bottomLevel in description.Geometries
                    .OfType<InstanceGeometry>().SelectMany(geometry => geometry.BottomLevels)
                    .Cast<VulkanAccelerationStructure>().Distinct())
                    leases.Add(bottomLevel.RetainBuildReference());
                _pendingBuild = new PendingBuild(this, owner, description, leases.ToArray());
                return _pendingBuild;
            }
            catch
            {
                foreach (IDisposable lease in leases)
                    lease.Dispose();
                throw;
            }
        }
    }

    private IDisposable RetainBuildReference()
    {
        lock (_lifetimeLock)
        {
            if (!_isBuilt || _pendingBuild is not null)
                throw new InvalidOperationException("TLAS references require a completed bottom-level build with no pending update.");
            return Retain();
        }
    }

    internal void ValidateTrace(object owner)
    {
        lock (_lifetimeLock)
        {
            if (!_isBuilt)
                throw new InvalidOperationException("Ray tracing requires an acceleration structure with a completed initial build.");
            if (_pendingBuild is not null && !ReferenceEquals(_pendingBuild.Owner, owner))
                throw new InvalidOperationException("Ray tracing cannot use an acceleration structure with a build pending in another command buffer.");
        }
    }

    internal sealed class PendingBuild(
        VulkanAccelerationStructure structure,
        object owner,
        AccelerationStructureBuildDescription description,
        IDisposable[] bottomLevelLeases) : IDisposable
    {
        private IDisposable[] _leases = bottomLevelLeases;
        private bool _disposed;

        internal object Owner { get; } = owner;

        internal void Commit()
        {
            IDisposable[] previous;
            lock (structure._lifetimeLock)
            {
                if (_disposed || !ReferenceEquals(structure._pendingBuild, this))
                    throw new InvalidOperationException("Acceleration structure build reservation is no longer active.");
                previous = structure._bottomLevelLeases;
                structure._bottomLevelLeases = _leases;
                _leases = [];
                structure.BuildDescription = description;
                structure._isBuilt = true;
                structure._pendingBuild = null;
            }
            foreach (IDisposable lease in previous)
                lease.Dispose();
        }

        public void Dispose()
        {
            IDisposable[] leases;
            lock (structure._lifetimeLock)
            {
                if (_disposed)
                    return;
                _disposed = true;
                if (ReferenceEquals(structure._pendingBuild, this))
                    structure._pendingBuild = null;
                leases = _leases;
                _leases = [];
            }
            foreach (IDisposable lease in leases)
                lease.Dispose();
        }
    }

    private void ReleaseRetained()
    {
        lock (_lifetimeLock)
        {
            if (_retainedCount <= 0)
                throw new InvalidOperationException("Acceleration structure retention count underflow.");
            _retainedCount--;
            if (_disposeRequested && _retainedCount == 0)
                DestroyNative();
        }
    }

    public void Dispose()
    {
        lock (_lifetimeLock)
        {
            if (_disposeRequested)
                return;
            _disposeRequested = true;
            if (_retainedCount == 0)
                DestroyNative();
        }
    }

    private void DestroyNative()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _isBuilt = false;
        BuildDescription = null;
        _device.AccelerationStructureExtension.DestroyAccelerationStructure(_device.Handle, Handle, null);
        _storage.Dispose();
        foreach (IDisposable lease in _bottomLevelLeases)
            lease.Dispose();
        _bottomLevelLeases = [];
    }
}

internal sealed class VulkanResourceLease(Action release) : IDisposable
{
    private Action? _release = release;

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}

internal static unsafe class VulkanRayTracingOperations
{
    public static AccelerationStructureBuildSizes QueryBuildSizes(Vk vk, VulkanDevice device,
        AccelerationStructureBuildDescription description)
    {
        AccelerationStructureGeometryKHR[] geometries = CreateGeometries(description);
        uint[] primitiveCounts = description.Geometries.Select(geometry => geometry.PrimitiveCount).ToArray();
        AccelerationStructureBuildGeometryInfoKHR buildInfo = new()
        {
            SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
            Type = ToVulkanType(description.Type),
            Flags = ToVulkanBuildFlags(description.Flags),
            Mode = BuildAccelerationStructureModeKHR.BuildKhr,
            GeometryCount = (uint)geometries.Length,
        };
        AccelerationStructureBuildSizesInfoKHR sizes = new()
        {
            SType = StructureType.AccelerationStructureBuildSizesInfoKhr,
        };
        fixed (AccelerationStructureGeometryKHR* geometryPointer = geometries)
        fixed (uint* primitiveCountPointer = primitiveCounts)
        {
            buildInfo.PGeometries = geometryPointer;
            device.AccelerationStructureExtension.GetAccelerationStructureBuildSizes(
                device.Handle,
                AccelerationStructureBuildTypeKHR.DeviceKhr,
                &buildInfo,
                primitiveCountPointer,
                &sizes);
        }
        if (sizes.AccelerationStructureSize == 0)
            throw new VulkanException(Result.ErrorUnknown, "vkGetAccelerationStructureBuildSizesKHR returned zero storage size");
        return new AccelerationStructureBuildSizes(sizes.AccelerationStructureSize, sizes.BuildScratchSize, sizes.UpdateScratchSize);
    }

    public static AccelerationStructureGeometryKHR[] CreateGeometries(AccelerationStructureBuildDescription description)
    {
        AccelerationStructureGeometryKHR[] geometries = new AccelerationStructureGeometryKHR[description.Geometries.Count];
        for (int index = 0; index < geometries.Length; index++)
        {
            AccelerationStructureGeometry source = description.Geometries[index];
            AccelerationStructureGeometryKHR geometry = new()
            {
                SType = StructureType.AccelerationStructureGeometryKhr,
                Flags = ToVulkanGeometryFlags(source.Flags),
            };
            switch (source)
            {
                case TriangleGeometry triangles:
                    geometry.GeometryType = GeometryTypeKHR.TrianglesKhr;
                    geometry.Geometry.Triangles = new AccelerationStructureGeometryTrianglesDataKHR
                    {
                        SType = StructureType.AccelerationStructureGeometryTrianglesDataKhr,
                        VertexFormat = Format.R32G32B32Sfloat,
                        VertexData = new DeviceOrHostAddressConstKHR
                        {
                            DeviceAddress = GetAddress(triangles.VertexBuffer, triangles.VertexOffset),
                        },
                        VertexStride = triangles.VertexStride,
                        MaxVertex = triangles.VertexCount - 1,
                        IndexType = triangles.IndexBuffer is null
                            ? Silk.NET.Vulkan.IndexType.NoneKhr
                            : triangles.IndexType == Engine.RHI.IndexType.UInt16
                                ? Silk.NET.Vulkan.IndexType.Uint16
                                : Silk.NET.Vulkan.IndexType.Uint32,
                        IndexData = new DeviceOrHostAddressConstKHR
                        {
                            DeviceAddress = triangles.IndexBuffer is null
                                ? 0
                                : GetAddress(triangles.IndexBuffer, triangles.IndexOffset),
                        },
                    };
                    break;
                case AabbGeometry aabbs:
                    geometry.GeometryType = GeometryTypeKHR.AabbsKhr;
                    geometry.Geometry.Aabbs = new AccelerationStructureGeometryAabbsDataKHR
                    {
                        SType = StructureType.AccelerationStructureGeometryAabbsDataKhr,
                        Data = new DeviceOrHostAddressConstKHR { DeviceAddress = GetAddress(aabbs.Buffer, aabbs.Offset) },
                        Stride = aabbs.Stride,
                    };
                    break;
                case InstanceGeometry instances:
                    geometry.GeometryType = GeometryTypeKHR.InstancesKhr;
                    geometry.Geometry.Instances = new AccelerationStructureGeometryInstancesDataKHR
                    {
                        SType = StructureType.AccelerationStructureGeometryInstancesDataKhr,
                        ArrayOfPointers = false,
                        Data = new DeviceOrHostAddressConstKHR { DeviceAddress = GetAddress(instances.Buffer, instances.Offset) },
                    };
                    break;
                default:
                    throw new ArgumentException($"Unsupported acceleration structure geometry '{source.GetType().Name}'.", nameof(description));
            }
            geometries[index] = geometry;
        }
        return geometries;
    }

    public static AccelerationStructureTypeKHR ToVulkanType(AccelerationStructureType type) => type switch
    {
        AccelerationStructureType.BottomLevel => AccelerationStructureTypeKHR.BottomLevelKhr,
        AccelerationStructureType.TopLevel => AccelerationStructureTypeKHR.TopLevelKhr,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static BuildAccelerationStructureFlagsKHR ToVulkanBuildFlags(AccelerationStructureBuildFlags flags)
    {
        BuildAccelerationStructureFlagsKHR result = 0;
        if (flags.HasFlag(AccelerationStructureBuildFlags.AllowUpdate))
            result |= BuildAccelerationStructureFlagsKHR.AllowUpdateBitKhr;
        if (flags.HasFlag(AccelerationStructureBuildFlags.PreferFastBuild))
            result |= BuildAccelerationStructureFlagsKHR.PreferFastBuildBitKhr;
        if (flags.HasFlag(AccelerationStructureBuildFlags.PreferFastTrace))
            result |= BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr;
        return result;
    }

    private static GeometryFlagsKHR ToVulkanGeometryFlags(AccelerationStructureGeometryFlags flags)
    {
        GeometryFlagsKHR result = 0;
        if (flags.HasFlag(AccelerationStructureGeometryFlags.Opaque))
            result |= GeometryFlagsKHR.OpaqueBitKhr;
        if (flags.HasFlag(AccelerationStructureGeometryFlags.NoDuplicateAnyHit))
            result |= GeometryFlagsKHR.NoDuplicateAnyHitInvocationBitKhr;
        return result;
    }

    private static ulong GetAddress(IBuffer buffer, ulong offset)
    {
        if (buffer is not VulkanBuffer vulkanBuffer)
            throw new ArgumentException("Acceleration structure geometry buffer does not belong to Vulkan.", nameof(buffer));
        return checked(vulkanBuffer.DeviceAddress + offset);
    }
}
