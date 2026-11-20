using System.Runtime.InteropServices;
using Engine.Core;
using Engine.Mathematics;
using Engine.RHI;
using Engine.Shaders;

namespace Engine.Rendering;

public sealed class AssetPreviewRenderer : IDisposable
{
    public const int PreviewSize = 256;
    private readonly IDevice _device;
    private readonly ImageBasedLightingResources _ibl;
    private readonly IShaderProgram _pbrProgram;
    private readonly IShaderProgram _tonemapProgram;
    private readonly IGraphicsPipeline _pbrPipeline;
    private readonly IGraphicsPipeline _tonemapPipeline;
    private readonly ISampler _sampler;
    private readonly ISampler _shadowSampler;
    private readonly MaterialDefaultTextures _defaults;
    private readonly ITexture _shadow;
    private readonly ITexture _black;
    private readonly IBuffer _shadowBuffer;
    private readonly IBuffer _lightsBuffer;
    private readonly Mesh _sphere;
    private bool _shadowInitialized;
    private bool _disposed;

    public AssetPreviewRenderer(IDevice device, ILogger log, string shaderDirectory, string cacheDirectory, ImageBasedLightingResources ibl)
    {
        _device = device;
        _ibl = ibl;
        SlangShaderCompiler compiler = new(log, cacheDirectory: cacheDirectory);
        _pbrProgram = device.CreateShaderProgram(compiler.CompileProgram(Path.Combine(shaderDirectory, "PBR.slang"), [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")]));
        _tonemapProgram = device.CreateShaderProgram(compiler.CompileProgram(Path.Combine(shaderDirectory, "Tonemap.slang"), [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")]));
        _pbrPipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            Name = "AssetPreviewPBR",
            ShaderProgram = _pbrProgram,
            VertexLayouts = [Vertex.Layout],
            CullMode = CullMode.None,
            ColorAttachmentFormats = [TextureFormat.R16G16B16A16Float],
            DepthAttachmentFormat = TextureFormat.D32Float,
        });
        _tonemapPipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            Name = "AssetPreviewTonemap",
            ShaderProgram = _tonemapProgram,
            VertexLayouts = [],
            CullMode = CullMode.None,
            DepthTest = false,
            DepthWrite = false,
            ColorAttachmentFormats = [TextureFormat.R8G8B8A8Srgb],
        });
        _sampler = device.CreateSampler(new SamplerDescription());
        _shadowSampler = device.CreateSampler(new SamplerDescription { CompareEnabled = true, AddressMode = AddressMode.Clamp });
        _defaults = MaterialDefaultTextures.Create(device);
        _shadow = device.CreateTexture(new TextureDescription { Extent = new Size2D(1, 1), Format = TextureFormat.D32Float, Usage = TextureUsage.Sampled | TextureUsage.DepthAttachment });
        _black = device.CreateTexture(new TextureDescription { Extent = new Size2D(1, 1), Format = TextureFormat.R8G8B8A8UNorm });
        device.UploadTextureData(_black, [0, 0, 0, 255]);
        _shadowBuffer = CreateUniform(new ShadowData { LightIndex = -1 });
        LightsData lights = new() { Count = 3 };
        lights.Lights[0] = new LightData { DirectionOrPosition = new Vector3(-0.5f, -0.8f, -1f).Normalized, Color = new Vector3(1f, 0.96f, 0.9f), Intensity = 3f };
        lights.Lights[1] = new LightData { DirectionOrPosition = new Vector3(1f, -0.2f, 0.5f).Normalized, Color = new Vector3(0.75f, 0.85f, 1f), Intensity = 1.5f };
        lights.Lights[2] = new LightData { DirectionOrPosition = new Vector3(-0.3f, -1f, 0.5f).Normalized, Color = Vector3.One, Intensity = 1f };
        _lightsBuffer = CreateUniform(lights);
        var sphere = MeshPrimitives.Sphere(1f, 48, 32);
        _sphere = new Mesh(device, sphere.Vertices, sphere.Indices, "MaterialPreviewSphere");
    }

    public static (Camera Camera, Matrix4x4 Model) Frame(Vector3 minimum, Vector3 maximum)
    {
        Vector3 center = minimum * 0.5f + maximum * 0.5f;
        Vector3 half = (maximum - minimum) * 0.5f;
        float radius = MathF.Max(0.0001f, half.Length);
        float scale = 1f / radius;
        Matrix4x4 model = Matrix4x4.CreateTRS(-center * scale, Quaternion.Identity, new Vector3(scale));
        Camera camera = new() { AspectRatio = 1f, FieldOfViewY = 35f * MathUtil.DegToRad, NearPlane = 0.01f, FarPlane = 20f };
        Vector3 direction = new Vector3(1f, 0.65f, 1f).Normalized;
        camera.Position = direction;
        camera.LookAt(Vector3.Zero);
        float distance = 1f;
        float tangent = MathF.Tan(camera.FieldOfViewY * 0.5f) * 0.85f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3((i & 1) == 0 ? -half.X : half.X, (i & 2) == 0 ? -half.Y : half.Y, (i & 4) == 0 ? -half.Z : half.Z) * scale;
            float depth = Vector3.Dot(corner, direction);
            float projected = MathF.Max(MathF.Abs(Vector3.Dot(corner, camera.Right)), MathF.Abs(Vector3.Dot(corner, camera.Up)));
            distance = MathF.Max(distance, depth + projected / tangent);
        }
        camera.Position = direction * distance;
        return (camera, model);
    }

    public PreviewJob Prepare(Mesh? mesh, Material material, bool ownsMesh)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new PreviewJob(this, mesh ?? _sphere, material, ownsMesh);
    }

