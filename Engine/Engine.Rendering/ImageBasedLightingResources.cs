using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Engine.Mathematics;
using Engine.RHI;
using Engine.Shaders;

namespace Engine.Rendering;

public sealed class ImageBasedLightingResources : IDisposable
{
    private const uint EnvironmentSize = 32;
    private const uint EnvironmentMipLevels = 6;
    private const uint IrradianceSize = 16;
    private const uint PrefilteredSize = 64;
    private const uint PrefilteredMipLevels = 7;
    private const uint BrdfSize = 128;
    private readonly bool _ownsEnvironment;
    private readonly ITextureView[] _storageViews;
    private readonly IComputePipeline? _bakePipeline;
    private readonly IShaderProgram? _bakeProgram;
    private readonly ISampler? _ownedSampler;
    private bool _disposed;

    public ITexture Environment { get; }
    public ITexture Irradiance { get; }
    public ITexture PrefilteredSpecular { get; }
    public ITexture BrdfLut { get; }
    public ISampler Sampler => _ownedSampler ?? throw new ObjectDisposedException(nameof(ImageBasedLightingResources));
    public bool IsBaked { get; }

    private ImageBasedLightingResources(
        ITexture environment,
        bool ownsEnvironment,
        ITexture irradiance,
        ITexture prefilteredSpecular,
        ITexture brdfLut,
        ITextureView[] storageViews,
        IComputePipeline? bakePipeline,
        IShaderProgram? bakeProgram,
        ISampler? ownedSampler,
        bool isBaked)
    {
        Environment = environment;
        _ownsEnvironment = ownsEnvironment;
        Irradiance = irradiance;
        PrefilteredSpecular = prefilteredSpecular;
        BrdfLut = brdfLut;
        _storageViews = storageViews;
        _bakePipeline = bakePipeline;
        _bakeProgram = bakeProgram;
        _ownedSampler = ownedSampler;
        IsBaked = isBaked;
    }

    public static ImageBasedLightingResources Create(
        IDevice device,
        SlangShaderCompiler? compiler,
        string shaderPath,
        ITexture? environment = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (environment is not null && environment.Dimension != TextureDimension.Cube)
            throw new ArgumentException("IBL environment maps must be cube textures.", nameof(environment));

        ITexture? ownedEnvironment = null;
        ITexture? irradiance = null;
        ITexture? prefiltered = null;
        ITexture? brdf = null;
        List<ITextureView> views = [];
        ISampler? ownedSampler = null;
        IShaderProgram? program = null;
        IComputePipeline? pipeline = null;
        try
        {
            ownedSampler = device.CreateSampler(new SamplerDescription
            {
                MagFilter = FilterMode.Linear,
                MinFilter = FilterMode.Linear,
                AddressMode = AddressMode.Clamp,
                MaxAnisotropy = 1,
            });
            bool supportsBake = device.Features.SupportsCompute;
            ISampler sampler = ownedSampler;
            if (environment is null)
            {
                ownedEnvironment = CreateEnvironment(device);
                environment = ownedEnvironment;
            }
            irradiance = CreateCube(device, IrradianceSize, 1, supportsBake);
            prefiltered = CreateCube(device, PrefilteredSize, PrefilteredMipLevels, supportsBake);
            brdf = CreateTexture2D(device, BrdfSize, BrdfSize, supportsBake);

            if (supportsBake)
            {
                SlangShaderCompiler shaderCompiler = compiler ?? throw new InvalidOperationException("Compute-capable devices require the shader compiler to bake IBL resources.");
                ShaderProgramDescription description = shaderCompiler.CompileProgram(
                    shaderPath,
                    [(ShaderStage.Compute, "csMain")],
                    programName: "IBLBake");
                program = device.CreateShaderProgram(description);
                pipeline = device.CreateComputePipeline(new ComputePipelineDescription
                {
                    Name = "IBLBake",
                    ShaderProgram = program,
                });
                bool baked = Bake(device, program, pipeline, environment, irradiance, prefiltered, brdf, sampler, views);
                return new ImageBasedLightingResources(environment, ownedEnvironment is not null, irradiance, prefiltered, brdf, views.ToArray(), pipeline, program, ownedSampler, baked);
            }

            UploadNeutralFallback(device, irradiance, prefiltered, brdf);
            return new ImageBasedLightingResources(environment, ownedEnvironment is not null, irradiance, prefiltered, brdf, views.ToArray(), null, null, ownedSampler, false);
        }
        catch
        {
            foreach (ITextureView view in views)
                view.Dispose();
            pipeline?.Dispose();
            program?.Dispose();
            ownedSampler?.Dispose();
            brdf?.Dispose();
            prefiltered?.Dispose();
            irradiance?.Dispose();
            ownedEnvironment?.Dispose();
            throw;
        }
    }

