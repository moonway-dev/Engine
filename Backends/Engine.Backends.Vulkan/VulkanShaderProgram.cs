using Engine.Core;
using Engine.RHI;
using Silk.NET.Vulkan;

using VkDescriptorSet = Silk.NET.Vulkan.DescriptorSet;
using VkPushConstantRange = Silk.NET.Vulkan.PushConstantRange;
using VkDescriptorType = Silk.NET.Vulkan.DescriptorType;
using VkDescriptorSetLayout = Silk.NET.Vulkan.DescriptorSetLayout;

namespace Engine.Backends.Vulkan;

internal unsafe sealed class VulkanShaderProgram : IShaderProgram
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanDescriptorAllocator _allocator;
    private readonly Dictionary<uint, VkDescriptorSetLayout> _setLayouts = [];
    private readonly HashSet<uint> _declaredSetIndices = [];
    private readonly Dictionary<ShaderStage, ShaderModule> _modules = [];
    private readonly Dictionary<ShaderStage, string> _entryPoints = [];
    private readonly List<VkDescriptorSetLayout> _ownedSetLayouts = [];
    private readonly ShaderStage[] _stages;
    private readonly object _lifetimeLock = new();
    private int _retainedCount;
    private bool _disposeRequested;
    private bool _destroyed;

    public string Name { get; }

    public PipelineReflection Reflection { get; }

    internal PipelineLayout Layout { get; }

    internal IReadOnlyDictionary<uint, VkDescriptorSetLayout> SetLayouts => _setLayouts;

    internal IReadOnlyCollection<uint> DeclaredSetIndices => _declaredSetIndices;

    internal IReadOnlyList<ShaderStage> Stages => _stages;

    internal bool BelongsTo(VulkanDevice device) => ReferenceEquals(_device, device);

    internal bool IsDisposed
    {
        get
        {
            lock (_lifetimeLock)
                return _disposeRequested;
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
                throw new InvalidOperationException("Shader program retention count underflow.");
            _retainedCount--;
            if (_disposeRequested && _retainedCount == 0)
                DestroyNative();
        }
    }

    public VulkanShaderProgram(Vk vk, VulkanDevice device, VulkanDescriptorAllocator allocator, in ShaderProgramDescription description)
    {
        _vk = vk;
        _device = device;
        _allocator = allocator;
        Name = description.Name;
        Reflection = description.Reflection;
        ArgumentNullException.ThrowIfNull(description.Stages);
        _stages = description.Stages.Select(stage => stage.Stage).ToArray();
        if (_stages.Distinct().Count() != _stages.Length)
            throw new ArgumentException("Shader program cannot contain duplicate shader stages.", nameof(description));

        try
        {
            foreach (ShaderStageCode stage in description.Stages)
            {
                if (stage.SpirV.Length == 0 || stage.SpirV.Length % sizeof(uint) != 0)
                    throw new ArgumentException($"Shader stage {stage.Stage} has invalid SPIR-V bytecode.", nameof(description));
                fixed (byte* codePointer = stage.SpirV)
                {
                    ShaderModuleCreateInfo createInfo = new()
                    {
                        SType = StructureType.ShaderModuleCreateInfo,
                        CodeSize = (nuint)stage.SpirV.Length,
                        PCode = (uint*)codePointer,
                    };
                    VulkanHelpers.Throw(
                        vk.CreateShaderModule(device.Handle, &createInfo, null, out ShaderModule module),
                        $"vkCreateShaderModule ({stage.Stage})");
                    _modules.Add(stage.Stage, module);
                    _entryPoints.Add(stage.Stage, stage.EntryPoint);
                }
            }

            uint maxSetIndex = Reflection.DescriptorSets.Count == 0 ? 0 : Reflection.DescriptorSets.Max(layout => layout.SetIndex);
            uint maxBoundDescriptorSets = device.Physical.Properties.Limits.MaxBoundDescriptorSets;
            if (Reflection.DescriptorSets.Count > 0 && maxSetIndex >= maxBoundDescriptorSets)
                throw new ArgumentOutOfRangeException(nameof(description),
                    $"Descriptor set index {maxSetIndex} exceeds the device limit of {maxBoundDescriptorSets} sets.");

            HashSet<uint> seenSetIndices = [];
            foreach (Engine.RHI.DescriptorSetLayout setLayout in Reflection.DescriptorSets)
            {
                if (!seenSetIndices.Add(setLayout.SetIndex))
                    throw new ArgumentException($"Duplicate descriptor set index {setLayout.SetIndex}.", nameof(description));
                _declaredSetIndices.Add(setLayout.SetIndex);
                uint count = (uint)setLayout.Bindings.Count;
                DescriptorSetLayoutBinding[] bindings = new DescriptorSetLayoutBinding[Math.Max(1, (int)count)];
                for (int i = 0; i < setLayout.Bindings.Count; i++)
                {
                    DescriptorBinding binding = setLayout.Bindings[i];
                    bindings[i] = new DescriptorSetLayoutBinding
                    {
                        Binding = binding.Binding,
                        DescriptorType = binding.Type switch
                        {
                            Engine.RHI.DescriptorType.UniformBuffer => VkDescriptorType.UniformBuffer,
                            Engine.RHI.DescriptorType.StorageBuffer => VkDescriptorType.StorageBuffer,
                            Engine.RHI.DescriptorType.SampledImage => VkDescriptorType.SampledImage,
                            Engine.RHI.DescriptorType.StorageImage => VkDescriptorType.StorageImage,
                            Engine.RHI.DescriptorType.Sampler => VkDescriptorType.Sampler,
                            Engine.RHI.DescriptorType.CombinedImageSampler => VkDescriptorType.CombinedImageSampler,
                            Engine.RHI.DescriptorType.InputAttachment => VkDescriptorType.InputAttachment,
                            Engine.RHI.DescriptorType.AccelerationStructure => VkDescriptorType.AccelerationStructureKhr,
                            _ => throw new NotSupportedException($"Descriptor type '{binding.Type}' is not supported."),
                        },
                        DescriptorCount = Math.Max(1u, binding.ArrayCount),
                        StageFlags = VulkanHelpers.ToVulkanStageFlags(binding.Stages),
                    };
                }
                fixed (DescriptorSetLayoutBinding* bindingPointer = bindings)
                {
                    DescriptorSetLayoutCreateInfo layoutInfo = new()
                    {
                        SType = StructureType.DescriptorSetLayoutCreateInfo,
                        BindingCount = count,
                        PBindings = bindingPointer,
                    };
                    VulkanHelpers.Throw(
                        vk.CreateDescriptorSetLayout(device.Handle, &layoutInfo, null, out VkDescriptorSetLayout layout),
                        $"vkCreateDescriptorSetLayout (set {setLayout.SetIndex})");
                    _setLayouts.Add(setLayout.SetIndex, layout);
                    _ownedSetLayouts.Add(layout);
                }
            }

            if (_setLayouts.Count > 0)
                maxSetIndex = Math.Max(maxSetIndex, _setLayouts.Keys.Max());
            for (uint setIndex = 0; setIndex <= maxSetIndex && _setLayouts.Count > 0; setIndex++)
            {
                if (_setLayouts.ContainsKey(setIndex))
                    continue;
                DescriptorSetLayoutCreateInfo emptyLayoutInfo = new() { SType = StructureType.DescriptorSetLayoutCreateInfo };
                VulkanHelpers.Throw(
                    vk.CreateDescriptorSetLayout(device.Handle, &emptyLayoutInfo, null, out VkDescriptorSetLayout emptyLayout),
                    $"vkCreateDescriptorSetLayout (empty set {setIndex})");
                _setLayouts.Add(setIndex, emptyLayout);
                _ownedSetLayouts.Add(emptyLayout);
            }

            Layout = CreatePipelineLayout();
        }
        catch
        {
            foreach (ShaderModule module in _modules.Values)
                vk.DestroyShaderModule(device.Handle, module, null);
            foreach (VkDescriptorSetLayout layout in _ownedSetLayouts)
                vk.DestroyDescriptorSetLayout(device.Handle, layout, null);
            _modules.Clear();
            _entryPoints.Clear();
            _setLayouts.Clear();
            _declaredSetIndices.Clear();
            _ownedSetLayouts.Clear();
            throw;
        }
    }

    private PipelineLayout CreatePipelineLayout()
    {
        uint setCount = (uint)_setLayouts.Count;
        VkDescriptorSetLayout* layouts = stackalloc VkDescriptorSetLayout[Math.Max(1, (int)setCount)];
        for (uint index = 0; index < setCount; index++)
            layouts[index] = _setLayouts[index];

        uint pushCount = (uint)Reflection.PushConstants.Count;
        VkPushConstantRange* pushRanges = stackalloc VkPushConstantRange[Math.Max(1, (int)pushCount)];
        for (int i = 0; i < Reflection.PushConstants.Count; i++)
        {
            Engine.RHI.PushConstantRange range = Reflection.PushConstants[i];
            pushRanges[i] = new VkPushConstantRange
            {
                Offset = range.Offset,
                Size = range.Size,
                StageFlags = VulkanHelpers.ToVulkanStageFlags(range.Stages),
            };
        }

        PipelineLayoutCreateInfo layoutInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = setCount,
            PSetLayouts = layouts,
            PushConstantRangeCount = pushCount,
            PPushConstantRanges = pushRanges,
        };
        VulkanHelpers.Throw(
            _vk.CreatePipelineLayout(_device.Handle, &layoutInfo, null, out PipelineLayout layout),
            "vkCreatePipelineLayout");
        return layout;
    }

    internal ShaderModule GetModule(ShaderStage stage) =>
        _modules.TryGetValue(stage, out ShaderModule module)
            ? module
            : throw new ArgumentException($"Shader program '{Name}' has no {stage} stage.");

    internal string GetEntryPoint(ShaderStage stage) =>
        _entryPoints.TryGetValue(stage, out string? entryPoint)
            ? entryPoint
            : "main";

    public IDescriptorSet CreateDescriptorSet(uint setIndex)
    {
        lock (_lifetimeLock)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            if (!_declaredSetIndices.Contains(setIndex) || !_setLayouts.TryGetValue(setIndex, out VkDescriptorSetLayout layout))
                throw new ArgumentException($"Shader program '{Name}' has no descriptor set {setIndex}.");
            VkDescriptorSet handle = _allocator.Allocate(layout, $"{Name}.set{setIndex}");
            return new VulkanDescriptorSet(_vk, _device, _allocator, this, Layout, handle, setIndex);
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
        foreach (ShaderModule module in _modules.Values)
            _vk.DestroyShaderModule(_device.Handle, module, null);
        _modules.Clear();
        _entryPoints.Clear();
        foreach (VkDescriptorSetLayout layout in _ownedSetLayouts)
            _vk.DestroyDescriptorSetLayout(_device.Handle, layout, null);
        _setLayouts.Clear();
        _declaredSetIndices.Clear();
        _ownedSetLayouts.Clear();
        _vk.DestroyPipelineLayout(_device.Handle, Layout, null);
    }
}
