using Engine.Editor.DragDrop;
using Engine.Mathematics;
using Engine.RHI;
using Engine.Rendering;
using ImGuiNET;

namespace Engine.Editor.Panels;

public sealed class ViewportPanel : EditorPanel
{
    private readonly string _title;
    private readonly bool _tracksDesiredExtent;
    private ITexture? _texture;
    private IntPtr _textureId;
    private Size2D _textureSize;
    private System.Numerics.Vector2 _contentSize = new(1280f, 720f);

    public ImDrawListPtr DrawList { get; private set; }

    public override string Title => _title;

    public bool IsRenderVisible { get; private set; }

    public bool IsHovered { get; private set; }

    public bool ImageClicked { get; private set; }

    public System.Numerics.Vector2 ImageMinimum { get; private set; }

    public System.Numerics.Vector2 ImageSize { get; private set; }

    public System.Numerics.Vector2 MouseNormalized { get; private set; }

    public AssetDragPayload? DroppedAsset { get; private set; }

    public Size2D DesiredExtent => new(
        Math.Max(320, (int)_contentSize.X),
        Math.Max(180, (int)_contentSize.Y));

    public ViewportPanel(string title, bool tracksDesiredExtent)
    {
        _title = title;
        _tracksDesiredExtent = tracksDesiredExtent;
    }

    public void SetTexture(ITexture texture, IntPtr textureId)
    {
        if (ReferenceEquals(_texture, texture) && _textureId == textureId)
            return;
        _texture = texture;
        _textureId = textureId;
        _textureSize = new Size2D((int)texture.Width, (int)texture.Height);
    }

    public override void Draw()
    {
        DroppedAsset = null;
        DrawList = default;
        IsRenderVisible = false;
        if (!IsOpen)
        {
            IsHovered = false;
            ImageClicked = false;
            return;
        }
        bool open = IsOpen;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, System.Numerics.Vector2.Zero);
        if (!ImGui.Begin(Title, ref open))
        {
            IsOpen = open;
            IsHovered = false;
            ImageClicked = false;
            ImGui.PopStyleVar();
            ImGui.End();
            return;
        }
        IsOpen = open;
        ImGui.PopStyleVar();
        DrawList = ImGui.GetWindowDrawList();
        System.Numerics.Vector2 available = ImGui.GetContentRegionAvail();
        IsRenderVisible = open && available.X >= 8f && available.Y >= 8f;
        if (_tracksDesiredExtent)
            _contentSize = available;
        if (available.X < 8f || available.Y < 8f || _texture is null)
        {
            IsHovered = ImGui.IsWindowHovered();
            ImageClicked = false;
            ImGui.End();
            return;
        }
        float width = Math.Min(available.X, _textureSize.Width);
        float height = Math.Min(available.Y, _textureSize.Height);
        ImGui.Image(_textureId, new System.Numerics.Vector2(width, height));
        ImageMinimum = ImGui.GetItemRectMin();
        ImageSize = new System.Numerics.Vector2(width, height);
        IsHovered = ImGui.IsItemHovered();
        ImageClicked = IsHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        if (ImGui.IsWindowFocused())
            ImGui.GetWindowDrawList().AddRect(
                ImageMinimum + System.Numerics.Vector2.One,
                ImageMinimum + ImageSize - System.Numerics.Vector2.One,
                ImGui.GetColorU32(EditorTheme.AccentActive));
        if (ImGui.BeginDragDropTarget())
        {
            DroppedAsset = ImGuiDragPayloads.AcceptAsset();
            ImGui.GetWindowDrawList().AddRect(
                ImageMinimum - new System.Numerics.Vector2(1f, 1f),
                ImageMinimum + ImageSize + new System.Numerics.Vector2(1f, 1f),
                ImGui.GetColorU32(EditorTheme.Accent),
                0f,
                ImDrawFlags.None,
                2f);
            ImGui.EndDragDropTarget();
        }
        System.Numerics.Vector2 mouse = ImGui.GetMousePos() - ImageMinimum;
        MouseNormalized = new System.Numerics.Vector2(
            Math.Clamp(mouse.X / MathF.Max(width, 1f), 0f, 1f),
            Math.Clamp(mouse.Y / MathF.Max(height, 1f), 0f, 1f));
        ImGui.End();
    }
}

public sealed class StatsPanel : EditorPanel
{
    private readonly EditorContext _context;
    private readonly RendererModule _renderer;
    private readonly FeatureInfo? _graphicsFeatures;
    private bool _vsync;
    private System.Numerics.Vector4 _skyColor;

    public override string Title => "Stats";

    public event Action<bool>? VSyncChanged;

    public StatsPanel(EditorContext context, RendererModule renderer, bool vsync, FeatureInfo? graphicsFeatures = null)
    {
        _context = context;
        _renderer = renderer;
        _vsync = vsync;
        _graphicsFeatures = graphicsFeatures;
        Vector4 sky = renderer.SkyColor;
        _skyColor = new System.Numerics.Vector4(sky.X, sky.Y, sky.Z, sky.W);
    }

