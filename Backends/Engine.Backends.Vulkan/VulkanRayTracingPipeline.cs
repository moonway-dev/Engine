using System.Runtime.InteropServices;
using Engine.RHI;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Engine.Backends.Vulkan;

internal unsafe sealed class VulkanRayTracingPipeline : IRayTracingPipeline
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanShaderProgram _program;
    private IDisposable? _programLease;
    private readonly byte[] _shaderGroupHandles;
    private readonly object _lifetimeLock = new();
    private int _retainedCount;
    private bool _disposeRequested;
    private bool _destroyed;

    public Pipeline Handle { get; }

    IShaderProgram IRayTracingPipeline.ShaderProgram => _program;

    public RayTracingPipelineDescription Description { get; }

    internal VulkanShaderProgram Program => _program;

    internal bool BelongsTo(VulkanDevice device) => ReferenceEquals(_device, device);

    public VulkanRayTracingPipeline(Vk vk, VulkanDevice device, VulkanShaderProgram program, in RayTracingPipelineDescription description)
    {
        _vk = vk;
        _device = device;
        _program = program;
        if (!device.SupportsRayTracing)
            throw new NotSupportedException(device.RayTracingUnavailableReason ?? "Vulkan ray-tracing pipelines are unavailable.");
        if (!program.BelongsTo(device))
            throw new ArgumentException("Shader program does not belong to this Vulkan device.", nameof(program));
        ObjectDisposedException.ThrowIf(program.IsDisposed, program);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(description.Groups);
        Description = description with { Groups = description.Groups.ToArray() };

        IReadOnlyList<ShaderStage> stages = program.Stages;
        RayTracingValidation.ValidateGroups(Description, stages, device.Physical.RayTracing.Limits);
        if (stages.Count == 0 || stages.Any(stage => stage is not (ShaderStage.RayGeneration or ShaderStage.Miss
                or ShaderStage.ClosestHit or ShaderStage.AnyHit or ShaderStage.Intersection or ShaderStage.Callable)))
            throw new ArgumentException("A ray-tracing program may contain only ray-tracing shader stages.", nameof(description));

        PipelineShaderStageCreateInfo[] vkStages = new PipelineShaderStageCreateInfo[stages.Count];
        RayTracingShaderGroupCreateInfoKHR[] groups = new RayTracingShaderGroupCreateInfoKHR[Description.Groups.Count];
        nint[] entryPoints = new nint[stages.Count];
        Pipeline pipeline = default;
        IDisposable? programLease = null;
        try
        {
            programLease = program.Retain();
            for (int index = 0; index < stages.Count; index++)
            {
                ShaderStage stage = stages[index];
                entryPoints[index] = Marshal.StringToHGlobalAnsi(program.GetEntryPoint(stage));
                vkStages[index] = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ToVulkanStage(stage),
                    Module = program.GetModule(stage),
                    PName = (byte*)entryPoints[index],
                };
            }

            for (int index = 0; index < groups.Length; index++)
            {
                RayTracingShaderGroup source = Description.Groups[index];
                groups[index] = new RayTracingShaderGroupCreateInfoKHR
                {
                    SType = StructureType.RayTracingShaderGroupCreateInfoKhr,
                    Type = source.Type switch
                    {
                        RayTracingShaderGroupType.General => (RayTracingShaderGroupTypeKHR)0,
                        RayTracingShaderGroupType.TrianglesHit => (RayTracingShaderGroupTypeKHR)1,
                        RayTracingShaderGroupType.ProceduralHit => (RayTracingShaderGroupTypeKHR)2,
                        _ => throw new ArgumentOutOfRangeException(nameof(description), "Unknown ray-tracing shader group type."),
                    },
                    GeneralShader = ToVulkanShaderIndex(source.GeneralShader),
                    ClosestHitShader = ToVulkanShaderIndex(source.ClosestHitShader),
                    AnyHitShader = ToVulkanShaderIndex(source.AnyHitShader),
                    IntersectionShader = ToVulkanShaderIndex(source.IntersectionShader),
                };
            }

            fixed (PipelineShaderStageCreateInfo* stagePointer = vkStages)
            fixed (RayTracingShaderGroupCreateInfoKHR* groupPointer = groups)
            {
                RayTracingPipelineCreateInfoKHR createInfo = new()
                {
                    SType = StructureType.RayTracingPipelineCreateInfoKhr,
                    StageCount = (uint)vkStages.Length,
                    PStages = stagePointer,
                    GroupCount = (uint)groups.Length,
                    PGroups = groupPointer,
                    MaxPipelineRayRecursionDepth = Description.MaxRecursionDepth,
                    Layout = program.Layout,
                    BasePipelineIndex = -1,
                };
                VulkanHelpers.Throw(
                    device.RayTracingPipelineExtension.CreateRayTracingPipelines(device.Handle, default, default, 1, &createInfo, null, &pipeline),
                    $"vkCreateRayTracingPipelinesKHR ('{Description.Name}')");
            }

            uint handleSize = device.Physical.RayTracing.Limits.ShaderGroupHandleSize;
            byte[] shaderGroupHandles = new byte[checked(groups.Length * (int)handleSize)];
            fixed (byte* handlePointer = shaderGroupHandles)
                VulkanHelpers.Throw(
                    device.RayTracingPipelineExtension.GetRayTracingShaderGroupHandles(
                        device.Handle, pipeline, 0, (uint)groups.Length, (nuint)shaderGroupHandles.Length, handlePointer),
                    $"vkGetRayTracingShaderGroupHandlesKHR ('{Description.Name}')");

            Handle = pipeline;
            _shaderGroupHandles = shaderGroupHandles;
            _programLease = programLease;
            programLease = null;
        }
        catch
        {
            if (pipeline.Handle != 0)
                _vk.DestroyPipeline(_device.Handle, pipeline, null);
            programLease?.Dispose();
            throw;
        }
        finally
        {
            foreach (nint entryPoint in entryPoints)
            {
                if (entryPoint != 0)
                    Marshal.FreeHGlobal(entryPoint);
            }
        }
    }

    public byte[] GetShaderGroupHandles()
    {
        lock (_lifetimeLock)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            return _shaderGroupHandles.ToArray();
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
                throw new InvalidOperationException("Ray-tracing pipeline retention count underflow.");
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
        _vk.DestroyPipeline(_device.Handle, Handle, null);
        Interlocked.Exchange(ref _programLease, null)?.Dispose();
    }

    private static ShaderStageFlags ToVulkanStage(ShaderStage stage) => stage switch
    {
        ShaderStage.RayGeneration => ShaderStageFlags.RaygenBitKhr,
        ShaderStage.Miss => ShaderStageFlags.MissBitKhr,
        ShaderStage.ClosestHit => ShaderStageFlags.ClosestHitBitKhr,
        ShaderStage.AnyHit => (ShaderStageFlags)0x200,
        ShaderStage.Intersection => (ShaderStageFlags)0x1000,
        ShaderStage.Callable => (ShaderStageFlags)0x2000,
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };

    private static uint ToVulkanShaderIndex(int index) => index < 0 ? uint.MaxValue : checked((uint)index);
}