    private static bool Bake(
        IDevice device,
        IShaderProgram program,
        IComputePipeline pipeline,
        ITexture environment,
        ITexture irradiance,
        ITexture prefiltered,
        ITexture brdf,
        ISampler sampler,
        List<ITextureView> storageViews)
    {
        using ICommandBuffer commandBuffer = device.CreateCommandBuffer();
        List<IDescriptorSet> descriptorSets = [];
        try
        {
            using ITextureView brdfStorageView = device.CreateTextureView(brdf, new TextureViewDescription
            {
                Type = TextureViewType.Texture2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
            });
            commandBuffer.Begin();
        commandBuffer.ImageBarrier(irradiance, ImageLayout.Undefined, ImageLayout.General,
            PipelineStages.Top, ResourceAccess.None, PipelineStages.ComputeShader, ResourceAccess.ShaderWrite);
        commandBuffer.ImageBarrier(prefiltered, ImageLayout.Undefined, ImageLayout.General,
            PipelineStages.Top, ResourceAccess.None, PipelineStages.ComputeShader, ResourceAccess.ShaderWrite);
        commandBuffer.ImageBarrier(brdf, ImageLayout.Undefined, ImageLayout.General,
            PipelineStages.Top, ResourceAccess.None, PipelineStages.ComputeShader, ResourceAccess.ShaderWrite);
        commandBuffer.BindComputePipeline(pipeline);

        for (uint face = 0; face < 6; face++)
        {
            ITextureView output = CreateStorageView(device, irradiance, 0, face);
            storageViews.Add(output);
            Dispatch(commandBuffer, program, environment, sampler, output, brdfStorageView, descriptorSets,
                new BakeConstants { Mode = 0, Face = face, Extent = IrradianceSize });
        }

        for (uint mip = 0; mip < PrefilteredMipLevels; mip++)
        {
            uint extent = Math.Max(1u, PrefilteredSize >> checked((int)mip));
            for (uint face = 0; face < 6; face++)
            {
                ITextureView output = CreateStorageView(device, prefiltered, mip, face);
                storageViews.Add(output);
                Dispatch(commandBuffer, program, environment, sampler, output, brdfStorageView, descriptorSets,
                    new BakeConstants
                    {
                        Mode = 1,
                        Face = face,
                        Extent = extent,
                        Roughness = mip / (float)(PrefilteredMipLevels - 1),
                    });
            }
        }

        ITextureView dummyCubeView = storageViews[0];
        Dispatch(commandBuffer, program, environment, sampler, dummyCubeView, brdfStorageView, descriptorSets,
            new BakeConstants { Mode = 2, Extent = BrdfSize });

        commandBuffer.ImageBarrier(irradiance, ImageLayout.General, ImageLayout.ShaderReadOnlyOptimal,
            PipelineStages.ComputeShader, ResourceAccess.ShaderWrite, PipelineStages.FragmentShader, ResourceAccess.ShaderRead);
        commandBuffer.ImageBarrier(prefiltered, ImageLayout.General, ImageLayout.ShaderReadOnlyOptimal,
            PipelineStages.ComputeShader, ResourceAccess.ShaderWrite, PipelineStages.FragmentShader, ResourceAccess.ShaderRead);
        commandBuffer.ImageBarrier(brdf, ImageLayout.General, ImageLayout.ShaderReadOnlyOptimal,
            PipelineStages.ComputeShader, ResourceAccess.ShaderWrite, PipelineStages.FragmentShader, ResourceAccess.ShaderRead);
        commandBuffer.End();
            device.Submit(commandBuffer);
            return true;
        }
        finally
        {
            foreach (IDescriptorSet set in descriptorSets)
                set.Dispose();
        }
    }

