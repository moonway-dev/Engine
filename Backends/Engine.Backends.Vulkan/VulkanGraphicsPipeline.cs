using System.Runtime.InteropServices;
using Engine.RHI;
using Silk.NET.Vulkan;

using VkPrimitiveTopology = Silk.NET.Vulkan.PrimitiveTopology;
using VkPolygonMode = Silk.NET.Vulkan.PolygonMode;
using VkCullModeFlags = Silk.NET.Vulkan.CullModeFlags;
using VkFrontFace = Silk.NET.Vulkan.FrontFace;
using VkCompareOp = Silk.NET.Vulkan.CompareOp;
using VkFormat = Silk.NET.Vulkan.Format;

namespace Engine.Backends.Vulkan;

internal unsafe sealed class VulkanGraphicsPipeline : IGraphicsPipeline
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanShaderProgram _program;
    private bool _disposed;

    public Pipeline Handle { get; }

    IShaderProgram IGraphicsPipeline.ShaderProgram => _program;

    internal VulkanShaderProgram Program => _program;

    public VulkanGraphicsPipeline(Vk vk, VulkanDevice device, VulkanShaderProgram program, PipelineCache cache, in GraphicsPipelineDescription description)
    {
        _vk = vk;
        _device = device;
        _program = program;

        HashSet<uint> consumedLocations = [.. program.Reflection.VertexInputs];
        List<VertexInputAttributeDescription> attributes = [];
        List<VertexInputBindingDescription> bindings = [];
        uint location = 0;
        for (uint bindingIndex = 0; bindingIndex < description.VertexLayouts.Count; bindingIndex++)
        {
            VertexLayoutDescription layout = description.VertexLayouts[(int)bindingIndex];
            List<VertexInputAttributeDescription> bindingAttributes = [];
            foreach (VertexAttributeDescription attribute in layout.Attributes)
            {
                if (!consumedLocations.Contains(location))
                {
                    location++;
                    continue;
                }
                bindingAttributes.Add(new VertexInputAttributeDescription
                {
                    Location = location++,
                    Binding = bindingIndex,
                    Offset = attribute.Offset,
                    Format = attribute.Format switch
                    {
                        VertexFormat.Float1 => Format.R32Sfloat,
                        VertexFormat.Float2 => Format.R32G32Sfloat,
                        VertexFormat.Float3 => Format.R32G32B32Sfloat,
                        VertexFormat.Float4 => Format.R32G32B32A32Sfloat,
                        VertexFormat.UByte4Norm => Format.A8B8G8R8UnormPack32,
                        _ => throw new NotSupportedException($"Vertex format '{attribute.Format}' is not supported."),
                    },
                });
            }
            if (bindingAttributes.Count > 0)
            {
                attributes.AddRange(bindingAttributes);
                bindings.Add(new VertexInputBindingDescription
                {
                    Binding = bindingIndex,
                    Stride = layout.Stride,
                    InputRate = VertexInputRate.Vertex,
                });
            }
        }

        VertexInputAttributeDescription* attributePointer = stackalloc VertexInputAttributeDescription[Math.Max(1, attributes.Count)];
        for (int i = 0; i < attributes.Count; i++)
            attributePointer[i] = attributes[i];
        VertexInputBindingDescription* bindingPointer = stackalloc VertexInputBindingDescription[Math.Max(1, bindings.Count)];
        for (int i = 0; i < bindings.Count; i++)
            bindingPointer[i] = bindings[i];

        PipelineVertexInputStateCreateInfo vertexInput = new()
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexAttributeDescriptionCount = (uint)attributes.Count,
            PVertexAttributeDescriptions = attributePointer,
            VertexBindingDescriptionCount = (uint)bindings.Count,
            PVertexBindingDescriptions = bindingPointer,
        };

        PipelineInputAssemblyStateCreateInfo inputAssembly = new()
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = description.Topology switch
            {
                Engine.RHI.PrimitiveTopology.TriangleList => VkPrimitiveTopology.TriangleList,
                Engine.RHI.PrimitiveTopology.TriangleStrip => VkPrimitiveTopology.TriangleStrip,
                Engine.RHI.PrimitiveTopology.LineList => VkPrimitiveTopology.LineList,
                Engine.RHI.PrimitiveTopology.PointList => VkPrimitiveTopology.PointList,
                _ => VkPrimitiveTopology.TriangleList,
            },
            PrimitiveRestartEnable = false,
        };

        PipelineViewportStateCreateInfo viewportState = new()
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1,
        };

        PipelineRasterizationStateCreateInfo rasterization = new()
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = description.PolygonMode switch
            {
                Engine.RHI.PolygonMode.Fill => VkPolygonMode.Fill,
                Engine.RHI.PolygonMode.Line => VkPolygonMode.Line,
                Engine.RHI.PolygonMode.Point => VkPolygonMode.Point,
                _ => VkPolygonMode.Fill,
            },
            CullMode = description.CullMode switch
            {
                Engine.RHI.CullMode.None => VkCullModeFlags.None,
                Engine.RHI.CullMode.Front => VkCullModeFlags.FrontBit,
                Engine.RHI.CullMode.Back => VkCullModeFlags.BackBit,
                Engine.RHI.CullMode.FrontAndBack => VkCullModeFlags.FrontAndBack,
                _ => VkCullModeFlags.BackBit,
            },
            FrontFace = description.FrontFace == Engine.RHI.FrontFace.CounterClockwise ? VkFrontFace.CounterClockwise : VkFrontFace.Clockwise,
            DepthBiasEnable = description.DepthBiasEnabled,
            DepthBiasConstantFactor = description.DepthBiasConstant,
            DepthBiasSlopeFactor = description.DepthBiasSlope,
            LineWidth = 1f,
        };

        PipelineDepthStencilStateCreateInfo depthStencil = new()
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = description.DepthTest,
            DepthWriteEnable = description.DepthWrite,
            DepthCompareOp = description.DepthCompare switch
            {
                Engine.RHI.CompareOp.Never => VkCompareOp.Never,
                Engine.RHI.CompareOp.Less => VkCompareOp.Less,
                Engine.RHI.CompareOp.Equal => VkCompareOp.Equal,
                Engine.RHI.CompareOp.LessOrEqual => VkCompareOp.LessOrEqual,
                Engine.RHI.CompareOp.Greater => VkCompareOp.Greater,
                Engine.RHI.CompareOp.NotEqual => VkCompareOp.NotEqual,
                Engine.RHI.CompareOp.GreaterOrEqual => VkCompareOp.GreaterOrEqual,
                Engine.RHI.CompareOp.Always => VkCompareOp.Always,
                _ => VkCompareOp.Less,
            },
            MinDepthBounds = 0f,
            MaxDepthBounds = 1f,
        };

        PipelineColorBlendAttachmentState blendAttachment = description.BlendMode switch
        {
            BlendMode.None => new PipelineColorBlendAttachmentState
            {
                BlendEnable = false,
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            },
            BlendMode.Alpha => new PipelineColorBlendAttachmentState
            {
                BlendEnable = true,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.Zero,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            },
            BlendMode.Additive => new PipelineColorBlendAttachmentState
            {
                BlendEnable = true,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.One,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.One,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            },
            BlendMode.Premultiplied => new PipelineColorBlendAttachmentState
            {
                BlendEnable = true,
                SrcColorBlendFactor = BlendFactor.One,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            },
            _ => throw new NotSupportedException($"Blend mode '{description.BlendMode}' is not supported."),
        };
        PipelineMultisampleStateCreateInfo multisample = new()
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            RasterizationSamples = SampleCountFlags.Count1Bit,
            MinSampleShading = 1f,
        };
        uint colorFormatCount = (uint)description.ColorAttachmentFormats.Count;
        PipelineColorBlendAttachmentState* blendAttachments = null;
        if (colorFormatCount > 0)
        {
            PipelineColorBlendAttachmentState* attachments = stackalloc PipelineColorBlendAttachmentState[(int)colorFormatCount];
            for (int i = 0; i < description.ColorAttachmentFormats.Count; i++)
                attachments[i] = blendAttachment;
            blendAttachments = attachments;
        }
        PipelineColorBlendStateCreateInfo colorBlend = new()
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            AttachmentCount = colorFormatCount,
            PAttachments = blendAttachments,
        };

        PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
        uint stageCount = 0;
        if (description.ShaderProgram is VulkanShaderProgram shaderProgram)
        {
            stages[stageCount++] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = shaderProgram.GetModule(ShaderStage.Vertex),
                PName = (byte*)Marshal.StringToHGlobalAnsi(shaderProgram.GetEntryPoint(ShaderStage.Vertex)),
            };
            stages[stageCount++] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = shaderProgram.GetModule(ShaderStage.Fragment),
                PName = (byte*)Marshal.StringToHGlobalAnsi(shaderProgram.GetEntryPoint(ShaderStage.Fragment)),
            };
        }
        else
        {
            throw new ArgumentException("Pipeline shader program does not belong to the Vulkan backend.", nameof(description));
        }

        DynamicState* dynamicStates = stackalloc DynamicState[]
        {
            DynamicState.Viewport,
            DynamicState.Scissor,
        };
        PipelineDynamicStateCreateInfo dynamicState = new()
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicStates,
        };

        VkFormat* colorFormats = null;
        if (colorFormatCount > 0)
        {
            VkFormat* formats = stackalloc VkFormat[(int)colorFormatCount];
            for (int i = 0; i < description.ColorAttachmentFormats.Count; i++)
                formats[i] = VulkanHelpers.ToVulkanFormat(description.ColorAttachmentFormats[i]);
            colorFormats = formats;
        }
        VkFormat depthFormat = description.DepthAttachmentFormat.HasValue
            ? VulkanHelpers.ToVulkanFormat(description.DepthAttachmentFormat.Value)
            : VkFormat.Undefined;
        PipelineRenderingCreateInfo renderingInfo = new()
        {
            SType = StructureType.PipelineRenderingCreateInfo,
            ColorAttachmentCount = colorFormatCount,
            PColorAttachmentFormats = colorFormats,
            DepthAttachmentFormat = depthFormat,
        };

        GraphicsPipelineCreateInfo createInfo = new()
        {
            SType = StructureType.GraphicsPipelineCreateInfo,
            PNext = &renderingInfo,
            StageCount = stageCount,
            PStages = stages,
            PVertexInputState = &vertexInput,
            PInputAssemblyState = &inputAssembly,
            PViewportState = &viewportState,
            PRasterizationState = &rasterization,
            PMultisampleState = &multisample,
            PDepthStencilState = &depthStencil,
            PColorBlendState = &colorBlend,
            PDynamicState = &dynamicState,
            Layout = _program.Layout,
        };

        VulkanHelpers.Throw(
            _vk.CreateGraphicsPipelines(_device.Handle, cache, 1, &createInfo, null, out Pipeline pipeline),
            $"vkCreateGraphicsPipelines ('{description.Name}')");
        Handle = pipeline;

        Marshal.FreeHGlobal((nint)stages[0].PName);
        Marshal.FreeHGlobal((nint)stages[1].PName);
    }


    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _vk.DestroyPipeline(_device.Handle, Handle, null);
    }
}

