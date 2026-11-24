using Camera = Engine.Rendering.Camera;
using Light = Engine.Rendering.Light;
using System.Text.Json;
using Engine.Assets;
using Engine.Core;
using Engine.Mathematics;
using Engine.Physics;
using Engine.Platform;
using Engine.RHI;
using Engine.Rendering;
using Engine.Scene;
using Engine.Scripting;
using SceneModel = Engine.Scene.Scene;

namespace Engine.Player;

public sealed class PlayerRuntimeModule : IUpdatableModule
{
    public const string LogCategory = "Player";

    private readonly PlayerOptions _options;
    private readonly RhiModule _rhiModule;
    private readonly RendererModule _renderer;
    private readonly PhysicsModule _physics;
    private readonly ScriptingModule _scripting;
    private readonly SceneBehavioursModule _behaviours;
    private readonly string _projectRoot;
    private readonly Dictionary<string, Mesh> _meshes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PhysicsMeshData> _physicsMeshes = new(StringComparer.OrdinalIgnoreCase);
    private AssetDatabase? _assets;
    private SceneModel? _scene;
    private IEngineContext? _context;
    private IWindow? _window;
    private ILogger? _log;

    public string Name => "Player";

    public SceneModel? Scene => _scene;

    public PlayerRuntimeModule(
        PlayerOptions options,
        RhiModule rhiModule,
        RendererModule renderer,
        PhysicsModule physics,
        ScriptingModule scripting,
        SceneBehavioursModule behaviours,
        string projectRoot)
    {
        _options = options;
        _rhiModule = rhiModule;
        _renderer = renderer;
        _physics = physics;
        _scripting = scripting;
        _behaviours = behaviours;
        _projectRoot = Path.GetFullPath(projectRoot);
    }

    public void Initialize(IEngineContext context)
    {
        _context = context;
        _log = context.Logging.CreateLogger(LogCategory);
        _window = context.Services.Resolve<IWindow>();
        _scripting.AlwaysPlay = true;
        _physics.SimulationActive = true;
        _behaviours.SimulationActive = true;
    }

    public void Shutdown()
    {
    }

    private void EnsureScene()
    {
        if (_scene is not null)
            return;
        IRHI? rhi = _rhiModule.Instance;
        if (rhi is null)
            return;

        _assets = new AssetDatabase(rhi.Device, _projectRoot);
        foreach (string key in PrimitiveMeshRegistry.Keys)
        {
            (Vertex[] vertices, uint[] indices) = PrimitiveMeshRegistry.TryGet(key, out (Vertex[], uint[]) primitive) ? primitive : default;
            _meshes[key] = new Mesh(rhi.Device, vertices, indices, key);
            _physicsMeshes[key] = ToPhysicsMeshData(vertices, indices);
        }
        TryRegisterCubeMesh();

        _renderer.MeshResolver = ResolveMesh;
        _renderer.MeshRequestResolver = key => _meshes.TryGetValue(key, out Mesh? mesh) ? mesh : _assets.RequestMesh(key);
        _renderer.MeshImportFailedResolver = key => !_meshes.ContainsKey(key) && _assets.IsMeshImportFailed(key);
        _assets.Diagnostic += message => _log!.Warning(message);
        _physics.MeshDataResolver = ResolvePhysicsMesh;
        _renderer.TextureResolver = ResolveTexture;
        _renderer.MaterialResolver = reference => _assets.Materials.Resolve(reference);
        _renderer.TextureRevisionResolver = (reference, _) => _assets.GetTextureRevision(reference);
        _renderer.ModelPartRequestResolver = (key, part) => _assets.RequestMesh(key, part);

        string? scenePath = _options.ScenePath;
        if (scenePath is null || !File.Exists(scenePath))
            scenePath = ReadLastScene(_projectRoot) ?? FindFirstScene(_projectRoot);
        if (scenePath is not null)
        {
            _scene = SceneModel.Load(scenePath);
            WriteLastScene(_projectRoot, scenePath);
            _log?.Information($"Scene loaded: {Path.GetFileName(scenePath)}");
        }
        else
        {
            _scene = new SceneModel("Untitled");
            _log?.Warning("No scene found in the packaged project; starting empty.");
        }

        _renderer.AttachScene(_scene);
        _physics.AttachScene(_scene);
        _behaviours.AttachScene(_scene);
        _scripting.AttachScene(_scene);
    }

    private void TryRegisterCubeMesh()
    {
        try
        {
            (Vertex[] cubeVertices, uint[] cubeIndices) = ObjModelLoader.Load(_assets!.ResolvePath(Path.Combine("Models", "cube.obj")));
            Vertex[] cubeScaled = [.. cubeVertices.Select(static v => new Vertex { Position = v.Position * 1.4f, Normal = v.Normal, UV = v.UV, Tangent = v.Tangent })];
            _meshes["cube"] = new Mesh(_rhiModule.Instance!.Device, cubeScaled, cubeIndices, "Cube");
            _physicsMeshes["cube"] = ToPhysicsMeshData(cubeScaled, cubeIndices);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            _log?.Debug($"Model 'Models/cube.obj' unavailable: {exception.Message}");
        }
    }

    public void Update(in FrameTime frameTime)
    {
        EnsureScene();
        if (_scene is null)
            return;
        if (_options.Frames > 0 && frameTime.FrameIndex >= (ulong)_options.Frames)
            _context!.RequestExit();
        if (_window is not null)
        {
            Size2D extent = _renderer.ViewTexture is not null
                ? new Size2D((int)_renderer.ViewTexture.Width, (int)_renderer.ViewTexture.Height)
                : _window.ClientSize;
            _renderer.Camera.AspectRatio = (float)extent.Width / Math.Max(1, extent.Height);
        }
    }

    private string? FindFirstScene(string projectRoot)
    {
        try
        {
            return Directory.EnumerateFiles(projectRoot, "*.scene", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadLastScene(string projectRoot)
    {
        try
        {
            string projectFile = Path.Combine(projectRoot, "Project.project");
            string? json = VirtualFileSystem.TryReadAllText(projectFile);
            if (json is null)
                return null;
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("LastScene", out JsonElement value)
                || value.ValueKind != JsonValueKind.String)
                return null;
            string relative = value.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(relative))
                return null;
            string full = Path.Combine(projectRoot, relative);
            return VirtualFileSystem.Exists(full) ? full : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void WriteLastScene(string projectRoot, string scenePath)
    {
        try
        {
            string projectFile = Path.Combine(projectRoot, "Project.project");
            if (!File.Exists(projectFile))
                return;
            string relative = Path.GetRelativePath(projectRoot, scenePath).Replace(Path.DirectorySeparatorChar, '/');
            string json = File.ReadAllText(projectFile);
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
            System.Text.Json.JsonElement cloned = document.RootElement.Clone();
            using MemoryStream stream = new();
            using (System.Text.Json.Utf8JsonWriter writer = new(stream, new System.Text.Json.JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                bool written = false;
                foreach (System.Text.Json.JsonProperty property in cloned.EnumerateObject())
                {
                    if (property.Name == "LastScene")
                    {
                        writer.WriteString("LastScene", relative);
                        written = true;
                    }
                    else
                    {
                        property.WriteTo(writer);
                    }
                }
                if (!written)
                    writer.WriteString("LastScene", relative);
                writer.WriteEndObject();
            }
            File.WriteAllBytes(projectFile, stream.ToArray());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
        }
    }

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
}
