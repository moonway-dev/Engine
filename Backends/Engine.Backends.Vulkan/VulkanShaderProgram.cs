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
    private readonly Dictionary<ShaderStage, ShaderModule> _modules = [];
    private readonly Dictionary<ShaderStage, string> _entryPoints = [];
    private bool _disposed;

    public string Name { get; }

    public PipelineReflection Reflection { get; }

    internal PipelineLayout Layout { get; }

    internal IReadOnlyDictionary<uint, VkDescriptorSetLayout> SetLayouts => _setLayouts;

    public VulkanShaderProgram(Vk vk, VulkanDevice device, VulkanDescriptorAllocator allocator, in ShaderProgramDescription description)
    {
        _vk = vk;
        _device = device;
        _allocator = allocator;
        Name = description.Name;
        Reflection = description.Reflection;

        foreach (ShaderStageCode stage in description.Stages)
        {
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
                _modules[stage.Stage] = module;
                _entryPoints[stage.Stage] = stage.EntryPoint;
            }
        }

        foreach (Engine.RHI.DescriptorSetLayout setLayout in Reflection.DescriptorSets)
        {
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
                _setLayouts[setLayout.SetIndex] = layout;
            }
        }

        Layout = CreatePipelineLayout();
    }

    private PipelineLayout CreatePipelineLayout()
    {
        uint setCount = (uint)_setLayouts.Count;
        VkDescriptorSetLayout* layouts = stackalloc VkDescriptorSetLayout[Math.Max(1, (int)setCount)];
        uint index = 0;
        foreach (uint setIndex in _setLayouts.Keys.OrderBy(k => k))
            layouts[index++] = _setLayouts[setIndex];

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
        if (!_setLayouts.TryGetValue(setIndex, out VkDescriptorSetLayout layout))
            throw new ArgumentException($"Shader program '{Name}' has no descriptor set {setIndex}.");
        VkDescriptorSet handle = _allocator.Allocate(layout, $"{Name}.set{setIndex}");
        return new VulkanDescriptorSet(_vk, _device, _allocator, this, Layout, handle, setIndex);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (ShaderModule module in _modules.Values)
            _vk.DestroyShaderModule(_device.Handle, module, null);
        _modules.Clear();
        _entryPoints.Clear();
        foreach (VkDescriptorSetLayout layout in _setLayouts.Values)
            _vk.DestroyDescriptorSetLayout(_device.Handle, layout, null);
        _setLayouts.Clear();
        _vk.DestroyPipelineLayout(_device.Handle, Layout, null);
    }
}
