using Engine.Core;
using Engine.RHI;
using Silk.NET.Core;
using Silk.NET.Vulkan;

using VkDescriptorSet = Silk.NET.Vulkan.DescriptorSet;
using VkDescriptorSetLayout = Silk.NET.Vulkan.DescriptorSetLayout;
using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkDescriptorType = Silk.NET.Vulkan.DescriptorType;
using VkImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace Engine.Backends.Vulkan;

internal unsafe sealed class VulkanDescriptorAllocator : IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly ILogger _log;
    private readonly List<DescriptorPoolEntry> _pools = [];
    private readonly Dictionary<ulong, DescriptorPoolEntry> _owners = [];

    private const uint MaxSetsPerPool = 1024;
    private const uint UniformPerPool = 1024;
    private const uint StoragePerPool = 512;
    private const uint SampledImagePerPool = 4096;
    private const uint SamplerPerPool = 256;
    private const uint CombinedPerPool = 512;
    private const uint StorageImagePerPool = 256;
    private const uint AccelerationStructurePerPool = 128;

    public VulkanDescriptorAllocator(Vk vk, VulkanDevice device, ILogger log)
    {
        _vk = vk;
        _device = device;
        _log = log;
    }

    public VkDescriptorSet Allocate(VkDescriptorSetLayout layout, string debugName)
    {
        foreach (DescriptorPoolEntry pool in _pools)
        {
            if (pool.TryAllocate(layout, out VkDescriptorSet descriptorSet))
            {
                _owners[descriptorSet.Handle] = pool;
                return descriptorSet;
            }
        }
        DescriptorPoolEntry newPool = CreatePool();
        _pools.Add(newPool);
        if (!newPool.TryAllocate(layout, out VkDescriptorSet set))
            throw new VulkanException(Result.ErrorOutOfPoolMemory, $"Descriptor set allocation '{debugName}'");
        _owners[set.Handle] = newPool;
        return set;
    }

    public void Free(VkDescriptorSet descriptorSet)
    {
        if (!_owners.Remove(descriptorSet.Handle, out DescriptorPoolEntry? pool))
            return;
        pool.Free(descriptorSet);
    }

    private DescriptorPoolEntry CreatePool()
    {
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[]
        {
            new() { Type = VkDescriptorType.UniformBuffer, DescriptorCount = UniformPerPool },
            new() { Type = VkDescriptorType.StorageBuffer, DescriptorCount = StoragePerPool },
            new() { Type = VkDescriptorType.SampledImage, DescriptorCount = SampledImagePerPool },
            new() { Type = VkDescriptorType.Sampler, DescriptorCount = SamplerPerPool },
            new() { Type = VkDescriptorType.CombinedImageSampler, DescriptorCount = CombinedPerPool },
            new() { Type = VkDescriptorType.StorageImage, DescriptorCount = StorageImagePerPool },
            new() { Type = VkDescriptorType.AccelerationStructureKhr, DescriptorCount = AccelerationStructurePerPool },
        };
        DescriptorPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = MaxSetsPerPool,
            PoolSizeCount = 7,
            PPoolSizes = sizes,
        };
        VulkanHelpers.Throw(
            _vk.CreateDescriptorPool(_device.Handle, &poolInfo, null, out DescriptorPool pool),
            "vkCreateDescriptorPool");
        return new DescriptorPoolEntry(_vk, _device, pool);
    }

    public void Dispose()
    {
        _owners.Clear();
        foreach (DescriptorPoolEntry pool in _pools)
            pool.Dispose();
        _pools.Clear();
    }

    internal sealed class DescriptorPoolEntry : IDisposable
    {
        private readonly Vk _vk;
        private readonly VulkanDevice _device;
        private readonly DescriptorPool _pool;
        private uint _allocatedSets;

        public DescriptorPoolEntry(Vk vk, VulkanDevice device, DescriptorPool pool)
        {
            _vk = vk;
            _device = device;
            _pool = pool;
        }

        public bool TryAllocate(VkDescriptorSetLayout layout, out VkDescriptorSet descriptorSet)
        {
            if (_allocatedSets >= MaxSetsPerPool)
            {
                descriptorSet = default;
                return false;
            }
            DescriptorSetAllocateInfo allocateInfo = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _pool,
                DescriptorSetCount = 1,
                PSetLayouts = &layout,
            };
            Result result = _vk.AllocateDescriptorSets(_device.Handle, &allocateInfo, out VkDescriptorSet set);
            if (result != Result.Success)
            {
                descriptorSet = default;
                return false;
            }
            _allocatedSets++;
            descriptorSet = set;
            return true;
        }

        public void Free(VkDescriptorSet descriptorSet)
        {
            VulkanHelpers.Throw(
                _vk.FreeDescriptorSets(_device.Handle, _pool, 1, &descriptorSet),
                "vkFreeDescriptorSets");
            if (_allocatedSets > 0)
                _allocatedSets--;
        }

        public void Dispose()
        {
            _vk.DestroyDescriptorPool(_device.Handle, _pool, null);
        }
    }
}

