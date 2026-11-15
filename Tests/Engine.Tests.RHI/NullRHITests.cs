using Engine.Backends.Null;
using Engine.Core;
using Engine.Mathematics;
using Engine.RHI;
using Xunit;

namespace Engine.Tests.RHI;

public class NullRHITests
{
    private static NullRHI CreateInitialized(out RHIConfiguration configuration)
    {
        configuration = new RHIConfiguration { Backend = GraphicsBackend.Null, HeadlessExtent = new Size2D(800, 600) };
        NullRHI rhi = new(new LoggerFactory(new MemoryLogSink()).CreateLogger("NullRHI"));
        rhi.Initialize(configuration, null);
        return rhi;
    }

    [Fact]
    public void Factory_CreatesNullBackend()
    {
        NullRHIFactory factory = new();
        Assert.True(factory.IsSupported());
        Assert.Equal(GraphicsBackend.Null, factory.Backend);
        using IRHI rhi = factory.Create(new LoggerFactory(new MemoryLogSink()).CreateLogger("NullRHI"));
        Assert.Equal(GraphicsBackend.Null, rhi.Backend);
        Assert.Equal("Null Device", rhi.Device.Name);
    }

    [Fact]
    public void Swapchain_FrameSequence_Works()
    {
        using NullRHI rhi = CreateInitialized(out RHIConfiguration configuration);
        Assert.Equal(new Size2D(800, 600), rhi.Swapchain.Extent);
        Assert.Equal(configuration.VSync, rhi.Swapchain.VSync);
        Assert.NotNull(rhi.Swapchain.GetRenderTarget(0));
        rhi.BeginFrame();
        ICommandBuffer commandBuffer = rhi.Device.CreateCommandBuffer();
        commandBuffer.Begin();
        rhi.ClearFramebuffer(commandBuffer, new Vector4(1f, 0f, 0f, 1f));
        commandBuffer.End();
        rhi.EndFrame(commandBuffer);
        rhi.WaitIdle();
    }

    [Fact]
    public void ClearFramebuffer_WithoutBeginFrame_Throws()
    {
        using NullRHI rhi = CreateInitialized(out _);
        ICommandBuffer commandBuffer = rhi.Device.CreateCommandBuffer();
        Assert.Throws<InvalidOperationException>(() => rhi.ClearFramebuffer(commandBuffer, Vector4.Zero));
        Assert.Throws<InvalidOperationException>(() => rhi.EndFrame(commandBuffer));
    }

    [Fact]
    public void Swapchain_Resize_UpdatesExtent()
    {
        using NullRHI rhi = CreateInitialized(out _);
        rhi.Swapchain.Resize(new Size2D(1920, 1080));
        Assert.Equal(new Size2D(1920, 1080), rhi.Swapchain.Extent);
    }

    [Fact]
    public void Device_CreatesResources()
    {
        using NullRHI rhi = CreateInitialized(out _);
        using IBuffer buffer = rhi.Device.CreateBuffer(new BufferDescription { Size = 1024, Usage = BufferUsage.Vertex, Memory = MemoryUsage.CpuToGpu });
        Assert.Equal(1024UL, buffer.Size);
        Assert.Equal(BufferUsage.Vertex, buffer.Usage);
        using ITexture texture = rhi.Device.CreateTexture(new TextureDescription { Extent = new Size2D(512, 512), Format = TextureFormat.R8G8B8A8Srgb });
        Assert.Equal(512u, texture.Width);
        Assert.Equal(1u, texture.MipLevels);
        using ISampler sampler = rhi.Device.CreateSampler(new SamplerDescription());
        Assert.NotNull(sampler);
    }

    [Fact]
    public void Device_UploadsDataWithoutError()
    {
        using NullRHI rhi = CreateInitialized(out _);
        using IBuffer buffer = rhi.Device.CreateBuffer(new BufferDescription { Size = 64, Usage = BufferUsage.Uniform, Memory = MemoryUsage.CpuToGpu });
        rhi.Device.UploadBufferData(buffer, new byte[64]);
        using ITexture texture = rhi.Device.CreateTexture(new TextureDescription { Extent = new Size2D(4, 4), Format = TextureFormat.R8G8B8A8UNorm });
        rhi.Device.UploadTextureData(texture, new byte[4 * 4 * 4]);
    }

    [Fact]
    public void CommandBuffer_EnforcesState()
    {
        using NullRHI rhi = CreateInitialized(out _);
        using NullCommandBuffer cmd = (NullCommandBuffer)rhi.Device.CreateCommandBuffer();
        ITexture target = rhi.Device.CreateTexture(new TextureDescription { Extent = new Size2D(4, 4), Format = TextureFormat.R8G8B8A8UNorm });
        Assert.Throws<InvalidOperationException>(() => cmd.BeginRendering(new RenderPassInfo
        {
            ColorAttachments = [new ColorAttachmentInfo { Texture = target, LoadOp = AttachmentLoadOp.Clear, ClearColor = Vector4.One }],
            RenderArea = new Rect(0, 0, 4, 4),
        }));
        cmd.Begin();
        cmd.BeginRendering(new RenderPassInfo
        {
            ColorAttachments = [new ColorAttachmentInfo { Texture = target, LoadOp = AttachmentLoadOp.Clear, ClearColor = new Vector4(0.5f, 0.5f, 0.5f, 1f) }],
            RenderArea = new Rect(0, 0, 4, 4),
        });
        Assert.Throws<InvalidOperationException>(() => cmd.BeginRendering(new RenderPassInfo
        {
            ColorAttachments = [new ColorAttachmentInfo { Texture = target, LoadOp = AttachmentLoadOp.Clear }],
            RenderArea = new Rect(0, 0, 4, 4),
        }));
        cmd.Draw(3);
        cmd.DrawIndexed(6);
        cmd.EndRendering();
        cmd.End();
        Assert.Equal(new Vector4(0.5f, 0.5f, 0.5f, 1f), cmd.LastClearColor);
        Assert.Equal(1u, cmd.DrawCalls);
        Assert.Equal(1u, cmd.DrawIndexedCalls);
        Assert.True(cmd.HasRecorded);
    }

    [Fact]
    public void CommandBuffer_RecordsComputeDispatch()
    {
        NullDevice device = new();
        ShaderProgramDescription programDescription = new()
        {
            Name = "Compute",
            Stages = [new ShaderStageCode { Stage = ShaderStage.Compute, SpirV = [], EntryPoint = "main" }],
        };
        using IShaderProgram program = device.CreateShaderProgram(programDescription);
        using IComputePipeline pipeline = device.CreateComputePipeline(new ComputePipelineDescription { Name = "Compute", ShaderProgram = program });
        using NullCommandBuffer commandBuffer = (NullCommandBuffer)device.CreateCommandBuffer();
        commandBuffer.Begin();
        commandBuffer.BindComputePipeline(pipeline);
        commandBuffer.Dispatch(8, 4, 2);
        commandBuffer.End();
        Assert.Equal(1u, commandBuffer.DispatchCalls);
    }

    [Fact]
    public void FrameSlots_AdvanceAfterPresent()
    {
        using NullRHI rhi = CreateInitialized(out _);
        Assert.Equal(0u, rhi.CurrentFrameSlot);
        rhi.BeginFrame();
        rhi.EndFrame(null);
        Assert.Equal(1u, rhi.CurrentFrameSlot);
        rhi.BeginFrame();
        rhi.EndFrame(null);
        Assert.Equal(0u, rhi.CurrentFrameSlot);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        NullRHI rhi = CreateInitialized(out _);
        rhi.Dispose();
        rhi.Dispose();
    }
}
