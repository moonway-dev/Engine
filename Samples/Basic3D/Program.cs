using Engine.Assets;
using Engine.Backends.Null;
using Engine.Physics;
using Engine.Backends.Vulkan;
using Engine.Core;
using Engine.Editor;
using Engine.Mathematics;
using Engine.Platform;
using Engine.RHI;
using Engine.Rendering;
using Engine.Scene;

namespace Basic3D;

internal static class Program
{
    internal static int Main(string[] args)
    {
        SampleOptions options = SampleOptions.Parse(args);
        RhiModule rhiModule = CreateRhiModule(options);
        RendererModule renderer = new(rhiModule, Path.Combine(AppContext.BaseDirectory, "Shaders"));
        PhysicsModule physics = new();
        Basic3DModule module = new(options, rhiModule, renderer, physics);

        ApplicationBuilder builder = ApplicationBuilder
            .Create()
            .AddLoggerSink(new ConsoleLogSink(LogLevel.Debug))
            .AddLoggerSink(new FileLogSink(Path.Combine("Logs"), "basic3d"))
            .AddModule(new PlatformModule(new WindowDescription
            {
                Title = "Engine",
                Width = options.Width,
                Height = options.Height,
            }))
            .AddModule(rhiModule)
            .AddModule(renderer)
            .AddModule(physics)
            .AddModule(module);
        if (options.Editor)
        {
            EditorModule editor = new(
                rhiModule,
                renderer,
                Path.Combine(AppContext.BaseDirectory, "Shaders"),
                Path.Combine(AppContext.BaseDirectory, "Assets"));
            builder.AddModule(editor);
            module.SetEditor(editor);
        }
        Application application = builder.Build();
        application.Run();
        return application.Context.ExitCode;
    }

    private static RhiModule CreateRhiModule(SampleOptions options)
    {
        RHIConfiguration configuration = new()
        {
            ApplicationName = "Basic3D",
            EnableValidation = options.Validation,
            VSync = options.VSync,
        };
        if (options.RequestedBackend == GraphicsBackend.Null)
            return new RhiModule(new NullRHIFactory(), configuration);
        VulkanRHIFactory vulkanFactory = new();
        if (!vulkanFactory.IsSupported())
            return new RhiModule(new NullRHIFactory(), configuration);
        return new RhiModule(vulkanFactory, configuration);
    }
}

internal sealed class SampleOptions
{
    public int Width { get; private set; } = 1600;
    public int Height { get; private set; } = 900;
    public int Frames { get; private set; }
    public GraphicsBackend RequestedBackend { get; private set; } = GraphicsBackend.Vulkan;
    public bool Validation { get; private set; }

    public bool Editor { get; private set; }
    public bool VSync { get; private set; } = true;
    public string? ScenePath { get; private set; }
    public string? ScreenshotPath { get; private set; }

    public static SampleOptions Parse(string[] args)
    {
        SampleOptions options = new();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--width":
                    options.Width = ParseInt(args, ref i, options.Width);
                    break;
                case "--height":
                    options.Height = ParseInt(args, ref i, options.Height);
                    break;
                case "--frames":
                    options.Frames = ParseInt(args, ref i, options.Frames);
                    break;
                case "--scene":
                    if (i + 1 < args.Length)
                        options.ScenePath = args[++i];
                    break;
                case "--screenshot":
                    if (i + 1 < args.Length)
                        options.ScreenshotPath = args[++i];
                    break;
                case "--backend":
                    if (i + 1 < args.Length && args[++i].Equals("null", StringComparison.OrdinalIgnoreCase))
                        options.RequestedBackend = GraphicsBackend.Null;
                    break;
                case "--validation":
                    options.Validation = true;
                    break;
                case "--editor":
                    options.Editor = true;
                    break;
                case "--no-vsync":
                    options.VSync = false;
                    break;
            }
        }
        return options;
    }

    private static int ParseInt(string[] args, ref int index, int fallback)
    {
        if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out int value))
            return fallback;
        index++;
        return value;
    }
}

