using VkSemaphore = Silk.NET.Vulkan.Semaphore;
using VkImageLayout = Silk.NET.Vulkan.ImageLayout;
using System.Runtime.InteropServices;
using System.Text;
using Engine.Mathematics;
using Engine.RHI;
using Silk.NET.Vulkan;

namespace Engine.Backends.Vulkan;

internal static unsafe class VulkanHelpers
{
    public const string LayerKhronosValidation = "VK_LAYER_KHRONOS_validation";
    public const string ExtensionDebugUtils = "VK_EXT_debug_utils";
    public const string ExtensionSurface = "VK_KHR_surface";
    public const string ExtensionSwapchain = "VK_KHR_swapchain";
    public const string ExtensionAccelerationStructure = "VK_KHR_acceleration_structure";
    public const string ExtensionRayTracingPipeline = "VK_KHR_ray_tracing_pipeline";
    public const string ExtensionRayQuery = "VK_KHR_ray_query";
    public const string ExtensionDeferredHostOperations = "VK_KHR_deferred_host_operations";
    public const string ExtensionMeshShader = "VK_EXT_mesh_shader";

    public static uint MakeVersion(int major, int minor, int patch) =>
        ((uint)major << 22) | ((uint)minor << 12) | (uint)patch;

    public static string DecodeVersion(uint version) =>
        $"{version >> 22}.{(version >> 12) & 0x3FF}.{version & 0xFFF}";

    public static void Throw(Result result, string context)
    {
        if (result != Result.Success)
            throw new VulkanException(result, context);
    }