    public override void Draw()
    {
        if (!IsOpen)
            return;
        bool open = IsOpen;
        if (!ImGui.Begin(Title, ref open))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }
        IsOpen = open;
        ImGui.TextUnformatted($"{ImGui.GetIO().Framerate:F0} FPS  |  {_context.Document.Scene.AllObjects.Count} objects  |  {_renderer.Lights.Count} lights");
        ImGui.Separator();
        if (ImGui.CollapsingHeader("Graphics", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (_graphicsFeatures is { } features)
            {
                ImGui.TextUnformatted($"Device: {features.DeviceName}");
                ImGui.TextUnformatted($"Vendor / API: {features.VendorName} / {features.ApiVersion}");
                ImGui.TextUnformatted($"Dedicated memory: {features.DedicatedVideoMemory / (1024.0 * 1024.0):F0} MiB");
                ImGui.TextUnformatted($"Compute / Ray tracing / Mesh shaders: {YesNo(features.SupportsCompute)} / {YesNo(features.SupportsRayTracing)} / {YesNo(features.SupportsMeshShaders)}");
            }
            else
                ImGui.TextUnformatted("Graphics device information unavailable");
            ImGui.TextUnformatted($"Views: Scene {(_renderer.SceneViewVisible ? "on" : "off")}, Game {(_renderer.GameViewVisible ? "on" : "off")}");
        }
        if (ImGui.CollapsingHeader("Geometry", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextUnformatted($"Render items / lights: {_renderer.Items.Count} / {_renderer.Lights.Count}");
            ImGui.TextUnformatted($"Source: {_renderer.SceneSourceVertexCount:N0} vertices / {_renderer.SceneSourceTriangleCount:N0} triangles");
            ImGui.TextUnformatted($"Submitted triangles: Scene {_renderer.SceneDrawnTriangleCount:N0} (LOD ≤ {_renderer.SceneLodLevel}), Game {_renderer.GameDrawnTriangleCount:N0} (LOD ≤ {_renderer.GameLodLevel}), Shadow {_renderer.ShadowDrawnTriangleCount:N0} (LOD0)");
            ImGui.TextUnformatted($"Drawn objects: Scene {_renderer.SceneDrawnObjects}, Game {_renderer.GameDrawnObjects}, Shadow {_renderer.ShadowDrawnObjects}; culled {_renderer.CulledObjects}");
        }
        if (ImGui.CollapsingHeader("Assets and LOD", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextUnformatted($"Pending / failed meshes: {_renderer.LoadingMeshCount} / {_renderer.FailedMeshCount}");
            if (_context.ModelAssets is { } assets)
            {
                ImGui.TextUnformatted($"Import/cache: {assets.LastMeshImportMilliseconds:F1} ms ({(assets.LastMeshCacheHit ? "cache hit" : "source")})");
                ImGui.TextUnformatted($"Mesh GPU upload: {assets.LastMeshUploadMilliseconds:F1} ms");
                ImGui.TextUnformatted($"LOD chains: {assets.LodChainCount} ({assets.GeneratingLodChains} generating, {assets.PendingLodUploads} pending upload)");
                ImGui.TextUnformatted($"LOD generation: {assets.LastLodGenerationMilliseconds:F1} ms ({(assets.LastLodCacheHit ? "cache hit" : "source")})");
                ImGui.TextUnformatted($"Uploaded levels / LOD meshes: {assets.UploadedLodLevels} / {_renderer.LodMeshCount}");
            }
        }
        if (ImGui.CollapsingHeader("Frame CPU timings"))
        {
            ImGui.TextUnformatted($"Scene sync: {_renderer.SceneSyncMilliseconds:F2} ms");
            ImGui.TextUnformatted($"Frame wait: {_renderer.FrameWaitMilliseconds:F2} ms");
            ImGui.TextUnformatted($"Frame setup/upload: {_renderer.UniformUploadMilliseconds:F2} ms");
            ImGui.TextUnformatted($"Record/submit/present: {_renderer.RenderCpuMilliseconds:F2} ms");
        }
        if (ImGui.CollapsingHeader("Render settings"))
        {
            float exposure = _renderer.Exposure;
            if (ImGui.SliderFloat("Exposure", ref exposure, 0.1f, 4f))
                _renderer.Exposure = exposure;
            float lodPixelBudget = _renderer.LodPixelBudget;
            if (ImGui.SliderFloat("LOD pixel budget", ref lodPixelBudget, 0.25f, 8f))
                _renderer.LodPixelBudget = lodPixelBudget;
            float shadowStrength = _renderer.ShadowStrength;
            if (ImGui.SliderFloat("Shadow strength", ref shadowStrength, 0f, 1f))
                _renderer.ShadowStrength = shadowStrength;
            bool vsync = _vsync;
            if (ImGui.Checkbox("VSync", ref vsync))
            {
                _vsync = vsync;
                VSyncChanged?.Invoke(vsync);
            }
            if (ImGui.ColorEdit4("Sky color", ref _skyColor))
                _renderer.SkyColor = new Vector4(_skyColor.X, _skyColor.Y, _skyColor.Z, _skyColor.W);
        }
        ImGui.End();
    }

    private static string YesNo(bool value) => value ? "yes" : "no";
}
