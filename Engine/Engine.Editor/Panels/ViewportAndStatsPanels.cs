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
    private bool _vsync;
    private System.Numerics.Vector4 _skyColor;

    public override string Title => "Stats";

    public event Action<bool>? VSyncChanged;

    public StatsPanel(EditorContext context, RendererModule renderer, bool vsync)
    {
        _context = context;
        _renderer = renderer;
        _vsync = vsync;
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
        ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.MutedText);
        ImGui.TextUnformatted("FPS");
        ImGui.PopStyleColor();
        ImGui.SameLine(96f);
        ImGui.TextUnformatted($"{ImGui.GetIO().Framerate:F0}");
        ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.MutedText);
        ImGui.TextUnformatted("Objects");
        ImGui.PopStyleColor();
        ImGui.SameLine(96f);
        ImGui.TextUnformatted($"{_context.Document.Scene.AllObjects.Count}");
        ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.MutedText);
        ImGui.TextUnformatted("Render items");
        ImGui.PopStyleColor();
        ImGui.SameLine(96f);
        ImGui.TextUnformatted($"{_renderer.Items.Count}");
        ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.MutedText);
        ImGui.TextUnformatted("Lights");
        ImGui.PopStyleColor();
        ImGui.SameLine(96f);
        ImGui.TextUnformatted($"{_renderer.Lights.Count}");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextUnformatted($"Geometry: {_renderer.SceneVertexCount:N0} vertices / {_renderer.SceneTriangleCount:N0} triangles");
        ImGui.TextUnformatted($"Pending / failed meshes: {_renderer.LoadingMeshCount} / {_renderer.FailedMeshCount}");
        if (_context.ModelAssets is { } assets)
        {
            ImGui.TextUnformatted($"Import/cache: {assets.LastMeshImportMilliseconds:F1} ms ({(assets.LastMeshCacheHit ? "cache hit" : "source")})");
            ImGui.TextUnformatted($"Mesh GPU upload: {assets.LastMeshUploadMilliseconds:F1} ms");
            ImGui.TextUnformatted($"LOD chains: {assets.LodChainCount} ({assets.GeneratingLodChains} generating, {assets.PendingLodUploads} pending upload)");
            ImGui.TextUnformatted($"LOD generation: {assets.LastLodGenerationMilliseconds:F1} ms ({(assets.LastLodCacheHit ? "cache hit" : "source")})");
            ImGui.TextUnformatted($"Uploaded LOD levels: {assets.UploadedLodLevels}");
            ImGui.TextUnformatted($"LOD meshes: {_renderer.LodMeshCount}");
        }
        ImGui.TextUnformatted($"Scene sync CPU: {_renderer.SceneSyncMilliseconds:F2} ms");
        ImGui.TextUnformatted($"Frame wait CPU: {_renderer.FrameWaitMilliseconds:F2} ms");
        ImGui.TextUnformatted($"Frame setup/upload CPU: {_renderer.UniformUploadMilliseconds:F2} ms");
        ImGui.TextUnformatted($"Record/submit/present CPU: {_renderer.RenderCpuMilliseconds:F2} ms");
        ImGui.TextUnformatted($"Drawn / culled (all passes): {_renderer.DrawnObjects} / {_renderer.CulledObjects}");
        ImGui.TextUnformatted($"Views: Scene {(_renderer.SceneViewVisible ? "on" : "off")}, Game {(_renderer.GameViewVisible ? "on" : "off")}");
        float exposure = _renderer.Exposure;
        if (ImGui.SliderFloat("Exposure", ref exposure, 0.1f, 4f))
            _renderer.Exposure = exposure;
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
        ImGui.End();
    }
}