    private static void Dispatch(
        ICommandBuffer commandBuffer,
        IShaderProgram program,
        ITexture environment,
        ISampler sampler,
        ITextureView cubeOutput,
        ITextureView brdfOutput,
        List<IDescriptorSet> descriptorSets,
        BakeConstants constants)
    {
        IDescriptorSet set = program.CreateDescriptorSet(0);
        set.WriteTexture(0, environment);
        set.WriteSampler(1, sampler);
        set.WriteStorageImage(2, cubeOutput);
        set.WriteStorageImage(3, brdfOutput);
        descriptorSets.Add(set);
        commandBuffer.BindDescriptorSet(set, 0);
        BakeConstants pushConstants = constants;
        commandBuffer.PushConstants(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref pushConstants, 1)), ShaderStages.Compute);
        commandBuffer.Dispatch((constants.Extent + 7) / 8, (constants.Extent + 7) / 8);
    }

    private static ITextureView CreateStorageView(IDevice device, ITexture texture, uint mip, uint face) =>
        device.CreateTextureView(texture, new TextureViewDescription
        {
            Type = TextureViewType.Texture2DArray,
            BaseMipLevel = mip,
            MipLevelCount = 1,
            BaseArrayLayer = face,
            ArrayLayerCount = 1,
        });

    private static ITexture CreateCube(IDevice device, uint size, uint mipLevels, bool supportsBake) => device.CreateTexture(new TextureDescription
    {
        Extent = new Size2D((int)size, (int)size),
        Format = TextureFormat.R16G16B16A16Float,
        Usage = TextureUsage.Sampled | TextureUsage.TransferDestination
            | (supportsBake ? TextureUsage.Storage : TextureUsage.None),
        Dimension = TextureDimension.Cube,
        MipLevels = mipLevels,
    });

    private static ITexture CreateTexture2D(IDevice device, uint width, uint height, bool supportsBake) => device.CreateTexture(new TextureDescription
    {
        Extent = new Size2D((int)width, (int)height),
        Format = TextureFormat.R16G16B16A16Float,
        Usage = TextureUsage.Sampled | TextureUsage.TransferDestination
            | (supportsBake ? TextureUsage.Storage : TextureUsage.None),
    });

    private static ITexture CreateEnvironment(IDevice device)
    {
        ITexture texture = device.CreateTexture(new TextureDescription
        {
            Extent = new Size2D((int)EnvironmentSize, (int)EnvironmentSize),
            Format = TextureFormat.R16G16B16A16Float,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            Dimension = TextureDimension.Cube,
            MipLevels = EnvironmentMipLevels,
        });
        try
        {
            List<TextureSubresourceData> subresources = [];
            for (uint mip = 0; mip < EnvironmentMipLevels; mip++)
            {
                uint extent = Math.Max(1u, EnvironmentSize >> checked((int)mip));
                for (uint face = 0; face < 6; face++)
                {
                    byte[] pixels = new byte[checked((int)(extent * extent * 8))];
                    for (uint y = 0; y < extent; y++)
                    for (uint x = 0; x < extent; x++)
                    {
                        Vector3 direction = FaceDirection(face, (x + 0.5f) / extent * 2f - 1f, (y + 0.5f) / extent * 2f - 1f);
                        float horizon = Math.Clamp(direction.Y * 0.5f + 0.5f, 0f, 1f);
                        Vector3 color = Vector3.Lerp(new Vector3(0.11f, 0.12f, 0.14f), new Vector3(0.32f, 0.42f, 0.58f), horizon);
                        Vector3 sunDirection = new Vector3(0.25f, 0.82f, 0.51f).Normalized;
                        float sun = MathF.Pow(Math.Clamp(Vector3.Dot(direction, sunDirection), 0f, 1f), 256f) * 12f;
                        color += new Vector3(sun, sun * 0.88f, sun * 0.68f);
                        int offset = checked((int)((y * extent + x) * 8));
                        WriteHalf(pixels, offset, color.X);
                        WriteHalf(pixels, offset + 2, color.Y);
                        WriteHalf(pixels, offset + 4, color.Z);
                        WriteHalf(pixels, offset + 6, 1f);
                    }
                    subresources.Add(new TextureSubresourceData { MipLevel = mip, ArrayLayer = face, Data = pixels });
                }
            }
            device.UploadTextureSubresources(texture, CollectionsMarshal.AsSpan(subresources));
            return texture;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    private static Vector3 FaceDirection(uint face, float u, float v)
    {
        Vector3 direction = face switch
        {
            0 => new Vector3(1f, -v, -u),
            1 => new Vector3(-1f, -v, u),
            2 => new Vector3(u, 1f, v),
            3 => new Vector3(u, -1f, -v),
            4 => new Vector3(u, -v, 1f),
            _ => new Vector3(-u, -v, -1f),
        };
        return direction.Normalized;
    }

    private static void UploadNeutralFallback(IDevice device, ITexture irradiance, ITexture prefiltered, ITexture brdf)
    {
        UploadSolidTexture(device, irradiance, 0.03f, 0.03f, 0.03f);
        UploadSolidTexture(device, prefiltered, 0.03f, 0.03f, 0.03f);
        UploadSolidTexture(device, brdf, 1f, 0f, 0f);
    }

    private static void UploadSolidTexture(IDevice device, ITexture texture, float red, float green, float blue)
    {
        List<TextureSubresourceData> subresources = [];
        for (uint mip = 0; mip < texture.MipLevels; mip++)
        {
            uint width = Math.Max(1u, texture.Width >> checked((int)mip));
            uint height = Math.Max(1u, texture.Height >> checked((int)mip));
            for (uint layer = 0; layer < texture.ArrayLayers; layer++)
            {
                byte[] pixels = new byte[checked((int)(width * height * 8))];
                for (int offset = 0; offset < pixels.Length; offset += 8)
                {
                    WriteHalf(pixels, offset, red);
                    WriteHalf(pixels, offset + 2, green);
                    WriteHalf(pixels, offset + 4, blue);
                    WriteHalf(pixels, offset + 6, 1f);
                }
                subresources.Add(new TextureSubresourceData { MipLevel = mip, ArrayLayer = layer, Data = pixels });
            }
        }
        device.UploadTextureSubresources(texture, CollectionsMarshal.AsSpan(subresources));
    }

    private static void WriteHalf(Span<byte> destination, int offset, float value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], BitConverter.HalfToUInt16Bits((Half)value));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _ownedSampler?.Dispose();
        foreach (ITextureView view in _storageViews)
            view.Dispose();
        _bakePipeline?.Dispose();
        _bakeProgram?.Dispose();
        BrdfLut.Dispose();
        PrefilteredSpecular.Dispose();
        Irradiance.Dispose();
        if (_ownsEnvironment)
            Environment.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BakeConstants
    {
        public uint Mode;
        public uint Face;
        public uint Extent;
        public float Roughness;
    }
}