internal unsafe sealed class VulkanComputePipeline : IComputePipeline
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private bool _disposed;

    public Pipeline Handle { get; }

    IShaderProgram IComputePipeline.ShaderProgram => _program;

    internal VulkanShaderProgram Program => _program;

    private readonly VulkanShaderProgram _program;

    public VulkanComputePipeline(Vk vk, VulkanDevice device, VulkanShaderProgram program, PipelineCache cache, in ComputePipelineDescription description)
    {
        _vk = vk;
        _device = device;
        _program = program;
        PipelineShaderStageCreateInfo stage = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.ComputeBit,
            Module = program.GetModule(ShaderStage.Compute),
            PName = (byte*)Marshal.StringToHGlobalAnsi(program.GetEntryPoint(ShaderStage.Compute)),
        };
        ComputePipelineCreateInfo createInfo = new()
        {
            SType = StructureType.ComputePipelineCreateInfo,
            Stage = stage,
            Layout = program.Layout,
        };
        VulkanHelpers.Throw(
            vk.CreateComputePipelines(device.Handle, cache, 1, &createInfo, null, out Pipeline pipeline),
            $"vkCreateComputePipelines ('{description.Name}')");
        Handle = pipeline;
        Marshal.FreeHGlobal((nint)stage.PName);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _vk.DestroyPipeline(_device.Handle, Handle, null);
    }
}