internal sealed class Basic3DModule(
    SampleOptions options,
    RhiModule rhiModule,
    RendererModule renderer,
    PhysicsModule physics) : IUpdatableModule
{
    private IEngineContext? _context;
    private IWindow? _window;
    private IRHI? _rhi;
    private ILogger? _log;
    private EditorModule? _editor;
    private Vector2 _lastMousePosition;
    private bool _mouseCaptured;
    private float _orbitYaw = MathUtil.DegToRad * 35f;
    private float _orbitPitch = MathUtil.DegToRad * 25f;
    private float _flyYaw = MathUtil.DegToRad * 35f;
    private float _flyPitch = MathUtil.DegToRad * 25f;
    private float _flySpeed = 4f;
    private float _lastFrameDelta = 1f / 60f;
    private bool _flyInitialized;
    private Vector3 _flyPosition;
    private float _orbitDistance = 9f;
    private readonly List<GameObject> _rotatingObjects = [];
    private readonly Dictionary<string, Mesh> _meshes = new(StringComparer.OrdinalIgnoreCase);
    private AssetDatabase? _assets;
    private Scene? _scene;
    private GameObject? _cameraObject;
    private string? _screenshotPath;
    private long _benchmarkStart;
    private int _benchmarkFrames;
    private bool _benchmarkReported;
    private int _lastLoggedLodLevels = -1;

    public string Name => "Basic3D";

    public void SetEditor(EditorModule editor)
    {
        _editor = editor;
        editor.SceneHost.ActiveSceneChanged += (_, args) =>
        {
            if (args.Current is not null)
                physics.AttachScene(args.Current);
        };
    }

    public void Initialize(IEngineContext context)
    {
        _context = context;
        _log = context.Logging.CreateLogger(Name);
        _window = context.Services.Resolve<IWindow>();
        _screenshotPath = options.ScreenshotPath;
    }

    private void EnsureScene()
    {
        if (_scene is not null)
            return;
        _rhi = rhiModule.Instance;
        if (_rhi is null)
            return;
        IDevice device = _rhi.Device;

        (Vertex[] groundVertices, uint[] groundIndices) = MeshPrimitives.Plane(24f, 24f);
        _meshes["ground"] = new Mesh(device, groundVertices, groundIndices, "Ground");
        _assets = new AssetDatabase(device, ResolveAssetsDirectory());
        (Vertex[] cubeVertices, uint[] cubeIndices) = ObjModelLoader.Load(_assets.ResolvePath(Path.Combine("Models", "cube.obj")));
        _meshes["cube"] = new Mesh(device, [.. cubeVertices.Select(static v => new Vertex { Position = v.Position * 1.4f, Normal = v.Normal, UV = v.UV, Tangent = v.Tangent })], cubeIndices, "Cube");
        (Vertex[] sphereVertices, uint[] sphereIndices) = MeshPrimitives.Sphere(1f, 32, 20);
        _meshes["sphere"] = new Mesh(device, sphereVertices, sphereIndices, "Sphere");

        if (options.ScenePath is not null && File.Exists(options.ScenePath))
        {
            _scene = Scene.Load(options.ScenePath);
        }
        else
        {
            _scene = BuildScene();
            string savePath = Path.Combine("Assets", "Scenes", "main.scene");
            _scene.Save(savePath);
        }

        _cameraObject = _scene.EnumerateHierarchy().FirstOrDefault(gameObject => gameObject.GetComponent<CameraComponent>() is not null);
        renderer.MeshResolver = ResolveMesh;
        renderer.MeshRequestResolver = key => _meshes.TryGetValue(key, out Mesh? mesh) ? mesh : _assets.RequestMesh(key);
        renderer.MeshImportFailedResolver = key => !_meshes.ContainsKey(key) && _assets.IsMeshImportFailed(key);
        renderer.ModelPartRequestResolver = (key, part) => _assets.RequestMesh(key, part);
        _assets.Diagnostic += message => _log!.Warning(message);
        renderer.TextureResolver = ResolveTexture;
        renderer.MaterialResolver = reference => _assets.Materials.Resolve(reference);
        renderer.TextureRevisionResolver = (reference, _) => _assets.GetTextureRevision(reference);
        if (_editor is not null)
        {
            _editor.MaterialStore = _assets.Materials;
            _editor.ModelAssets = _assets;
        }
        renderer.AttachScene(_scene);
        physics.AttachScene(_scene);
        _editor?.AttachScene(_scene);
    }

    private static string ResolveAssetsDirectory()
    {
        List<string> candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Assets"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets"),
        ];
        string? current = Directory.GetCurrentDirectory();
        for (int i = 0; i < 4 && current is not null; i++)
        {
            candidates.Add(Path.Combine(current, "Assets"));
            current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar));
        }
        foreach (string candidate in candidates)
        {
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "Models", "cube.obj")))
                return candidate;
        }
        throw new DirectoryNotFoundException(
            "Assets directory with Models/cube.obj not found. Searched: " + string.Join(", ", candidates.Select(c => Path.GetFullPath(c))));
    }

    private Mesh ResolveMesh(string key)
    {
        if (_meshes.TryGetValue(key, out Mesh? mesh))
            return mesh;
        if (_assets is null)
            throw new InvalidOperationException("Asset database is not initialized.");
        return _assets.LoadMesh(key);
    }

    private ITexture? ResolveTexture(AssetReference reference, MaterialTextureSlot slot)
    {
        if (_assets is null || reference.IsEmpty)
            return null;
        TextureSemantic semantic = slot switch
        {
            MaterialTextureSlot.Albedo => TextureSemantic.Albedo,
            MaterialTextureSlot.Metallic => TextureSemantic.Metallic,
            MaterialTextureSlot.Roughness => TextureSemantic.Roughness,
            MaterialTextureSlot.Normal => TextureSemantic.Normal,
            MaterialTextureSlot.AmbientOcclusion => TextureSemantic.AmbientOcclusion,
            MaterialTextureSlot.Emission => TextureSemantic.Emission,
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
        };
        try
        {
            return _assets.LoadTexture(reference.Key, semantic);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            _log?.Warning($"Texture '{reference}' could not be loaded: {exception.Message}");
            return null;
        }
    }

    private Scene BuildScene()
    {
        Scene scene = new("Main");

        GameObject ground = scene.CreateObject("Ground");
        ground.Transform.Position = new Vector3(0f, -0.75f, 0f);
        MeshRendererComponent groundRenderer = ground.AddComponent<MeshRendererComponent>();
        groundRenderer.ResourceKey = "ground";
        RigidBodyComponent groundBody = ground.AddComponent<RigidBodyComponent>();
        groundBody.Kind = RigidBodyKind.Static;
        groundBody.Shape = ColliderShape.Box;
        groundBody.Size = new Vector3(24f, 0.25f, 24f);
        groundBody.Center = new Vector3(0f, -0.125f, 0f);
        groundRenderer.AlbedoColor = new Vector3(1f, 0.1f, 0.1f);
        groundRenderer.Metallic = 0.05f;
        groundRenderer.Roughness = 0.85f;
        groundRenderer.UvScale = new Vector2(10f, 10f);

        GameObject metalCube = scene.CreateObject("MetalCube");
        metalCube.Transform.Position = new Vector3(0f, 0.6f, 0f);
        MeshRendererComponent metalRenderer = metalCube.AddComponent<MeshRendererComponent>();
        metalRenderer.ResourceKey = "cube";
        metalRenderer.AlbedoColor = new Vector3(0.85f, 0.45f, 0.2f);
        metalRenderer.Metallic = 0.95f;
        metalRenderer.Roughness = 0.25f;
        _rotatingObjects.Add(metalCube);

        GameObject physicsCrate = scene.CreateObject("PhysicsCrate");
        physicsCrate.Transform.Position = new Vector3(0.4f, 4.5f, 0.6f);
        MeshRendererComponent crateRenderer = physicsCrate.AddComponent<MeshRendererComponent>();
        crateRenderer.ResourceKey = "cube";
        crateRenderer.AlbedoColor = new Vector3(0.9f, 0.75f, 0.3f);
        crateRenderer.Metallic = 0.1f;
        crateRenderer.Roughness = 0.7f;
        RigidBodyComponent crateBody = physicsCrate.AddComponent<RigidBodyComponent>();
        crateBody.Kind = RigidBodyKind.Dynamic;
        crateBody.Shape = ColliderShape.Box;
        crateBody.Size = new Vector3(1.4f, 1.4f, 1.4f);
        crateBody.Mass = 2f;

        GameObject plasticSphere = scene.CreateObject("PlasticSphere");
        plasticSphere.Transform.Position = new Vector3(-2.6f, 0.4f, 0.8f);
        MeshRendererComponent plasticRenderer = plasticSphere.AddComponent<MeshRendererComponent>();
        plasticRenderer.ResourceKey = "sphere";
        plasticRenderer.AlbedoColor = new Vector3(0.2f, 0.5f, 0.9f);
        plasticRenderer.Roughness = 0.45f;
        _rotatingObjects.Add(plasticSphere);

        GameObject roughCube = scene.CreateObject("RoughCube");
        roughCube.Transform.Position = new Vector3(2.6f, 0.4f, 0.6f);
        MeshRendererComponent roughRenderer = roughCube.AddComponent<MeshRendererComponent>();
        roughRenderer.ResourceKey = "cube";
        roughRenderer.AlbedoColor = new Vector3(0.35f, 0.85f, 0.4f);
        roughRenderer.Metallic = 0.1f;
        roughRenderer.Roughness = 0.8f;
        _rotatingObjects.Add(roughCube);

        GameObject glowingSphere = scene.CreateObject("GlowingSphere");
        glowingSphere.Transform.Position = new Vector3(0.8f, 1.9f, -1.8f);
        glowingSphere.Transform.LocalScale = new Vector3(0.55f, 0.55f, 0.55f);
        MeshRendererComponent glowRenderer = glowingSphere.AddComponent<MeshRendererComponent>();
        glowRenderer.ResourceKey = "sphere";
        glowRenderer.AlbedoColor = new Vector3(0.9f, 0.75f, 0.25f);
        glowRenderer.Roughness = 0.4f;
        glowRenderer.EmissiveIntensity = 1.6f;

        GameObject sun = scene.CreateObject("Sun");
        sun.Transform.LocalRotation = Quaternion.LookRotation(new Vector3(0.55f, -1f, 0.35f).Normalized, Vector3.Up);
        LightComponent sunLight = sun.AddComponent<LightComponent>();
        sunLight.Kind = LightKind.Directional;
        sunLight.Color = new Vector3(1f, 0.97f, 0.92f);
        sunLight.Intensity = 3.2f;

        GameObject blueLamp = scene.CreateObject("BlueLamp");
        blueLamp.Transform.Position = new Vector3(-3.2f, 2.2f, 1.6f);
        LightComponent blueLight = blueLamp.AddComponent<LightComponent>();
        blueLight.Kind = LightKind.Point;
        blueLight.Color = new Vector3(0.35f, 0.55f, 1f);
        blueLight.Intensity = 14f;
        blueLight.Range = 12f;

        GameObject redLamp = scene.CreateObject("RedLamp");
        redLamp.Transform.Position = new Vector3(3.4f, 1.8f, -1.2f);
        LightComponent redLight = redLamp.AddComponent<LightComponent>();
        redLight.Kind = LightKind.Point;
        redLight.Color = new Vector3(1f, 0.4f, 0.3f);
        redLight.Intensity = 10f;
        redLight.Range = 10f;

        GameObject cameraObject = scene.CreateObject("MainCamera");
        cameraObject.Transform.Position = new Vector3(6f, 4f, 8f);
        CameraComponent camera = cameraObject.AddComponent<CameraComponent>();
        camera.FieldOfViewY = MathUtil.DegToRad * 55f;
        camera.NearPlane = 0.1f;
        camera.FarPlane = 500f;
        _cameraObject = cameraObject;

        return scene;
    }

    public void Update(in FrameTime frameTime)
    {
        EnsureScene();
        if (_assets is { } assets && assets.UploadedLodLevels != _lastLoggedLodLevels)
        {
            _lastLoggedLodLevels = assets.UploadedLodLevels;
            _log!.Information($"LOD levels uploaded: {assets.UploadedLodLevels}; chains {assets.LodChainCount}, generating {assets.GeneratingLodChains}, pending {assets.PendingLodUploads}, generation {assets.LastLodGenerationMilliseconds:F0} ms ({(assets.LastLodCacheHit ? "cache" : "source")})");
        }
        if (_scene is null)
            return;

        HandleInput();
        UpdateCamera(frameTime);
        if (Environment.GetEnvironmentVariable("ENGINE_MESH_BENCHMARK") == "1" && renderer.SceneSourceTriangleCount > 1000000)
        {
            _benchmarkFrames++;
            if (_benchmarkFrames == 31)
                _benchmarkStart = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_benchmarkFrames == 151 && !_benchmarkReported)
            {
                double milliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(_benchmarkStart).TotalMilliseconds / 120;
                _log!.Information($"Mesh benchmark: source {renderer.SceneSourceVertexCount} vertices/{renderer.SceneSourceTriangleCount} triangles; submitted Scene/Game/Shadow {renderer.SceneDrawnTriangleCount}/{renderer.GameDrawnTriangleCount}/{renderer.ShadowDrawnTriangleCount} triangles; max LOD Scene/Game {renderer.SceneLodLevel}/{renderer.GameLodLevel}; views {renderer.SceneViewVisible}/{renderer.GameViewVisible}, drawn {renderer.DrawnObjects}, culled {renderer.CulledObjects}; {milliseconds:F2} ms/frame, {1000 / milliseconds:F2} FPS; sync {renderer.SceneSyncMilliseconds:F2} ms, wait {renderer.FrameWaitMilliseconds:F2} ms, record {renderer.RenderCpuMilliseconds:F2} ms; import/cache {_assets!.LastMeshImportMilliseconds:F2} ms, cache hit {_assets.LastMeshCacheHit}; upload {_assets.LastMeshUploadMilliseconds:F2} ms; LOD chains {_assets.LodChainCount}, levels uploaded {_assets.UploadedLodLevels}, generating {_assets.GeneratingLodChains}, pending {_assets.PendingLodUploads}, LOD meshes {renderer.LodMeshCount}");
                _benchmarkReported = true;
                _context!.RequestExit();
            }
        }

        for (int i = 0; i < _rotatingObjects.Count; i++)
        {
            GameObject gameObject = _rotatingObjects[i];
            gameObject.Transform.LocalRotation =
                Quaternion.FromAxisAngle(Vector3.Up, frameTime.TotalTime * 0.8f) *
                Quaternion.FromAxisAngle(Vector3.Right, frameTime.TotalTime * 0.3f);
            if (i == 1)
            {
                float angle = frameTime.TotalTime * 0.6f;
                gameObject.Transform.Position = new Vector3(
                    -2.6f + MathF.Sin(angle) * 0.4f,
                    0.4f + MathF.Sin(angle * 1.7f) * 0.5f,
                    0.8f + MathF.Cos(angle) * 0.4f);
            }
        }

        if (Environment.GetEnvironmentVariable("ENGINE_SKIP_SCENE") == "1")
        {
            if (frameTime.FrameIndex >= (ulong)Math.Max(5, options.Frames))
                _context!.RequestExit();
            return;
        }
        if (options.Frames > 0 && frameTime.FrameIndex >= (ulong)options.Frames)
            _context!.RequestExit();

        if (_screenshotPath is not null && frameTime.FrameIndex >= 15 && renderer.SwapchainTargetTexture is not null)
        {
            _rhi!.WaitIdle();
            DumpTexture($"{_screenshotPath}.hdr.ppm", renderer.DebugHdrTexture!, renderer.DebugHdrLayout);
            DumpTexture($"{_screenshotPath}.present.ppm", renderer.DebugPresentTexture!, renderer.DebugPresentLayout);
            if (renderer.ViewTexture is not null)
                DumpTexture($"{_screenshotPath}.view.ppm", renderer.ViewTexture, ImageLayout.ShaderReadOnlyOptimal);
            DumpTexture($"{_screenshotPath}.shadow.ppm", renderer.DebugShadowTexture!, renderer.DebugShadowLayout);
            _log!.Information($"Render items at screenshot: {renderer.DebugRenderItemCount}");
            byte[]? pixels = _rhi.ReadSwapchainPixels();
            if (pixels is not null)
            {
                WritePpm(_screenshotPath, (int)_rhi.Swapchain.Extent.Width, (int)_rhi.Swapchain.Extent.Height, pixels);
                _log!.Information($"Screenshot saved to {Path.GetFullPath(_screenshotPath)}");
            }
            _screenshotPath = null;
            _context!.RequestExit();
        }
    }

    private void HandleInput()
    {
        if (_window!.Keyboard.IsPressed(KeyCode.Escape))
            _context!.RequestExit();

        bool editorMode = _editor is not null;
        bool viewportOwnsMouse = editorMode && (!_editor!.ViewportHovered || _editor.WantCaptureMouse);

        if (editorMode)
        {
            HandleFlyInput();
            return;
        }

        if (!viewportOwnsMouse && (_window.Mouse.IsDown(MouseButton.Right) || _window.Mouse.IsDown(MouseButton.Left)))
        {
            if (!_mouseCaptured)
            {
                _mouseCaptured = true;
                _lastMousePosition = _window.Mouse.Position;
            }
            Vector2 delta = _window.Mouse.Position - _lastMousePosition;
            _orbitYaw -= delta.X * 0.005f;
            _orbitPitch = MathUtil.Clamp(_orbitPitch - delta.Y * 0.005f, -MathUtil.HalfPi + 0.1f, MathUtil.HalfPi - 0.1f);
            _lastMousePosition = _window.Mouse.Position;
        }
        else
        {
            _mouseCaptured = false;
        }
        if (!viewportOwnsMouse && _window.Mouse.ScrollDelta.Y != 0f)
            _orbitDistance = MathUtil.Clamp(_orbitDistance - _window.Mouse.ScrollDelta.Y * 0.7f, 3f, 30f);
    }

    private void HandleFlyInput()
    {
        bool overScene = _editor!.SceneViewFocused;
        KeyboardState keyboard = _window!.Keyboard;
        MouseState mouse = _window.Mouse;

        if (overScene && mouse.IsDown(MouseButton.Right))
        {
            if (!_mouseCaptured)
            {
                _mouseCaptured = true;
                _lastMousePosition = mouse.Position;
            }
            Vector2 delta = mouse.Position - _lastMousePosition;
            _flyYaw -= delta.X * 0.004f;
            _flyPitch = MathUtil.Clamp(_flyPitch - delta.Y * 0.004f, -MathUtil.HalfPi + 0.05f, MathUtil.HalfPi - 0.05f);
            _lastMousePosition = mouse.Position;
        }
        else
        {
            _mouseCaptured = false;
        }

        if (!overScene)
            return;

        float speed = _flySpeed * (keyboard.IsDown(KeyCode.LeftShift) ? 3f : 1f);
        Quaternion rotation = EulerToQuaternion(_flyYaw, _flyPitch);
        Vector3 forward = rotation.Rotate(Vector3.Forward);
        Vector3 right = rotation.Rotate(Vector3.Right);
        Vector3 up = Vector3.UnitY;
        Vector3 movement = Vector3.Zero;
        if (keyboard.IsDown(KeyCode.W))
            movement += forward;
        if (keyboard.IsDown(KeyCode.S))
            movement -= forward;
        if (keyboard.IsDown(KeyCode.A))
            movement -= right;
        if (keyboard.IsDown(KeyCode.D))
            movement += right;
        if (keyboard.IsDown(KeyCode.E))
            movement += up;
        if (keyboard.IsDown(KeyCode.Q))
            movement -= up;
        if (movement.Length > MathUtil.Epsilon)
            _flyPosition += movement.Normalized * speed * _lastFrameDelta;

        if (mouse.ScrollDelta.Y != 0f)
            _flySpeed = MathUtil.Clamp(_flySpeed + mouse.ScrollDelta.Y * 0.5f, 0.5f, 40f);
    }

    private static Quaternion EulerToQuaternion(float yaw, float pitch)
    {
        return Quaternion.FromAxisAngle(Vector3.UnitY, yaw)
            * Quaternion.FromAxisAngle(Vector3.UnitX, pitch);
    }

    private Vector3 cameraPosForDebug;

    private void UpdateCamera(FrameTime frameTime)
    {
        if (_cameraObject is null)
            return;
        _lastFrameDelta = frameTime.DeltaTime;
        Transform cameraTransform = _cameraObject.Transform;
        if (_editor is not null)
        {
            if (!_flyInitialized)
            {
                _flyInitialized = true;
                _flyPosition = cameraTransform.Position;
                _flyYaw = MathUtil.DegToRad * 35f;
                _flyPitch = -MathUtil.DegToRad * 25f;
            }
            renderer.EditorCamera.Position = _flyPosition;
            renderer.EditorCamera.Rotation = EulerToQuaternion(_flyYaw, _flyPitch);
            Size2D editorExtent = renderer.ViewTexture is not null
                ? new Size2D((int)renderer.ViewTexture.Width, (int)renderer.ViewTexture.Height)
                : _window!.ClientSize;
            renderer.EditorCamera.AspectRatio = (float)editorExtent.Width / Math.Max(1, editorExtent.Height);
        }
        else
        {
            float cosPitch = MathF.Cos(_orbitPitch);
            Vector3 orbitPosition = new(
                MathF.Sin(_orbitYaw) * cosPitch * _orbitDistance,
                MathF.Sin(_orbitPitch) * _orbitDistance,
                MathF.Cos(_orbitYaw) * cosPitch * _orbitDistance + 0.8f);
            cameraTransform.Position = orbitPosition;
            cameraTransform.LookAt(new Vector3(0f, 0.4f, 0f), Vector3.Up);
        }
        cameraPosForDebug = _editor is null ? cameraTransform.Position : renderer.EditorCamera.Position;

        Size2D viewExtent = renderer.ViewTexture is not null ? new Size2D((int)renderer.ViewTexture.Width, (int)renderer.ViewTexture.Height) : _window!.ClientSize;
        renderer.Camera.AspectRatio = (float)viewExtent.Width / Math.Max(1, viewExtent.Height);
    }

    private static void WritePpm(string path, int width, int height, byte[] bgraPixels)
    {
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 4;
                writer.Write(bgraPixels[index + 2]);
                writer.Write(bgraPixels[index + 1]);
                writer.Write(bgraPixels[index]);
            }
        }
    }

    private void DumpTexture(string path, ITexture texture, ImageLayout layout)
    {
        try
        {
            byte[] pixels = _rhi!.ReadTexturePixels(texture, layout)!;
            if (texture.Format is TextureFormat.D32Float or TextureFormat.D16Unorm)
            {
                int width = (int)texture.Width, height = (int)texture.Height;
                using FileStream stream = File.Create(path);
                using BinaryWriter writer = new(stream);
                writer.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
                for (int i = 0; i < width * height; i++)
                {
                    float depth = texture.Format is TextureFormat.D32Float
                        ? BitConverter.ToSingle(pixels, i * 4)
                        : BitConverter.ToUInt16(pixels, i * 2) / 65535f;
                    byte value = (byte)Math.Clamp(depth * 255f, 0f, 255f);
                    writer.Write(value);
                    writer.Write(value);
                    writer.Write(value);
                }
            }
            else if (texture.Format is TextureFormat.R16G16B16A16Float)
            {
                int width = (int)texture.Width, height = (int)texture.Height;
                using FileStream stream = File.Create(path);
                using BinaryWriter writer = new(stream);
                writer.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
                for (int i = 0; i < width * height; i++)
                {
                    Vector4 value = DecodeHalfPixel(pixels, i * 8);
                    writer.Write(ToByte(value.X));
                    writer.Write(ToByte(value.Y));
                    writer.Write(ToByte(value.Z));
                }
            }
            else
            {
                WritePpm(path, (int)texture.Width, (int)texture.Height, pixels);
            }
            _log!.Information($"Dumped {path}");
        }
        catch (Exception exception)
        {
            _log!.Warning($"Dump {path} failed: {exception.Message}");
        }
    }

    private static float HalfToFloat(ushort half)
    {
        uint sign = (uint)(half >> 15) & 0x1u;
        uint exponent = (uint)(half >> 10) & 0x1Fu;
        uint mantissa = half & 0x3FFu;
        if (exponent == 0)
            return mantissa == 0 ? 0f : MathF.Pow(2f, -14f) * (mantissa / 1024f);
        if (exponent == 31)
            return mantissa == 0 ? float.PositiveInfinity : float.NaN;
        return MathF.Pow(2f, (int)exponent - 15) * (1f + mantissa / 1024f) * (sign == 1 ? -1f : 1f);
    }

    private static Vector4 DecodeHalfPixel(byte[] pixels, int offset)
    {
        ushort Read(int index) => (ushort)(pixels[offset + index] | (pixels[offset + index + 1] << 8));
        return new Vector4(HalfToFloat(Read(0)), HalfToFloat(Read(2)), HalfToFloat(Read(4)), HalfToFloat(Read(6)));
    }

    private static byte ToByte(float value) => (byte)Math.Clamp(value * 255f, 0f, 255f);

    public void Shutdown()
    {
        _rhi?.WaitIdle();
        if (_assets is { } assets)
            _log?.Information($"LOD final: chains {assets.LodChainCount}, generating {assets.GeneratingLodChains}, pending {assets.PendingLodUploads}, uploaded {assets.UploadedLodLevels}, generation {assets.LastLodGenerationMilliseconds:F0} ms ({(assets.LastLodCacheHit ? "cache" : "source")})");
        foreach (Mesh mesh in _meshes.Values)
            mesh.Dispose();
        _meshes.Clear();
        _assets?.Dispose();
        _assets = null;
        _log?.Information("Basic3D stopped");
    }
}
