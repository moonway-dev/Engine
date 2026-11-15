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
    private const uint SampledImagePerPool = 1024;
    private const uint SamplerPerPool = 256;
    private const uint CombinedPerPool = 512;
    private const uint StorageImagePerPool = 256;

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
        };
        DescriptorPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = MaxSetsPerPool,
            PoolSizeCount = 6,
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
        VkBuffer vkBuffer = ResolveBuffer(buffer);
        WriteBufferDescriptor(binding, vkBuffer, offset, range, VkDescriptorType.UniformBuffer);
    }

    public void WriteStorageBuffer(uint binding, IBuffer buffer, ulong offset = 0, ulong range = 0)
    {
        VkBuffer vkBuffer = ResolveBuffer(buffer);
        WriteBufferDescriptor(binding, vkBuffer, offset, range, VkDescriptorType.StorageBuffer);
    }

    private void WriteBufferDescriptor(uint binding, VkBuffer vkBuffer, ulong offset, ulong range, VkDescriptorType type)
    {
        DescriptorBufferInfo bufferInfo = new()
        {
            Buffer = vkBuffer,
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
    }

    public void WriteTexture(uint binding, ITexture texture, ISampler? sampler = null)
    {
        IVulkanTexture vkTexture = texture as IVulkanTexture
            ?? throw new ArgumentException("Texture does not belong to the Vulkan backend.", nameof(texture));
        DescriptorImageInfo imageInfo = new()
        {
            Sampler = sampler is VulkanSampler vkSampler ? vkSampler.Handle : default,
            ImageView = vkTexture.View,
            ImageLayout = texture.Format.IsDepth()
                ? VkImageLayout.DepthStencilReadOnlyOptimal
                : VkImageLayout.ShaderReadOnlyOptimal,
        };
        VkDescriptorType type = sampler is not null ? VkDescriptorType.CombinedImageSampler : VkDescriptorType.SampledImage;
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
    }

    public void WriteStorageImage(uint binding, ITexture texture)
    {
        IVulkanTexture vkTexture = texture as IVulkanTexture
            ?? throw new ArgumentException("Texture does not belong to the Vulkan backend.", nameof(texture));
        DescriptorImageInfo imageInfo = new()
        {
            ImageView = vkTexture.View,
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
    }

    public void WriteSampler(uint binding, ISampler sampler)
    {
        if (sampler is not VulkanSampler vkSampler)
            throw new ArgumentException("Sampler does not belong to the Vulkan backend.", nameof(sampler));
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
    }

    private const ulong VK_WHOLE_SIZE = ulong.MaxValue;

    private VkBuffer ResolveBuffer(IBuffer buffer) => buffer is VulkanBuffer vkBuffer
        ? vkBuffer.Handle
        : throw new ArgumentException("Buffer does not belong to the Vulkan backend.", nameof(buffer));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _allocator.Free(Handle);
    }
}