internal unsafe sealed class VulkanDescriptorSet : IDescriptorSet
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanDescriptorAllocator _allocator;
    private readonly VulkanShaderProgram _program;
    private readonly object _lifetimeLock = new();
    private readonly Dictionary<uint, (VulkanAccelerationStructure Structure, IDisposable Lease)> _accelerationStructures = [];
    private readonly Dictionary<uint, IDisposable[]> _resourceLeases = [];
    private int _retainedCount;
    private bool _disposeRequested;
    private bool _disposed;

    public VkDescriptorSet Handle { get; }

    public uint SetIndex { get; }

    public PipelineLayout ProgramLayout { get; }

    IShaderProgram IDescriptorSet.Program => _program;

    public VulkanDescriptorSet(
        Vk vk,
        VulkanDevice device,
        VulkanDescriptorAllocator allocator,
        VulkanShaderProgram program,
        PipelineLayout programLayout,
        VkDescriptorSet handle,
        uint setIndex)
    {
        _vk = vk;
        _device = device;
        _allocator = allocator;
        _program = program;
        ProgramLayout = programLayout;
        Handle = handle;
        SetIndex = setIndex;
    }

    public void WriteUniformBuffer(uint binding, IBuffer buffer, ulong offset = 0, ulong range = 0)
    {
        VulkanBuffer vkBuffer = ResolveBuffer(buffer);
        WriteBufferDescriptor(binding, vkBuffer, offset, range, VkDescriptorType.UniformBuffer);
    }

    public void WriteStorageBuffer(uint binding, IBuffer buffer, ulong offset = 0, ulong range = 0)
    {
        VulkanBuffer vkBuffer = ResolveBuffer(buffer);
        WriteBufferDescriptor(binding, vkBuffer, offset, range, VkDescriptorType.StorageBuffer);
    }

    private void WriteBufferDescriptor(uint binding, VulkanBuffer vkBuffer, ulong offset, ulong range, VkDescriptorType type)
    {
        lock (_lifetimeLock)
        {
            EnsureWritable();
            IDisposable lease = vkBuffer.Retain();
            try
            {
                DescriptorBufferInfo bufferInfo = new()
                {
                    Buffer = vkBuffer.Handle,
                    Offset = offset,
                    Range = range == 0 ? VK_WHOLE_SIZE : range,
                };
                WriteDescriptorSet write = new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = Handle,
                    DstBinding = binding,
                    DstArrayElement = 0,
                    DescriptorCount = 1,
                    DescriptorType = type,
                    PBufferInfo = &bufferInfo,
                };
                _vk.UpdateDescriptorSets(_device.Handle, 1, &write, 0, null);
                ReplaceResourceLeases(binding, [lease]);
                lease = null!;
            }
            finally
            {
                lease?.Dispose();
            }
        }
    }

    public void WriteTexture(uint binding, ITexture texture, ISampler? sampler = null)
    {
        IVulkanTexture vkTexture = texture as IVulkanTexture
            ?? throw new ArgumentException("Texture does not belong to the Vulkan backend.", nameof(texture));
        if (!vkTexture.BelongsTo(_device))
            throw new ArgumentException("Texture does not belong to this Vulkan device.", nameof(texture));
        WriteTexture(binding, vkTexture, vkTexture.View, texture.Format, sampler);
    }

    public void WriteTexture(uint binding, ITextureView view, ISampler? sampler = null)
    {
        IVulkanTexture vkTexture = view as IVulkanTexture
            ?? throw new ArgumentException("Texture view does not belong to the Vulkan backend.", nameof(view));
        if (!vkTexture.BelongsTo(_device))
            throw new ArgumentException("Texture view does not belong to this Vulkan device.", nameof(view));
        WriteTexture(binding, vkTexture, vkTexture.View, view.Texture.Format, sampler);
    }

    private void WriteTexture(uint binding, IVulkanTexture texture, ImageView imageView, TextureFormat format, ISampler? sampler)
    {
        VulkanSampler? vkSampler = null;
        if (sampler is not null)
        {
            vkSampler = sampler as VulkanSampler
                ?? throw new ArgumentException("Sampler does not belong to the Vulkan backend.", nameof(sampler));
            if (!vkSampler.BelongsTo(_device))
                throw new ArgumentException("Sampler does not belong to this Vulkan device.", nameof(sampler));
        }
        lock (_lifetimeLock)
        {
            EnsureWritable();
            List<IDisposable> leases = [texture.Retain()];
            try
            {
                if (vkSampler is not null)
                    leases.Add(vkSampler.Retain());
                DescriptorImageInfo imageInfo = new()
                {
                    Sampler = vkSampler is not null ? vkSampler.Handle : default,
                    ImageView = imageView,
                    ImageLayout = format.IsDepth()
                        ? VkImageLayout.DepthStencilReadOnlyOptimal
                        : VkImageLayout.ShaderReadOnlyOptimal,
                };
                VkDescriptorType type = vkSampler is not null ? VkDescriptorType.CombinedImageSampler : VkDescriptorType.SampledImage;
                WriteDescriptorSet write = new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = Handle,
                    DstBinding = binding,
                    DstArrayElement = 0,
                    DescriptorCount = 1,
                    DescriptorType = type,
                    PImageInfo = &imageInfo,
                };
                _vk.UpdateDescriptorSets(_device.Handle, 1, &write, 0, null);
                ReplaceResourceLeases(binding, [.. leases]);
                leases.Clear();
            }
            finally
            {
                foreach (IDisposable lease in leases)
                    lease.Dispose();
            }
        }
    }

    public void WriteStorageImage(uint binding, ITexture texture)
    {
        IVulkanTexture vkTexture = texture as IVulkanTexture
            ?? throw new ArgumentException("Texture does not belong to the Vulkan backend.", nameof(texture));
        if (!vkTexture.BelongsTo(_device))
            throw new ArgumentException("Texture does not belong to this Vulkan device.", nameof(texture));
        WriteStorageImageDescriptor(binding, vkTexture);
    }

    public void WriteStorageImage(uint binding, ITextureView view)
    {
        IVulkanTexture vkTexture = view as IVulkanTexture
            ?? throw new ArgumentException("Texture view does not belong to the Vulkan backend.", nameof(view));
        if (!vkTexture.BelongsTo(_device))
            throw new ArgumentException("Texture view does not belong to this Vulkan device.", nameof(view));
        WriteStorageImageDescriptor(binding, vkTexture);
    }

    private void WriteStorageImageDescriptor(uint binding, IVulkanTexture texture)
    {
        lock (_lifetimeLock)
        {
            EnsureWritable();
            IDisposable lease = texture.Retain();
            try
            {
                DescriptorImageInfo imageInfo = new()
                {
                    ImageView = texture.View,
                    ImageLayout = VkImageLayout.General,
                };
                WriteDescriptorSet write = new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = Handle,
                    DstBinding = binding,
                    DstArrayElement = 0,
                    DescriptorCount = 1,
                    DescriptorType = VkDescriptorType.StorageImage,
                    PImageInfo = &imageInfo,
                };
                _vk.UpdateDescriptorSets(_device.Handle, 1, &write, 0, null);
                ReplaceResourceLeases(binding, [lease]);
                lease = null!;
            }
            finally
            {
                lease?.Dispose();
            }
        }
    }

    public void WriteAccelerationStructure(uint binding, IAccelerationStructure accelerationStructure)
    {
        if (!_device.SupportsAccelerationStructures)
            throw new NotSupportedException("Vulkan acceleration structures are unavailable on this device.");
        if (accelerationStructure is not VulkanAccelerationStructure vkStructure || !vkStructure.BelongsTo(_device))
            throw new ArgumentException("Acceleration structure does not belong to this Vulkan device.", nameof(accelerationStructure));
        lock (_lifetimeLock)
        {
            EnsureWritable();
            if (!vkStructure.IsBuilt)
                throw new InvalidOperationException("Acceleration-structure descriptor cannot reference an unbuilt structure.");

            IDisposable lease = vkStructure.Retain();
            try
            {
                AccelerationStructureKHR handle = vkStructure.Handle;
                WriteDescriptorSetAccelerationStructureKHR structureInfo = new()
                {
                    SType = StructureType.WriteDescriptorSetAccelerationStructureKhr,
                    AccelerationStructureCount = 1,
                    PAccelerationStructures = &handle,
                };
                WriteDescriptorSet write = new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    PNext = &structureInfo,
                    DstSet = Handle,
                    DstBinding = binding,
                    DstArrayElement = 0,
                    DescriptorCount = 1,
                    DescriptorType = VkDescriptorType.AccelerationStructureKhr,
                };
                _vk.UpdateDescriptorSets(_device.Handle, 1, &write, 0, null);
                if (_accelerationStructures.Remove(binding, out var previous))
                    previous.Lease.Dispose();
                _accelerationStructures[binding] = (vkStructure, lease);
                lease = null!;
                ClearResourceLeases(binding);
            }
            catch
            {
                lease?.Dispose();
                throw;
            }
        }
    }

    public void WriteTextureArray(uint binding, IReadOnlyList<ITexture> textures)
    {
        ArgumentNullException.ThrowIfNull(textures);
        if (textures.Count == 0)
            throw new ArgumentException("Texture array must contain at least one texture.", nameof(textures));
        IVulkanTexture[] vkTextures = new IVulkanTexture[textures.Count];
        for (int index = 0; index < textures.Count; index++)
        {
            vkTextures[index] = textures[index] as IVulkanTexture
                ?? throw new ArgumentException($"Texture at index {index} does not belong to the Vulkan backend.", nameof(textures));
            if (!vkTextures[index].BelongsTo(_device))
                throw new ArgumentException($"Texture at index {index} does not belong to this Vulkan device.", nameof(textures));
        }
        lock (_lifetimeLock)
        {
            EnsureWritable();
            List<IDisposable> leases = [];
            try
            {
                DescriptorImageInfo[] imageInfos = new DescriptorImageInfo[textures.Count];
                for (int index = 0; index < vkTextures.Length; index++)
                {
                    leases.Add(vkTextures[index].Retain());
                    imageInfos[index] = new DescriptorImageInfo
                    {
                        ImageView = vkTextures[index].View,
                        ImageLayout = vkTextures[index].Format.IsDepth()
                            ? VkImageLayout.DepthStencilReadOnlyOptimal
                            : VkImageLayout.ShaderReadOnlyOptimal,
                    };
                }
                fixed (DescriptorImageInfo* imageInfoPointer = imageInfos)
                {
                    WriteDescriptorSet write = new()
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = Handle,
                        DstBinding = binding,
                        DstArrayElement = 0,
                        DescriptorCount = (uint)imageInfos.Length,
                        DescriptorType = VkDescriptorType.SampledImage,
                        PImageInfo = imageInfoPointer,
                    };
                    _vk.UpdateDescriptorSets(_device.Handle, 1, &write, 0, null);
                }
                ReplaceResourceLeases(binding, [.. leases]);
                leases.Clear();
            }
            finally
            {
                foreach (IDisposable lease in leases)
                    lease.Dispose();
            }
        }
    }

    private void ReplaceResourceLeases(uint binding, IDisposable[] leases)
    {
        ClearResourceLeases(binding);
        _resourceLeases[binding] = leases;
        if (_accelerationStructures.Remove(binding, out var previousStructure))
            previousStructure.Lease.Dispose();
    }

    private void ClearResourceLeases(uint binding)
    {
        if (!_resourceLeases.Remove(binding, out IDisposable[]? previous))
            return;
        foreach (IDisposable lease in previous)
            lease.Dispose();
    }

    internal bool BelongsTo(VulkanDevice device) => ReferenceEquals(_device, device);

    internal IReadOnlyCollection<VulkanAccelerationStructure> AccelerationStructures
    {
        get
        {
            lock (_lifetimeLock)
                return _accelerationStructures.Values.Select(entry => entry.Structure).Distinct().ToArray();
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

    private void ReleaseRetained()
    {
        lock (_lifetimeLock)
        {
            if (_retainedCount <= 0)
                throw new InvalidOperationException("Descriptor-set retention count underflow.");
            _retainedCount--;
            if (_disposeRequested && _retainedCount == 0)
                FreeNative();
        }
    }

    private void FreeNative()
    {
        if (_disposed)
            return;
        _disposed = true;
        _allocator.Free(Handle);
        foreach (var entry in _accelerationStructures.Values)
            entry.Lease.Dispose();
        _accelerationStructures.Clear();
        foreach (IDisposable[] leases in _resourceLeases.Values)
        foreach (IDisposable lease in leases)
            lease.Dispose();
        _resourceLeases.Clear();
    }

    public void WriteSampler(uint binding, ISampler sampler)
    {
        if (sampler is not VulkanSampler vkSampler || !vkSampler.BelongsTo(_device))
            throw new ArgumentException("Sampler does not belong to this Vulkan device.", nameof(sampler));
        lock (_lifetimeLock)
        {
            EnsureWritable();
            IDisposable lease = vkSampler.Retain();
            try
            {
                DescriptorImageInfo imageInfo = new() { Sampler = vkSampler.Handle };
                WriteDescriptorSet write = new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = Handle,
                    DstBinding = binding,
                    DstArrayElement = 0,
                    DescriptorCount = 1,
                    DescriptorType = VkDescriptorType.Sampler,
                    PImageInfo = &imageInfo,
                };
                _vk.UpdateDescriptorSets(_device.Handle, 1, &write, 0, null);
                ReplaceResourceLeases(binding, [lease]);
                lease = null!;
            }
            finally
            {
                lease?.Dispose();
            }
        }
    }

    private void EnsureWritable()
    {
        ObjectDisposedException.ThrowIf(_disposeRequested, this);
        if (_retainedCount > 0)
            throw new InvalidOperationException("A descriptor set cannot be updated while retained by GPU work.");
    }

    private const ulong VK_WHOLE_SIZE = ulong.MaxValue;

    private VulkanBuffer ResolveBuffer(IBuffer buffer)
    {
        if (buffer is not VulkanBuffer vkBuffer || !vkBuffer.BelongsTo(_device))
            throw new ArgumentException("Buffer does not belong to this Vulkan device.", nameof(buffer));
        return vkBuffer;
    }

    public void Dispose()
    {
        lock (_lifetimeLock)
        {
            if (_disposeRequested)
                return;
            _disposeRequested = true;
            if (_retainedCount == 0)
                FreeNative();
        }
    }
}