    public static nint AllocUtf8(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text + "\0");
        nint pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }

    public static void FreeUtf8(nint pointer) => Marshal.FreeHGlobal(pointer);

    public static byte** AllocUtf8Array(IReadOnlyList<string> values, out uint count)
    {
        count = (uint)values.Count;
        byte** buffer = (byte**)Marshal.AllocHGlobal(IntPtr.Size * values.Count);
        for (int i = 0; i < values.Count; i++)
            buffer[i] = (byte*)AllocUtf8(values[i]);
        return buffer;
    }

    public static void FreeUtf8Array(byte** values, int count)
    {
        for (int i = 0; i < count; i++)
            Marshal.FreeHGlobal((nint)values[i]);
        Marshal.FreeHGlobal((nint)values);
    }

    public static string ReadFixedString(byte* pointer, int maxLength)
    {
        int length = 0;
        while (length < maxLength && pointer[length] != 0)
            length++;
        return Encoding.UTF8.GetString(pointer, length);
    }

    public static string GetVendorName(uint vendorId) => vendorId switch
    {
        0x10DE => "NVIDIA",
        0x1002 => "AMD",
        0x8086 => "Intel",
        0x106B => "Apple",
        0x13B5 => "ARM",
        0x5143 => "Qualcomm",
        _ => $"Vendor 0x{vendorId:X4}",
    };

    public static Format ToVulkanFormat(TextureFormat format) => format switch
    {
        TextureFormat.R8Unorm => Format.R8Unorm,
        TextureFormat.R8G8B8A8UNorm => Format.R8G8B8A8Unorm,
        TextureFormat.R8G8B8A8Srgb => Format.R8G8B8A8Srgb,
        TextureFormat.B8G8R8A8UNorm => Format.B8G8R8A8Unorm,
        TextureFormat.B8G8R8A8Srgb => Format.B8G8R8A8Srgb,
        TextureFormat.R16G16B16A16Float => Format.R16G16B16A16Sfloat,
        TextureFormat.R32G32B32A32Float => Format.R32G32B32A32Sfloat,
        TextureFormat.R11G11B10Float => Format.B10G11R11UfloatPack32,
        TextureFormat.D16Unorm => Format.D16Unorm,
        TextureFormat.D32Float => Format.D32Sfloat,
        TextureFormat.D24UnormS8Uint => Format.D24UnormS8Uint,
        TextureFormat.D32FloatS8Uint => Format.D32SfloatS8Uint,
        _ => throw new NotSupportedException($"Texture format '{format}' is not supported by the Vulkan backend."),
    };

    public static TextureFormat FromVulkanFormat(Format format) => format switch
    {
        Format.R8Unorm => TextureFormat.R8Unorm,
        Format.R8G8B8A8Unorm => TextureFormat.R8G8B8A8UNorm,
        Format.R8G8B8A8Srgb => TextureFormat.R8G8B8A8Srgb,
        Format.B8G8R8A8Unorm => TextureFormat.B8G8R8A8UNorm,
        Format.B8G8R8A8Srgb => TextureFormat.B8G8R8A8Srgb,
        Format.R16G16B16A16Sfloat => TextureFormat.R16G16B16A16Float,
        Format.R32G32B32A32Sfloat => TextureFormat.R32G32B32A32Float,
        Format.B10G11R11UfloatPack32 => TextureFormat.R11G11B10Float,
        Format.D16Unorm => TextureFormat.D16Unorm,
        Format.D32Sfloat => TextureFormat.D32Float,
        Format.D24UnormS8Uint => TextureFormat.D24UnormS8Uint,
        Format.D32SfloatS8Uint => TextureFormat.D32FloatS8Uint,
        _ => TextureFormat.Unknown,
    };

    public static VkImageLayout ToVulkanLayout(Engine.RHI.ImageLayout layout) => layout switch
    {
        Engine.RHI.ImageLayout.Undefined => VkImageLayout.Undefined,
        Engine.RHI.ImageLayout.General => VkImageLayout.General,
        Engine.RHI.ImageLayout.ColorAttachmentOptimal => VkImageLayout.ColorAttachmentOptimal,
        Engine.RHI.ImageLayout.DepthAttachmentOptimal => VkImageLayout.DepthAttachmentOptimal,
        Engine.RHI.ImageLayout.DepthReadOnly => VkImageLayout.DepthReadOnlyOptimal,
        Engine.RHI.ImageLayout.DepthStencilReadOnlyOptimal => VkImageLayout.DepthStencilReadOnlyOptimal,
        Engine.RHI.ImageLayout.ShaderReadOnlyOptimal => VkImageLayout.ShaderReadOnlyOptimal,
        Engine.RHI.ImageLayout.TransferSrcOptimal => VkImageLayout.TransferSrcOptimal,
        Engine.RHI.ImageLayout.TransferDstOptimal => VkImageLayout.TransferDstOptimal,
        Engine.RHI.ImageLayout.PresentSrcKhr => VkImageLayout.PresentSrcKhr,
        _ => VkImageLayout.Undefined,
    };

    public static (AccessFlags Src, AccessFlags Dst) LayoutAccess(VkImageLayout layout) => layout switch
    {
        VkImageLayout.ColorAttachmentOptimal => (AccessFlags.ColorAttachmentWriteBit, AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit),
        VkImageLayout.DepthAttachmentOptimal => (AccessFlags.DepthStencilAttachmentWriteBit, AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit),
        VkImageLayout.DepthReadOnlyOptimal => (AccessFlags.ShaderReadBit, AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit),
        VkImageLayout.DepthStencilReadOnlyOptimal => (AccessFlags.DepthStencilAttachmentWriteBit, AccessFlags.ShaderReadBit),
        VkImageLayout.ShaderReadOnlyOptimal => (AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit, AccessFlags.ShaderReadBit),
        VkImageLayout.TransferSrcOptimal => (AccessFlags.ColorAttachmentWriteBit | AccessFlags.TransferWriteBit, AccessFlags.TransferReadBit),
        VkImageLayout.TransferDstOptimal => (AccessFlags.TransferWriteBit, AccessFlags.TransferWriteBit | AccessFlags.TransferReadBit),
        VkImageLayout.PresentSrcKhr => (AccessFlags.ColorAttachmentWriteBit | AccessFlags.TransferWriteBit, 0),
        _ => (0, 0),
    };

    public static int FormatBytesPerPixel(TextureFormat format) => format switch
    {
        TextureFormat.R8Unorm => 1,
        TextureFormat.R8G8B8A8UNorm or TextureFormat.R8G8B8A8Srgb or TextureFormat.B8G8R8A8UNorm or TextureFormat.B8G8R8A8Srgb => 4,
        TextureFormat.R16G16B16A16Float => 8,
        TextureFormat.R32G32B32A32Float => 16,
        TextureFormat.R11G11B10Float => 4,
        TextureFormat.D16Unorm => 2,
        TextureFormat.D32Float or TextureFormat.D24UnormS8Uint => 4,
        TextureFormat.D32FloatS8Uint => 8,
        _ => 4,
    };

    public static ImageUsageFlags ToVulkanTextureUsage(TextureUsage usage, TextureFormat format)
    {
        if (usage == TextureUsage.None)
            usage = TextureUsage.Sampled | TextureUsage.TransferSource | TextureUsage.TransferDestination;
        ImageUsageFlags flags = 0;
        if (usage.HasFlag(TextureUsage.Sampled))
            flags |= ImageUsageFlags.SampledBit;
        if (usage.HasFlag(TextureUsage.Storage))
            flags |= ImageUsageFlags.StorageBit;
        if (usage.HasFlag(TextureUsage.ColorAttachment))
            flags |= ImageUsageFlags.ColorAttachmentBit;
        if (usage.HasFlag(TextureUsage.DepthAttachment))
            flags |= ImageUsageFlags.DepthStencilAttachmentBit;
        if (usage.HasFlag(TextureUsage.TransferSource))
            flags |= ImageUsageFlags.TransferSrcBit;
        if (usage.HasFlag(TextureUsage.TransferDestination))
            flags |= ImageUsageFlags.TransferDstBit;
        if (format.IsDepth())
            flags |= ImageUsageFlags.DepthStencilAttachmentBit;
        else if ((flags & (ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.StorageBit)) == 0)
            flags |= ImageUsageFlags.ColorAttachmentBit;
        return flags;
    }

    public static BufferUsageFlags ToVulkanBufferUsage(BufferUsage usage)
    {
        BufferUsageFlags flags = 0;
        if (usage.HasFlag(BufferUsage.Vertex))
            flags |= BufferUsageFlags.VertexBufferBit;
        if (usage.HasFlag(BufferUsage.Index))
            flags |= BufferUsageFlags.IndexBufferBit;
        if (usage.HasFlag(BufferUsage.Uniform))
            flags |= BufferUsageFlags.UniformBufferBit;
        if (usage.HasFlag(BufferUsage.Storage))
            flags |= BufferUsageFlags.StorageBufferBit;
        if (usage.HasFlag(BufferUsage.Indirect))
            flags |= BufferUsageFlags.IndirectBufferBit;
        if (usage.HasFlag(BufferUsage.TransferSource))
            flags |= BufferUsageFlags.TransferSrcBit;
        if (usage.HasFlag(BufferUsage.TransferDestination))
            flags |= BufferUsageFlags.TransferDstBit;
        if (usage.HasFlag(BufferUsage.DeviceAddress))
            flags |= BufferUsageFlags.ShaderDeviceAddressBit;
        if (usage.HasFlag(BufferUsage.AccelerationStructureBuildInput))
            flags |= BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr;
        if (usage.HasFlag(BufferUsage.AccelerationStructureStorage))
            flags |= BufferUsageFlags.AccelerationStructureStorageBitKhr;
        if (usage.HasFlag(BufferUsage.ShaderBindingTable))
            flags |= BufferUsageFlags.ShaderBindingTableBitKhr;
        if (flags == 0)
            throw new ArgumentException("Buffer usage must specify at least one flag.", nameof(usage));
        return flags;
    }

    public static MemoryPropertyFlags ToVulkanMemoryFlags(MemoryUsage usage) => usage switch
    {
        MemoryUsage.GpuOnly => MemoryPropertyFlags.DeviceLocalBit,
        MemoryUsage.CpuToGpu => MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
        MemoryUsage.GpuToCpu => MemoryPropertyFlags.DeviceLocalBit | MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
        MemoryUsage.CpuOnly => MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
        _ => 0,
    };

    public static PipelineStageFlags ToVulkanPipelineStages(PipelineStages stages)
    {
        PipelineStageFlags flags = 0;
        if (stages.HasFlag(PipelineStages.Top)) flags |= PipelineStageFlags.TopOfPipeBit;
        if (stages.HasFlag(PipelineStages.DrawIndirect)) flags |= PipelineStageFlags.DrawIndirectBit;
        if (stages.HasFlag(PipelineStages.VertexInput)) flags |= PipelineStageFlags.VertexInputBit;
        if (stages.HasFlag(PipelineStages.VertexShader)) flags |= PipelineStageFlags.VertexShaderBit;
        if (stages.HasFlag(PipelineStages.FragmentShader)) flags |= PipelineStageFlags.FragmentShaderBit;
        if (stages.HasFlag(PipelineStages.EarlyDepth)) flags |= PipelineStageFlags.EarlyFragmentTestsBit;
        if (stages.HasFlag(PipelineStages.LateDepth)) flags |= PipelineStageFlags.LateFragmentTestsBit;
        if (stages.HasFlag(PipelineStages.ColorOutput)) flags |= PipelineStageFlags.ColorAttachmentOutputBit;
        if (stages.HasFlag(PipelineStages.ComputeShader)) flags |= PipelineStageFlags.ComputeShaderBit;
        if (stages.HasFlag(PipelineStages.Transfer)) flags |= PipelineStageFlags.TransferBit;
        if (stages.HasFlag(PipelineStages.Bottom)) flags |= PipelineStageFlags.BottomOfPipeBit;
        if (stages.HasFlag(PipelineStages.Host)) flags |= PipelineStageFlags.HostBit;
        if (stages.HasFlag(PipelineStages.AllGraphics)) flags |= PipelineStageFlags.AllGraphicsBit;
        if (stages.HasFlag(PipelineStages.AllCommands)) flags |= PipelineStageFlags.AllCommandsBit;
        if (stages.HasFlag(PipelineStages.AccelerationStructureBuild)) flags |= PipelineStageFlags.AccelerationStructureBuildBitKhr;
        if (stages.HasFlag(PipelineStages.RayTracingShader)) flags |= PipelineStageFlags.RayTracingShaderBitKhr;
        return flags == 0 ? PipelineStageFlags.AllCommandsBit : flags;
    }

    public static PipelineStageFlags2 ToVulkanPipelineStages2(PipelineStages stages)
    {
        PipelineStageFlags2 flags = 0;
        if (stages.HasFlag(PipelineStages.Top)) flags |= PipelineStageFlags2.TopOfPipeBit;
        if (stages.HasFlag(PipelineStages.DrawIndirect)) flags |= PipelineStageFlags2.DrawIndirectBit;
        if (stages.HasFlag(PipelineStages.VertexInput)) flags |= PipelineStageFlags2.VertexInputBit;
        if (stages.HasFlag(PipelineStages.VertexShader)) flags |= PipelineStageFlags2.VertexShaderBit;
        if (stages.HasFlag(PipelineStages.FragmentShader)) flags |= PipelineStageFlags2.FragmentShaderBit;
        if (stages.HasFlag(PipelineStages.EarlyDepth)) flags |= PipelineStageFlags2.EarlyFragmentTestsBit;
        if (stages.HasFlag(PipelineStages.LateDepth)) flags |= PipelineStageFlags2.LateFragmentTestsBit;
        if (stages.HasFlag(PipelineStages.ColorOutput)) flags |= PipelineStageFlags2.ColorAttachmentOutputBit;
        if (stages.HasFlag(PipelineStages.ComputeShader)) flags |= PipelineStageFlags2.ComputeShaderBit;
        if (stages.HasFlag(PipelineStages.Transfer)) flags |= PipelineStageFlags2.AllTransferBit;
        if (stages.HasFlag(PipelineStages.Bottom)) flags |= PipelineStageFlags2.BottomOfPipeBit;
        if (stages.HasFlag(PipelineStages.Host)) flags |= PipelineStageFlags2.HostBit;
        if (stages.HasFlag(PipelineStages.AllGraphics)) flags |= PipelineStageFlags2.AllGraphicsBit;
        if (stages.HasFlag(PipelineStages.AllCommands)) flags |= PipelineStageFlags2.AllCommandsBit;
        if (stages.HasFlag(PipelineStages.AccelerationStructureBuild)) flags |= PipelineStageFlags2.AccelerationStructureBuildBitKhr;
        if (stages.HasFlag(PipelineStages.RayTracingShader)) flags |= PipelineStageFlags2.RayTracingShaderBitKhr;
        return flags;
    }

    public static AccessFlags2 ToVulkanAccess2(ResourceAccess access)
    {
        AccessFlags2 flags = 0;
        if (access.HasFlag(ResourceAccess.IndirectRead)) flags |= AccessFlags2.IndirectCommandReadBit;
        if (access.HasFlag(ResourceAccess.IndexRead)) flags |= AccessFlags2.IndexReadBit;
        if (access.HasFlag(ResourceAccess.VertexRead)) flags |= AccessFlags2.VertexAttributeReadBit;
        if (access.HasFlag(ResourceAccess.UniformRead)) flags |= AccessFlags2.UniformReadBit;
        if (access.HasFlag(ResourceAccess.ShaderRead)) flags |= AccessFlags2.ShaderReadBit;
        if (access.HasFlag(ResourceAccess.ShaderWrite)) flags |= AccessFlags2.ShaderWriteBit;
        if (access.HasFlag(ResourceAccess.ColorRead)) flags |= AccessFlags2.ColorAttachmentReadBit;
        if (access.HasFlag(ResourceAccess.ColorWrite)) flags |= AccessFlags2.ColorAttachmentWriteBit;
        if (access.HasFlag(ResourceAccess.DepthRead)) flags |= AccessFlags2.DepthStencilAttachmentReadBit;
        if (access.HasFlag(ResourceAccess.DepthWrite)) flags |= AccessFlags2.DepthStencilAttachmentWriteBit;
        if (access.HasFlag(ResourceAccess.TransferRead)) flags |= AccessFlags2.TransferReadBit;
        if (access.HasFlag(ResourceAccess.TransferWrite)) flags |= AccessFlags2.TransferWriteBit;
        if (access.HasFlag(ResourceAccess.HostRead)) flags |= AccessFlags2.HostReadBit;
        if (access.HasFlag(ResourceAccess.HostWrite)) flags |= AccessFlags2.HostWriteBit;
        if (access.HasFlag(ResourceAccess.MemoryRead)) flags |= AccessFlags2.MemoryReadBit;
        if (access.HasFlag(ResourceAccess.MemoryWrite)) flags |= AccessFlags2.MemoryWriteBit;
        if (access.HasFlag(ResourceAccess.AccelerationStructureRead)) flags |= AccessFlags2.AccelerationStructureReadBitKhr;
        if (access.HasFlag(ResourceAccess.AccelerationStructureWrite)) flags |= AccessFlags2.AccelerationStructureWriteBitKhr;
        return flags;
    }

    public static AccessFlags ToVulkanAccess(ResourceAccess access)
    {
        AccessFlags flags = 0;
        if (access.HasFlag(ResourceAccess.IndirectRead)) flags |= AccessFlags.IndirectCommandReadBit;
        if (access.HasFlag(ResourceAccess.IndexRead)) flags |= AccessFlags.IndexReadBit;
        if (access.HasFlag(ResourceAccess.VertexRead)) flags |= AccessFlags.VertexAttributeReadBit;
        if (access.HasFlag(ResourceAccess.UniformRead)) flags |= AccessFlags.UniformReadBit;
        if (access.HasFlag(ResourceAccess.ShaderRead)) flags |= AccessFlags.ShaderReadBit;
        if (access.HasFlag(ResourceAccess.ShaderWrite)) flags |= AccessFlags.ShaderWriteBit;
        if (access.HasFlag(ResourceAccess.ColorRead)) flags |= AccessFlags.ColorAttachmentReadBit;
        if (access.HasFlag(ResourceAccess.ColorWrite)) flags |= AccessFlags.ColorAttachmentWriteBit;
        if (access.HasFlag(ResourceAccess.DepthRead)) flags |= AccessFlags.DepthStencilAttachmentReadBit;
        if (access.HasFlag(ResourceAccess.DepthWrite)) flags |= AccessFlags.DepthStencilAttachmentWriteBit;
        if (access.HasFlag(ResourceAccess.TransferRead)) flags |= AccessFlags.TransferReadBit;
        if (access.HasFlag(ResourceAccess.TransferWrite)) flags |= AccessFlags.TransferWriteBit;
        if (access.HasFlag(ResourceAccess.HostRead)) flags |= AccessFlags.HostReadBit;
        if (access.HasFlag(ResourceAccess.HostWrite)) flags |= AccessFlags.HostWriteBit;
        if (access.HasFlag(ResourceAccess.MemoryRead)) flags |= AccessFlags.MemoryReadBit;
        if (access.HasFlag(ResourceAccess.MemoryWrite)) flags |= AccessFlags.MemoryWriteBit;
        if (access.HasFlag(ResourceAccess.AccelerationStructureRead)) flags |= AccessFlags.AccelerationStructureReadBitKhr;
        if (access.HasFlag(ResourceAccess.AccelerationStructureWrite)) flags |= AccessFlags.AccelerationStructureWriteBitKhr;
        return flags;
    }

    public static ShaderStageFlags ToVulkanStageFlags(ShaderStages stages)
    {
        ShaderStageFlags flags = 0;
        if (stages.HasFlag(ShaderStages.Vertex))
            flags |= ShaderStageFlags.VertexBit;
        if (stages.HasFlag(ShaderStages.Fragment))
            flags |= ShaderStageFlags.FragmentBit;
        if (stages.HasFlag(ShaderStages.Compute))
            flags |= ShaderStageFlags.ComputeBit;
        if (stages.HasFlag(ShaderStages.Geometry))
            flags |= ShaderStageFlags.GeometryBit;
        if (stages.HasFlag(ShaderStages.TessellationControl))
            flags |= ShaderStageFlags.TessellationControlBit;
        if (stages.HasFlag(ShaderStages.TessellationEvaluation))
            flags |= ShaderStageFlags.TessellationEvaluationBit;
        if (stages.HasFlag(ShaderStages.RayGeneration))
            flags |= ShaderStageFlags.RaygenBitKhr;
        if (stages.HasFlag(ShaderStages.Miss))
            flags |= ShaderStageFlags.MissBitKhr;
        if (stages.HasFlag(ShaderStages.ClosestHit))
            flags |= ShaderStageFlags.ClosestHitBitKhr;
        if (stages.HasFlag(ShaderStages.AnyHit))
            flags |= (ShaderStageFlags)0x200;
        if (stages.HasFlag(ShaderStages.Intersection))
            flags |= (ShaderStageFlags)0x1000;
        if (stages.HasFlag(ShaderStages.Callable))
            flags |= (ShaderStageFlags)0x2000;
        if (flags == 0)
            throw new ArgumentException("Shader stages must specify at least one flag.", nameof(stages));
        return flags;
    }
}