internal unsafe sealed class VulkanSampler : ISampler
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly object _lifetimeLock = new();
    private int _retainedCount;
    private bool _disposeRequested;
    private bool _disposed;

    public Sampler Handle { get; }

    public VulkanSampler(Vk vk, VulkanDevice device, in SamplerDescription description)
    {
        _vk = vk;
        _device = device;
        Filter vkFilter = description.MagFilter == FilterMode.Linear || description.MinFilter == FilterMode.Linear
            ? Filter.Linear
            : Filter.Nearest;
        SamplerMipmapMode mipmapMode = SamplerMipmapMode.Linear;
        SamplerAddressMode addressMode = description.AddressMode switch
        {
            AddressMode.Repeat => SamplerAddressMode.Repeat,
            AddressMode.MirroredRepeat => SamplerAddressMode.MirroredRepeat,
            AddressMode.Clamp => SamplerAddressMode.ClampToEdge,
            _ => SamplerAddressMode.Repeat,
        };
        SamplerCreateInfo createInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = vkFilter,
            MinFilter = vkFilter,
            MipmapMode = mipmapMode,
            AddressModeU = addressMode,
            AddressModeV = addressMode,
            AddressModeW = addressMode,
            MipLodBias = 0f,
            MaxAnisotropy = description.MaxAnisotropy,
            CompareEnable = description.CompareEnabled,
            CompareOp = description.CompareOp switch
            {
                Engine.RHI.CompareOp.Never => VkCompareOp.Never,
                Engine.RHI.CompareOp.Less => VkCompareOp.Less,
                Engine.RHI.CompareOp.Equal => VkCompareOp.Equal,
                Engine.RHI.CompareOp.LessOrEqual => VkCompareOp.LessOrEqual,
                Engine.RHI.CompareOp.Greater => VkCompareOp.Greater,
                Engine.RHI.CompareOp.NotEqual => VkCompareOp.NotEqual,
                Engine.RHI.CompareOp.GreaterOrEqual => VkCompareOp.GreaterOrEqual,
                Engine.RHI.CompareOp.Always => VkCompareOp.Always,
                _ => VkCompareOp.Less,
            },
            MinLod = 0f,
            MaxLod = 1000f,
        };
        VulkanHelpers.Throw(
            vk.CreateSampler(device.Handle, &createInfo, null, out Sampler sampler),
            "vkCreateSampler");
        Handle = sampler;
    }

    internal bool BelongsTo(VulkanDevice device) => ReferenceEquals(_device, device);

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
                throw new InvalidOperationException("Sampler retention count underflow.");
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
        if (_disposed)
            return;
        _disposed = true;
        _vk.DestroySampler(_device.Handle, Handle, null);
    }
}
