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
    public void Texture_CubeAndArrayViews_EnforceDimensionContracts()
    {
        NullDevice device = new();
        using ITexture cube = device.CreateTexture(new TextureDescription
        {
            Extent = new Size2D(8, 8),
            Format = TextureFormat.R8G8B8A8UNorm,
            Dimension = TextureDimension.Cube,
            MipLevels = 4,
        });
        Assert.Equal(6u, cube.ArrayLayers);
        using ITextureView cubeView = device.CreateTextureView(cube, TextureViewType.Cube);
        Assert.Equal(TextureViewType.Cube, cubeView.Type);
        Assert.Same(cube, cubeView.Texture);
        Assert.Equal(4u, cubeView.MipLevelCount);
        Assert.Equal(6u, cubeView.ArrayLayerCount);
        Assert.Throws<ArgumentException>(() => device.CreateTextureView(cube, TextureViewType.Texture2D));
        using ITextureView storageFace = device.CreateTextureView(cube, new TextureViewDescription
        {
            Type = TextureViewType.Texture2DArray,
            BaseMipLevel = 2,
            MipLevelCount = 1,
            BaseArrayLayer = 3,
            ArrayLayerCount = 1,
        });
        Assert.Equal(2u, storageFace.BaseMipLevel);
        Assert.Equal(1u, storageFace.MipLevelCount);
        Assert.Equal(3u, storageFace.BaseArrayLayer);
        Assert.Equal(1u, storageFace.ArrayLayerCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => device.CreateTextureView(cube, new TextureViewDescription
        {
            Type = TextureViewType.Texture2DArray,
            BaseMipLevel = 4,
            MipLevelCount = 1,
        }));

        using ITexture array = device.CreateTexture(new TextureDescription
        {
            Extent = new Size2D(8, 4),
            Format = TextureFormat.R8G8B8A8UNorm,
            Dimension = TextureDimension.Texture2DArray,
            ArrayLayers = 3,
        });
        using ITextureView arrayView = device.CreateTextureView(array, TextureViewType.Texture2DArray);
        Assert.Equal(3u, array.ArrayLayers);
        Assert.Equal(TextureViewType.Texture2DArray, arrayView.Type);
        Assert.Throws<ArgumentException>(() => device.CreateTextureView(array, TextureViewType.Texture2D));

        Assert.Throws<ArgumentException>(() => device.CreateTexture(new TextureDescription
        {
            Extent = new Size2D(8, 4),
            Format = TextureFormat.R8G8B8A8UNorm,
            Dimension = TextureDimension.Cube,
        }));
        Assert.Throws<ArgumentException>(() => device.CreateTexture(new TextureDescription
        {
            Extent = new Size2D(8, 8),
            Format = TextureFormat.R8G8B8A8UNorm,
            Dimension = TextureDimension.Texture2D,
            ArrayLayers = 2,
        }));
    }

    [Fact]
    public void TextureSubresources_ValidateMipLayerAndPayload()
    {
        NullDevice device = new();
        using ITexture texture = device.CreateTexture(new TextureDescription
        {
            Extent = new Size2D(4, 4),
            Format = TextureFormat.R8G8B8A8UNorm,
            Dimension = TextureDimension.Texture2DArray,
            MipLevels = 3,
            ArrayLayers = 2,
        });
        TextureSubresourceData valid = new()
        {
            MipLevel = 2,
            ArrayLayer = 1,
            Data = new byte[4],
        };
        device.UploadTextureSubresources(texture, [valid]);

        Assert.Throws<ArgumentOutOfRangeException>(() => device.UploadTextureSubresources(texture, [valid with { MipLevel = 3 }]));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.UploadTextureSubresources(texture, [valid with { ArrayLayer = 2 }]));
        Assert.Throws<ArgumentException>(() => device.UploadTextureSubresources(texture, [valid with { Data = new byte[3] }]));
        Assert.Throws<ArgumentException>(() => device.UploadTextureSubresources(texture, [valid, valid]));
        Assert.Throws<NotSupportedException>(() => TextureUploadValidation.ValidateSubresources(
            new NullTexture(new TextureDescription { Extent = new Size2D(1, 1), Format = TextureFormat.Unknown }),
            [valid with { MipLevel = 0, ArrayLayer = 0, Data = ReadOnlyMemory<byte>.Empty }]));
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
    public void GpuTimestamps_AreUnsupportedAndDoNotPublishFakeResults()
    {
        using NullRHI rhi = CreateInitialized(out _);
        using NullCommandBuffer commandBuffer = (NullCommandBuffer)rhi.Device.CreateCommandBuffer();
        Assert.False(rhi.SupportsGpuTimestamps);
        Assert.Null(rhi.LatestGpuFrameTiming);

        commandBuffer.Begin();
        commandBuffer.BeginTimestamp("Pass");
        commandBuffer.EndTimestamp();
        commandBuffer.End();

        Assert.Null(rhi.LatestGpuFrameTiming);
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