    private void InitializeShadow(ICommandBuffer commands)
    {
        if (_shadowInitialized)
            return;
        commands.ImageBarrier(_shadow, ImageLayout.Undefined, ImageLayout.DepthAttachmentOptimal);
        commands.BeginRendering(new RenderPassInfo
        {
            RenderArea = new Rect(0, 0, 1, 1),
            ColorAttachments = [],
            DepthAttachment = new DepthAttachmentInfo { Texture = _shadow, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearDepth = 1f },
        });
        commands.EndRendering();
        commands.ImageBarrier(_shadow, ImageLayout.DepthAttachmentOptimal, ImageLayout.DepthStencilReadOnlyOptimal, PipelineStages.EarlyDepth | PipelineStages.LateDepth, ResourceAccess.DepthWrite, PipelineStages.FragmentShader, ResourceAccess.ShaderRead);
        _shadowInitialized = true;
    }

    private IBuffer CreateUniform<T>(T value) where T : unmanaged
    {
        IBuffer buffer = _device.CreateBuffer(new BufferDescription { Size = (ulong)Marshal.SizeOf<T>(), Usage = BufferUsage.Uniform | BufferUsage.TransferDestination, Memory = MemoryUsage.CpuToGpu });
        _device.UploadBufferData(buffer, Bytes(value));
        return buffer;
    }

