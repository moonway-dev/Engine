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
using Engine.Scripting;

namespace Basic3D;

internal static class Program
{
    internal static int Main(string[] args)
    {
        SampleOptions options = SampleOptions.Parse(args);
        RhiModule rhiModule = CreateRhiModule(options);
        RendererModule renderer = new(rhiModule, Path.Combine(AppContext.BaseDirectory, "Shaders"));
        PhysicsModule physics = new();
        string projectRoot = ResolveProjectRoot();
        ScriptingModule scripting = new(Path.Combine(projectRoot, "Scripts"))
{
    Physics = physics,
};
        Basic3DModule module = new(options, rhiModule, renderer, physics, scripting);

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
            .AddModule(scripting)
            .AddModule(physics)
            .AddModule(module)
            .AddModule(renderer);
        if (options.Editor)
        {
            EditorModule editor = new(
                rhiModule,
                renderer,
                Path.Combine(AppContext.BaseDirectory, "Shaders"),
                Path.Combine(AppContext.BaseDirectory, "Assets"));
            editor.ScriptRuntime = scripting;
            builder.AddModule(editor);
            module.SetEditor(editor);
        }
        Application application = builder.Build();
        application.Run();
        return application.Context.ExitCode;
    }

    internal static string ResolveProjectRoot()
    {
        List<string> roots =
        [
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
        ];
        string? current = Directory.GetCurrentDirectory();
        for (int i = 0; i < 6 && current is not null; i++)
        {
            roots.Add(current);
            current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar));
        }
        List<string> candidates = [];
        foreach (string root in roots)
            candidates.Add(Path.Combine(root, "Projects", "SampleProject"));
        foreach (string candidate in candidates)
        {
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "Models", "cube.obj")))
                return candidate;
        }
        throw new DirectoryNotFoundException(
            "Projects/SampleProject with Models/cube.obj not found. Searched: " + string.Join(", ", candidates.Select(c => Path.GetFullPath(c))));
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
    PhysicsModule physics,
    ScriptingModule scripting) : IUpdatableModule
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
    private readonly Dictionary<string, PhysicsMeshData> _physicsMeshes = new(StringComparer.OrdinalIgnoreCase);
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
        editor.Physics = physics;
        editor.SceneHost.ActiveSceneChanged += (_, args) =>
        {
            if (args.Current is not null)
            {
                _scene = args.Current;
                _cameraObject = null;
                physics.AttachScene(args.Current);
                scripting.AttachScene(args.Current);
            }
        };
    }

    public void Initialize(IEngineContext context)
    {
        _context = context;
        _log = context.Logging.CreateLogger(Name);
        _window = context.Services.Resolve<IWindow>();
        _screenshotPath = options.ScreenshotPath;
        scripting.AlwaysPlay = !options.Editor;
        physics.SimulationActive = !options.Editor;
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
        _physicsMeshes["ground"] = ToPhysicsMeshData(groundVertices, groundIndices);
        _assets = new AssetDatabase(device, ResolveAssetsDirectory());
        (Vertex[] cubeVertices, uint[] cubeIndices) = ObjModelLoader.Load(_assets.ResolvePath(Path.Combine("Models", "cube.obj")));
        Vertex[] cubeScaled = [.. cubeVertices.Select(static v => new Vertex { Position = v.Position * 1.4f, Normal = v.Normal, UV = v.UV, Tangent = v.Tangent })];
        _meshes["cube"] = new Mesh(device, cubeScaled, cubeIndices, "Cube");
        _physicsMeshes["cube"] = ToPhysicsMeshData(cubeScaled, cubeIndices);
        (Vertex[] sphereVertices, uint[] sphereIndices) = MeshPrimitives.Sphere(1f, 32, 20);
        _meshes["sphere"] = new Mesh(device, sphereVertices, sphereIndices, "Sphere");
        _physicsMeshes["sphere"] = ToPhysicsMeshData(sphereVertices, sphereIndices);

        if (options.ScenePath is not null && File.Exists(options.ScenePath))
        {
            _scene = Scene.Load(options.ScenePath);
        }
        else
        {
            string defaultScene = Path.Combine(ResolveAssetsDirectory(), "Scenes", "PhysicsPlayground.scene");
            _scene = File.Exists(defaultScene)
                ? Scene.Load(defaultScene)
                : new Scene("Untitled");
        }

        _cameraObject = _scene.EnumerateHierarchy().FirstOrDefault(gameObject => gameObject.GetComponent<CameraComponent>() is not null);
        renderer.MeshResolver = ResolveMesh;
        if (Environment.GetEnvironmentVariable("ENGINE_NOGI") == "1")
            renderer.RayTracingGiEnabled = false;
        if (Environment.GetEnvironmentVariable("ENGINE_NOREFL") == "1")
            renderer.RayTracingReflectionsEnabled = false;
        if (Environment.GetEnvironmentVariable("ENGINE_NOSHADOW") == "1")
            renderer.RayTracingShadowsEnabled = false;
        renderer.MeshRequestResolver = key => _meshes.TryGetValue(key, out Mesh? mesh) ? mesh : _assets.RequestMesh(key);
        renderer.MeshImportFailedResolver = key => !_meshes.ContainsKey(key) && _assets.IsMeshImportFailed(key);
        renderer.ModelPartRequestResolver = (key, part) => _assets.RequestMesh(key, part);
        _assets.Diagnostic += message => _log!.Warning(message);
        physics.MeshDataResolver = ResolvePhysicsMesh;
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
        scripting.AttachScene(_scene);
        _editor?.AttachScene(_scene);
    }

    private static string ResolveAssetsDirectory() => Program.ResolveProjectRoot();

    private Mesh ResolveMesh(string key)
    {
        if (_meshes.TryGetValue(key, out Mesh? mesh))
            return mesh;
        if (_assets is null)
            throw new InvalidOperationException("Asset database is not initialized.");
        return _assets.LoadMesh(key);
    }

    private PhysicsMeshData? ResolvePhysicsMesh(string key)
    {
        if (_physicsMeshes.TryGetValue(key, out PhysicsMeshData? primitive))
            return primitive;
        Mesh? assetMesh = _assets?.RequestMesh(key);
        return assetMesh?.GetPhysicsData();
    }

    private static PhysicsMeshData ToPhysicsMeshData(Vertex[] vertices, uint[] indices)
    {
        Vector3[] positions = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
            positions[i] = vertices[i].Position;
        return new PhysicsMeshData(positions, indices);
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

        if (options.Frames > 0 && frameTime.FrameIndex >= (ulong)options.Frames)
            _context!.RequestExit();

        if (_screenshotPath is not null && frameTime.FrameIndex >= 90 && renderer.SwapchainTargetTexture is not null)
        {
            _rhi!.WaitIdle();
            ITexture screenshotTexture = renderer.DebugPresentTexture!;
            byte[]? pixels = _rhi.ReadTexturePixels(screenshotTexture, renderer.DebugPresentLayout);
            if (pixels is not null)
            {
                WritePpm(_screenshotPath, (int)screenshotTexture.Width, (int)screenshotTexture.Height, pixels);
                _log!.Information($"Screenshot saved to {Path.GetFullPath(_screenshotPath)}");
            }
            _screenshotPath = null;
            _context!.RequestExit();
        }
    }

    private void HandleInput()
    {
        if (_editor is null && _window!.Keyboard.IsPressed(KeyCode.Escape))
            _context!.RequestExit();

        bool editorMode = _editor is not null;
        bool viewportOwnsMouse = editorMode && (!_editor!.ViewportHovered || _editor.SceneViewMouseCaptured);

        if (editorMode)
        {
            HandleFlyInput();
            return;
        }

        IWindow window = _window!;
        if (!viewportOwnsMouse && (window.Mouse.IsDown(MouseButton.Right) || window.Mouse.IsDown(MouseButton.Left)))
        {
            if (!_mouseCaptured)
            {
                _mouseCaptured = true;
                _lastMousePosition = window.Mouse.Position;
            }
            Vector2 delta = window.Mouse.Position - _lastMousePosition;
            _orbitYaw -= delta.X * 0.005f;
            _orbitPitch = MathUtil.Clamp(_orbitPitch - delta.Y * 0.005f, -MathUtil.HalfPi + 0.1f, MathUtil.HalfPi - 0.1f);
            _lastMousePosition = window.Mouse.Position;
        }
        else
        {
            _mouseCaptured = false;
        }
        if (!viewportOwnsMouse && window.Mouse.ScrollDelta.Y != 0f)
            _orbitDistance = MathUtil.Clamp(_orbitDistance - window.Mouse.ScrollDelta.Y * 0.7f, 3f, 30f);
    }

    private void HandleFlyInput()
    {
        IWindow window = _window!;
        bool overScene = _editor!.ViewportHovered;
        bool sceneFocused = _editor.SceneViewFocused;
        bool hasSceneInput = overScene && _editor.ApplicationFocused;
        bool canUseKeyboard = _editor.ApplicationFocused && sceneFocused && !_editor.WantTextInput;
        KeyboardState keyboard = window.Keyboard;
        MouseState mouse = window.Mouse;
        bool looking = _editor.ApplicationFocused
            && mouse.IsDown(MouseButton.Right)
            && (_mouseCaptured || (overScene && sceneFocused && !_editor.SceneViewMouseCaptured));

        if (looking)
        {
            if (!_mouseCaptured)
            {
                _mouseCaptured = true;
                window.CursorMode = CursorMode.Disabled;
                window.ResetMouseDelta();
            }
            Vector2 delta = mouse.Delta;
            _flyYaw -= delta.X * 0.004f;
            _flyPitch = MathUtil.Clamp(_flyPitch - delta.Y * 0.004f, -MathUtil.HalfPi + 0.05f, MathUtil.HalfPi - 0.05f);
        }
        else
        {
            _mouseCaptured = false;
            if (window.CursorMode != CursorMode.Normal)
                window.CursorMode = CursorMode.Normal;
        }

        if (!hasSceneInput)
            return;

        if (mouse.ScrollDelta.Y != 0f && !_editor.SceneViewMouseCaptured && !_editor.WantCaptureMouse)
            _flySpeed = MathUtil.Clamp(_flySpeed + mouse.ScrollDelta.Y * 0.5f, 0.5f, 40f);
        if (!canUseKeyboard)
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

    }

    private static Quaternion EulerToQuaternion(float yaw, float pitch)
    {
        return Quaternion.FromAxisAngle(Vector3.UnitY, yaw)
            * Quaternion.FromAxisAngle(Vector3.UnitX, pitch);
    }

    private void UpdateCamera(FrameTime frameTime)
    {
        _lastFrameDelta = frameTime.DeltaTime;
        _cameraObject = _scene?.EnumerateHierarchy().FirstOrDefault(gameObject => gameObject.GetComponent<CameraComponent>() is not null);
        Transform? cameraTransform = _cameraObject?.Transform;
        if (_editor is not null)
        {
            if (!_flyInitialized)
            {
                _flyInitialized = true;
                _flyPosition = cameraTransform?.Position ?? new Vector3(0f, 2.5f, 8f);
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
        else if (cameraTransform is not null)
        {
            float cosPitch = MathF.Cos(_orbitPitch);
            Vector3 orbitPosition = new(
                MathF.Sin(_orbitYaw) * cosPitch * _orbitDistance,
                MathF.Sin(_orbitPitch) * _orbitDistance,
                MathF.Cos(_orbitYaw) * cosPitch * _orbitDistance + 0.8f);
            cameraTransform.Position = orbitPosition;
            cameraTransform.LookAt(new Vector3(0f, 0.4f, 0f), Vector3.Up);
        }

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