    private static byte[] Bytes<T>(T value) where T : unmanaged => MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)).ToArray();

    public sealed class PreviewJob : IDisposable
    {
        private readonly AssetPreviewRenderer _renderer;
        private readonly Mesh _mesh;
        private readonly bool _ownsMesh;
        private readonly Matrix4x4 _model;
        private readonly ITexture _hdr;
        private readonly ITexture _depth;
        private readonly IBuffer _camera;
        private readonly IBuffer _material;
        private readonly IDescriptorSet _frameSet;
        private readonly IDescriptorSet _materialSet;
        private readonly IDescriptorSet _tonemapSet;
        private readonly List<IDisposable> _owned = [];
        private bool _disposed;
        public ITexture Output { get; }

        internal PreviewJob(AssetPreviewRenderer renderer, Mesh mesh, Material material, bool ownsMesh)
        {
            _renderer = renderer;
            _mesh = mesh;
            _ownsMesh = ownsMesh;
            IDevice device = renderer._device;
            Output = device.CreateTexture(new TextureDescription { Extent = new Size2D(PreviewSize, PreviewSize), Format = TextureFormat.R8G8B8A8Srgb, Usage = TextureUsage.Sampled | TextureUsage.ColorAttachment | TextureUsage.TransferSource });
            _hdr = device.CreateTexture(new TextureDescription { Extent = new Size2D(PreviewSize, PreviewSize), Format = TextureFormat.R16G16B16A16Float, Usage = TextureUsage.Sampled | TextureUsage.ColorAttachment });
            _depth = device.CreateTexture(new TextureDescription { Extent = new Size2D(PreviewSize, PreviewSize), Format = TextureFormat.D32Float, Usage = TextureUsage.DepthAttachment });
            var frame = ReferenceEquals(mesh, renderer._sphere)
                ? Frame(-Vector3.One / MathF.Sqrt(3f), Vector3.One / MathF.Sqrt(3f))
                : Frame(mesh.BoundsMin, mesh.BoundsMax);
            _model = frame.Model;
            _camera = renderer.CreateUniform(new CameraData { ViewProjection = frame.Camera.ViewProjection, PreviousViewProjection = frame.Camera.ViewProjection, CameraPosition = frame.Camera.Position });
            _material = renderer.CreateUniform(material.ToData());
            _frameSet = renderer._pbrProgram.CreateDescriptorSet(0);
            _frameSet.WriteUniformBuffer(0, _camera);
            _frameSet.WriteUniformBuffer(1, renderer._shadowBuffer);
            _frameSet.WriteTexture(2, renderer._shadow);
            _frameSet.WriteSampler(3, renderer._shadowSampler);
            _frameSet.WriteUniformBuffer(4, renderer._lightsBuffer);
            _frameSet.WriteTexture(5, renderer._ibl.Irradiance);
            _frameSet.WriteTexture(6, renderer._ibl.PrefilteredSpecular);
            _frameSet.WriteTexture(7, renderer._ibl.BrdfLut);
            _frameSet.WriteSampler(8, renderer._ibl.Sampler);
            _materialSet = renderer._pbrProgram.CreateDescriptorSet(1);
            MaterialTextureSet textures = material.ResolveTextures(renderer._defaults);
            _materialSet.WriteUniformBuffer(0, _material);
            _materialSet.WriteTexture(1, textures.Albedo);
            _materialSet.WriteTexture(2, textures.Metallic);
            _materialSet.WriteTexture(3, textures.Roughness);
            _materialSet.WriteTexture(4, textures.Normal);
            _materialSet.WriteTexture(5, textures.AmbientOcclusion);
            _materialSet.WriteSampler(6, renderer._sampler);
            _materialSet.WriteTexture(7, textures[MaterialTextureSlot.Emission]);
            _tonemapSet = renderer._tonemapProgram.CreateDescriptorSet(0);
            _tonemapSet.WriteTexture(0, _hdr);
            _tonemapSet.WriteSampler(1, renderer._sampler);
            _tonemapSet.WriteTexture(2, renderer._black);
        }

        public void Own(IDisposable resource) => _owned.Add(resource);

        public void Record(ICommandBuffer commands)
        {
            _renderer.InitializeShadow(commands);
            Rect rect = new(0, 0, PreviewSize, PreviewSize);
            commands.ImageBarrier(_hdr, ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal);
            commands.ImageBarrier(_depth, ImageLayout.Undefined, ImageLayout.DepthAttachmentOptimal);
            commands.BeginRendering(new RenderPassInfo
            {
                RenderArea = rect,
                ColorAttachments = [new ColorAttachmentInfo { Texture = _hdr, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearColor = new Vector4(0.027f, 0.027f, 0.027f, 1f) }],
                DepthAttachment = new DepthAttachmentInfo { Texture = _depth, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.DontCare, ClearDepth = 1f },
            });
            commands.SetViewport(new Viewport(0, 0, PreviewSize, PreviewSize, 0, 1));
            commands.SetScissor(rect);
            commands.BindGraphicsPipeline(_renderer._pbrPipeline);
            commands.BindDescriptorSet(_frameSet, 0);
            commands.BindDescriptorSet(_materialSet, 1);
            commands.PushConstants(Bytes(ModelConstants.FromModel(_model)), ShaderStages.Vertex);
            _mesh.Draw(commands);
            commands.EndRendering();
            commands.ImageBarrier(_hdr, ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal, PipelineStages.ColorOutput, ResourceAccess.ColorWrite, PipelineStages.FragmentShader, ResourceAccess.ShaderRead);
            commands.ImageBarrier(Output, ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal);
            commands.BeginRendering(new RenderPassInfo
            {
                RenderArea = rect,
                ColorAttachments = [new ColorAttachmentInfo { Texture = Output, LoadOp = AttachmentLoadOp.DontCare, StoreOp = AttachmentStoreOp.Store }],
            });
            commands.BindGraphicsPipeline(_renderer._tonemapPipeline);
            commands.BindDescriptorSet(_tonemapSet, 0);
            commands.PushConstants(Bytes(new TonemapConstants { Exposure = 1f }), ShaderStages.Fragment);
            commands.Draw(3);
            commands.EndRendering();
            commands.ImageBarrier(Output, ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal, PipelineStages.ColorOutput, ResourceAccess.ColorWrite, PipelineStages.FragmentShader, ResourceAccess.ShaderRead);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _frameSet.Dispose();
            _materialSet.Dispose();
            _tonemapSet.Dispose();
            _camera.Dispose();
            _material.Dispose();
            _hdr.Dispose();
            _depth.Dispose();
            if (_ownsMesh)
                _mesh.Dispose();
            foreach (IDisposable resource in _owned)
                resource.Dispose();
            _owned.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _sphere.Dispose();
        _lightsBuffer.Dispose();
        _shadowBuffer.Dispose();
        _black.Dispose();
        _shadow.Dispose();
        _defaults.Dispose();
        _shadowSampler.Dispose();
        _sampler.Dispose();
        _pbrPipeline.Dispose();
        _tonemapPipeline.Dispose();
        _pbrProgram.Dispose();
        _tonemapProgram.Dispose();
    }
}
