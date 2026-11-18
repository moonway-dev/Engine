using Engine.Editor.DragDrop;
using Engine.Mathematics;
using Engine.Scene;
using Engine.RHI;
using Engine.Rendering;
using ImGuiNET;

namespace Engine.Editor.Panels;

public sealed class ViewportPanel : EditorPanel
{
    private readonly string _title;
    private readonly bool _tracksDesiredExtent;
    private readonly EditorContext? _editorContext;
    private ITexture? _texture;
    private IntPtr _textureId;
    private Size2D _textureSize;
    private System.Numerics.Vector2 _contentSize = new(1280f, 720f);

    public ImDrawListPtr DrawList { get; private set; }

    public override string Title => _title;

    public bool IsRenderVisible { get; private set; }

    public bool IsHovered { get; private set; }

    public bool IsWindowFocused { get; private set; }

    public bool ImageClicked { get; private set; }

    public System.Numerics.Vector2 ImageMinimum { get; private set; }

    public System.Numerics.Vector2 ImageSize { get; private set; }

    public System.Numerics.Vector2 WindowPosition { get; private set; }

    public System.Numerics.Vector2 MouseNormalized { get; private set; }

    public AssetDragPayload? DroppedAsset { get; private set; }

    public bool OverlayItemActive { get; private set; }

    public bool HasImage { get; private set; }

    public bool CanDrawOverlay => HasImage && HasNativeDrawList(DrawList);

    private static unsafe bool HasNativeDrawList(ImDrawListPtr drawList) => drawList.NativePtr != null;

    public Size2D DesiredExtent => new(
        Math.Max(16, (int)_contentSize.X),
        Math.Max(16, (int)_contentSize.Y));

    public ViewportPanel(string title, bool tracksDesiredExtent, EditorContext? editorContext = null)
    {
        _title = title;
        _tracksDesiredExtent = tracksDesiredExtent;
        _editorContext = editorContext;
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
        OverlayItemActive = false;
        IsHovered = false;
        IsWindowFocused = false;
        ImageClicked = false;
        HasImage = false;
        MouseNormalized = System.Numerics.Vector2.Zero;
        DrawList = default;
        ImageMinimum = System.Numerics.Vector2.Zero;
        ImageSize = System.Numerics.Vector2.Zero;
        WindowPosition = System.Numerics.Vector2.Zero;
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
        IsWindowFocused = ImGui.IsWindowFocused();
        WindowPosition = ImGui.GetWindowPos();
        DrawList = ImGui.GetWindowDrawList();
        System.Numerics.Vector2 available = ImGui.GetContentRegionAvail();
        IsRenderVisible = open && available.X >= 8f && available.Y >= 8f;
        if (available.X > 8f && available.Y > 8f)
            _contentSize = available;
        if (available.X < 8f || available.Y < 8f || _texture is null)
        {
            IsHovered = ImGui.IsWindowHovered();
            IsWindowFocused = ImGui.IsWindowFocused();
            ImageClicked = false;
            ImGui.End();
            return;
        }
        float scaleX = _textureSize.Width > 0 ? available.X / _textureSize.Width : 1f;
        float scaleY = _textureSize.Height > 0 ? available.Y / _textureSize.Height : 1f;
        float scale = MathF.Min(scaleX, scaleY);
        float width = _textureSize.Width * scale;
        float height = _textureSize.Height * scale;
        ImGui.Image(_textureId, new System.Numerics.Vector2(width, height));
        ImageMinimum = ImGui.GetItemRectMin();
        ImageSize = new System.Numerics.Vector2(width, height);
        HasImage = width > 0f && height > 0f && HasNativeDrawList(DrawList);
        IsRenderVisible = IsRenderVisible && HasImage;
        IsHovered = HasImage && ImGui.IsItemHovered();
        bool imageClicked = HasImage && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        if (IsHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            ImGui.SetWindowFocus();
            IsWindowFocused = ImGui.IsWindowFocused();
        }
        if (HasImage && ImGui.BeginDragDropTarget())
        {
            DroppedAsset = ImGuiDragPayloads.AcceptAsset();
            DrawList.AddRect(
                ImageMinimum - new System.Numerics.Vector2(1f, 1f),
                ImageMinimum + ImageSize + new System.Numerics.Vector2(1f, 1f),
                ImGui.GetColorU32(EditorTheme.Accent),
                0f,
                ImDrawFlags.None,
                2f);
            ImGui.EndDragDropTarget();
        }
        if (HasImage && _editorContext is not null)
            DrawTransformOverlay();
        ImageClicked = imageClicked && IsHovered && !OverlayItemActive;
        if (HasImage && IsWindowFocused)
            DrawList.AddRect(
                ImageMinimum + System.Numerics.Vector2.One,
                ImageMinimum + ImageSize - System.Numerics.Vector2.One,
                ImGui.GetColorU32(EditorTheme.AccentActive));
        System.Numerics.Vector2 mouse = ImGui.GetMousePos() - ImageMinimum;
        MouseNormalized = new System.Numerics.Vector2(
            Math.Clamp(mouse.X / MathF.Max(width, 1f), 0f, 1f),
            Math.Clamp(mouse.Y / MathF.Max(height, 1f), 0f, 1f));
        ImGui.End();
    }

    private void DrawTransformOverlay()
    {
        System.Numerics.Vector2 imageMinimum = ImageMinimum;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(4f, 2f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new System.Numerics.Vector2(3f, 2f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(31f / 255f, 37f / 255f, 48f / 255f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(42f / 255f, 53f / 255f, 70f / 255f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, EditorTheme.AccentActive);
        ImGui.PushStyleColor(ImGuiCol.Border, new System.Numerics.Vector4(48f / 255f, 55f / 255f, 68f / 255f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Separator, new System.Numerics.Vector4(48f / 255f, 56f / 255f, 70f / 255f, 0.7f));
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.96f);
        ImGui.PushClipRect(imageMinimum, imageMinimum + ImageSize, true);
        DrawList.ChannelsSplit(2);
        DrawList.ChannelsSetCurrent(1);
        ImGui.SetCursorScreenPos(imageMinimum + new System.Numerics.Vector2(10f, 10f));
        ImGui.BeginGroup();
        DrawModeButton("P", TransformMode.Translate, "Move (W)");
        ImGui.SameLine();
        DrawModeButton("R", TransformMode.Rotate, "Rotate (E)");
        ImGui.SameLine();
        DrawModeButton("S", TransformMode.Scale, "Scale (R)");
        ImGui.SameLine(0f, 6f);
        ImGui.TextDisabled("|");
        ImGui.SameLine(0f, 5f);
        bool localScale = _editorContext!.TransformMode == TransformMode.Scale;
        ImGui.BeginDisabled(localScale);
        bool local = localScale || _editorContext.TransformSpace == TransformSpace.Local;
        DrawSpaceButton("Local", TransformSpace.Local, local);
        ImGui.SameLine();
        DrawSpaceButton("Global", TransformSpace.Global, !local);
        ImGui.EndDisabled();
        System.Numerics.Vector2 overlayMinimum = ImGui.GetItemRectMin();
        System.Numerics.Vector2 overlayMaximum = ImGui.GetItemRectMax();
        System.Numerics.Vector2 mouse = ImGui.GetMousePos();
        OverlayItemActive = mouse.X >= overlayMinimum.X - 4f && mouse.X <= overlayMaximum.X + 4f
            && mouse.Y >= overlayMinimum.Y - 4f && mouse.Y <= overlayMaximum.Y + 4f;
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && localScale)
            ImGui.SetTooltip("Scale uses local axes.");
        ImGui.EndGroup();
        System.Numerics.Vector2 cardMaximum = ImGui.GetItemRectMax() + new System.Numerics.Vector2(4f, 4f);
        System.Numerics.Vector2 cardMinimum = ImGui.GetItemRectMin() - new System.Numerics.Vector2(4f, 4f);
        DrawList.ChannelsSetCurrent(0);
        DrawList.AddRectFilled(cardMinimum, cardMaximum, ImGui.GetColorU32(new System.Numerics.Vector4(0.08f, 0.10f, 0.14f, 0.96f)), 4f);
        DrawList.AddRect(cardMinimum, cardMaximum, ImGui.GetColorU32(new System.Numerics.Vector4(EditorTheme.Accent.X, EditorTheme.Accent.Y, EditorTheme.Accent.Z, 0.35f)), 4f, ImDrawFlags.None, 1f);
        DrawList.ChannelsMerge();
        ImGui.PopClipRect();
        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(5);
    }

    private void DrawModeButton(string label, TransformMode mode, string tooltip)
    {
        bool selected = _editorContext?.TransformMode == mode;
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, EditorTheme.AccentActive);
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Text);
        }
        if (ImGui.SmallButton(label) && _editorContext is not null)
            _editorContext.TransformMode = mode;
        if (selected)
            ImGui.PopStyleColor(2);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }

    private void DrawSpaceButton(string label, TransformSpace space, bool selected)
    {
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, EditorTheme.AccentActive);
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Text);
        }
        if (ImGui.SmallButton(label) && _editorContext is not null)
            _editorContext.TransformSpace = space;
        if (selected)
            ImGui.PopStyleColor(2);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(space == TransformSpace.Local ? "Use object axes." : "Use world axes.");
    }
}

public sealed class StatsPanel : EditorPanel
{
    private readonly EditorContext _context;
    private readonly RendererModule _renderer;
    private readonly FeatureInfo? _graphicsFeatures;
    private readonly IRHI? _rhi;

    public override string Title => "Stats";

    public StatsPanel(EditorContext context, RendererModule renderer, FeatureInfo? graphicsFeatures = null, IRHI? rhi = null)
    {
        _context = context;
        _renderer = renderer;
        _graphicsFeatures = graphicsFeatures;
        _rhi = rhi;
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
        DrawStats();
        ImGui.End();
    }

    private void DrawStats()
    {
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
            ImGui.TextUnformatted($"Render items / lights: {_renderer.Items.Count} / {_renderer.Lights.Count} ({_renderer.ActiveLights.Count} selected)");
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
        if (ImGui.CollapsingHeader("GPU pass timings"))
        {
            if (_rhi?.SupportsGpuTimestamps != true)
                ImGui.TextUnformatted("GPU timestamps are not supported by this backend.");
            else if (_rhi.LatestGpuFrameTiming is not { } timing)
                ImGui.TextUnformatted("Waiting for a completed frame slot.");
            else
            {
                ImGui.TextUnformatted($"Frame {timing.FrameNumber} (delayed until GPU completion)");
                foreach (GpuPassTiming pass in timing.Passes)
                    ImGui.TextUnformatted($"{pass.Name}: {pass.Milliseconds:F3} ms");
                if (timing.Incomplete)
                    ImGui.TextUnformatted("Timing data is incomplete; one or more scopes were dropped or unavailable.");
                else if (timing.Passes.Count == 0)
                    ImGui.TextUnformatted("No pass timestamps were recorded.");
            }
        }
        if (ImGui.CollapsingHeader("Frame CPU timings"))
        {
            ImGui.TextUnformatted("CPU measurements only; not GPU execution time.");
            ImGui.TextUnformatted($"Scene sync: {_renderer.SceneSyncMilliseconds:F2} ms");
            ImGui.TextUnformatted($"Frame wait: {_renderer.FrameWaitMilliseconds:F2} ms");
            ImGui.TextUnformatted($"Frame setup/upload: {_renderer.UniformUploadMilliseconds:F2} ms");
            ImGui.TextUnformatted($"Record/submit/present: {_renderer.RenderCpuMilliseconds:F2} ms");
        }
    }

    private static string YesNo(bool value) => value ? "yes" : "no";
}

public sealed class ProjectSettingsPanel : EditorPanel
{
    private readonly RendererModule _renderer;
    private readonly ProjectSettings _settings;
    private readonly string _path;
    private bool _dirty;

    public override string Title => "Project Settings";

    public event Action<bool>? VSyncChanged;

    public ProjectSettingsPanel(RendererModule renderer, ProjectSettings settings, string path)
    {
        _renderer = renderer;
        _settings = settings;
        _path = path;
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
        bool rtAvailable = _renderer.RayTracingAvailable;
        bool rtMaster = rtAvailable && _settings.RayTracingEnabled;
        ImGui.TextUnformatted("Rendering");
        ImGui.Separator();
        if (ImGui.SliderFloat("Exposure", ref _settings.Exposure, 0.1f, 4f))
        {
            _renderer.Exposure = _settings.Exposure;
            _dirty = true;
        }
        if (ImGui.SliderFloat("LOD pixel budget", ref _settings.LodPixelBudget, 0.25f, 8f))
        {
            _renderer.LodPixelBudget = _settings.LodPixelBudget;
            _dirty = true;
        }
        ImGui.TextUnformatted("Ray tracing");
        ImGui.Separator();
        if (!rtAvailable)
        {
            ImGui.TextDisabled("Ray tracing is unavailable on this device.");
        }
        else if (ImGui.Checkbox("Ray Tracing", ref _settings.RayTracingEnabled))
        {
            _renderer.RayTracingEnabled = _settings.RayTracingEnabled;
            _dirty = true;
        }
        rtMaster = rtAvailable && _settings.RayTracingEnabled;
        if (rtMaster)
        {
            if (ImGui.Checkbox("RT Shadows (directional + point)", ref _settings.RayTracingShadowsEnabled))
            {
                _renderer.RayTracingShadowsEnabled = _settings.RayTracingShadowsEnabled;
                _dirty = true;
            }
            if (ImGui.Checkbox("RT Reflections", ref _settings.RayTracingReflectionsEnabled))
            {
                _renderer.RayTracingReflectionsEnabled = _settings.RayTracingReflectionsEnabled;
                _dirty = true;
            }
            if (_settings.RayTracingReflectionsEnabled
                && ImGui.SliderFloat("RT reflection strength", ref _settings.RayTracingReflectionStrength, 0f, 1f))
            {
                _renderer.RayTracingReflectionStrength = _settings.RayTracingReflectionStrength;
                _dirty = true;
            }
            if (ImGui.Checkbox("RT GI", ref _settings.RayTracingGiEnabled))
            {
                _renderer.RayTracingGiEnabled = _settings.RayTracingGiEnabled;
                _dirty = true;
            }
            if (_settings.RayTracingGiEnabled
                && ImGui.SliderFloat("GI intensity", ref _settings.ScreenSpaceGiIntensity, 0f, 2f))
            {
                _renderer.ScreenSpaceGIIntensity = _settings.ScreenSpaceGiIntensity;
                _dirty = true;
            }
            if (ImGui.SliderFloat("RT resolution scale", ref _settings.RayTracingResolutionScale, 0.25f, 1f, "%.2f"))
            {
                _renderer.RayTracingResolutionScale = _settings.RayTracingResolutionScale;
                _dirty = true;
            }
            if (ImGui.SliderFloat("RT ray bias", ref _settings.RayTracingRayBias, 0.0001f, 0.1f, "%.4f"))
            {
                _renderer.RayTracingRayBias = _settings.RayTracingRayBias;
                _dirty = true;
            }
            if (ImGui.SliderFloat("RT max distance", ref _settings.RayTracingMaxDistance, 1f, 1000f))
            {
                _renderer.RayTracingMaxDistance = _settings.RayTracingMaxDistance;
                _dirty = true;
            }
            if (_settings.RayTracingReflectionsEnabled && ImGui.SliderFloat("RT roughness limit", ref _settings.RayTracingReflectionRoughnessLimit, 0.045f, 1f))
            {
                _renderer.RayTracingReflectionRoughnessLimit = _settings.RayTracingReflectionRoughnessLimit;
                _dirty = true;
            }
            int rtSamples = _settings.RayTracingSamples;
            if (ImGui.SliderInt("RT samples", ref rtSamples, 1, 8))
            {
                _settings.RayTracingSamples = rtSamples;
                _renderer.RayTracingSamples = rtSamples;
                _dirty = true;
            }
            bool adaptive = _settings.RayTracingAdaptiveQuality;
            if (ImGui.Checkbox("RT adaptive quality", ref adaptive))
            {
                _settings.RayTracingAdaptiveQuality = adaptive;
                _renderer.RayTracingAdaptiveQualityEnabled = adaptive;
                _dirty = true;
            }
            DrawRtQualityCombo("RT shadow quality", RayTracingQualityEffect.Shadows);
            DrawRtQualityCombo("RT reflection quality", RayTracingQualityEffect.Reflections);
            DrawRtQualityCombo("RT GI quality", RayTracingQualityEffect.Gi);
            (RayTracingQualityCamera, RayTracingQualityEffect)[] effects =
            [
                (RayTracingQualityCamera.Scene, RayTracingQualityEffect.Shadows),
                (RayTracingQualityCamera.Scene, RayTracingQualityEffect.Reflections),
                (RayTracingQualityCamera.Scene, RayTracingQualityEffect.Gi),
                (RayTracingQualityCamera.Game, RayTracingQualityEffect.Shadows),
                (RayTracingQualityCamera.Game, RayTracingQualityEffect.Reflections),
                (RayTracingQualityCamera.Game, RayTracingQualityEffect.Gi),
            ];
            foreach ((RayTracingQualityCamera camera, RayTracingQualityEffect effect) in effects)
            {
                (int stage, RayTracingQualityProfile profile, double milliseconds) = _renderer.GetRayTracingQuality(camera, effect);
                ImGui.TextUnformatted($"{camera} {effect}: stage {stage} ({profile.ResolutionScale * 100f:0}%, {profile.Samples} spp, rate {profile.FarTraceRate}), {milliseconds:F2} ms");
            }
        }
        if (!rtMaster)
        {
            DrawShadowSettings();
            DrawSsgiSettings();
        }
        ImGui.TextUnformatted("Application");
        ImGui.Separator();
        if (ImGui.Checkbox("VSync", ref _settings.VSync))
        {
            VSyncChanged?.Invoke(_settings.VSync);
            _dirty = true;
        }
        Save();
        ImGui.End();
    }

    private void DrawRtQualityCombo(string label, RayTracingQualityEffect effect)
    {
        RayTracingQualityPreset current = effect switch
        {
            RayTracingQualityEffect.Shadows => _settings.RayTracingShadowQuality,
            RayTracingQualityEffect.Reflections => _settings.RayTracingReflectionQuality,
            _ => _settings.RayTracingGiQuality,
        };
        string[] names = ["Low", "Medium", "High", "Ultra"];
        int index = (int)current;
        if (!ImGui.Combo(label, ref index, names, names.Length) || index == (int)current)
            return;
        RayTracingQualityPreset preset = (RayTracingQualityPreset)index;
        switch (effect)
        {
            case RayTracingQualityEffect.Shadows:
                _settings.RayTracingShadowQuality = preset;
                _renderer.RayTracingShadowQuality = preset;
                break;
            case RayTracingQualityEffect.Reflections:
                _settings.RayTracingReflectionQuality = preset;
                _renderer.RayTracingReflectionQuality = preset;
                break;
            default:
                _settings.RayTracingGiQuality = preset;
                _renderer.RayTracingGiQuality = preset;
                break;
        }
        _dirty = true;
    }

    private void DrawShadowSettings()
    {
        ImGui.TextUnformatted("Shadows");
        ImGui.Separator();
        CascadedShadowSettings shadows = _settings.Shadows;
        float strength = shadows.Strength;
        if (ImGui.SliderFloat("Shadow strength", ref strength, 0f, 1f))
        {
            _settings.Shadows = shadows = shadows with { Strength = strength };
            _renderer.ShadowSettings = shadows;
            _dirty = true;
        }
        int quality = (int)shadows.Quality;
        if (ImGui.Combo("Shadow quality", ref quality, "Low\0Medium\0High\0Soft (PCSS)\0"))
        {
            int resolution = quality == 0 ? 512 : quality == 1 ? 1024 : 2048;
            _settings.Shadows = shadows = shadows with { Quality = (ShadowQuality)quality, Resolution = resolution };
            _renderer.ShadowSettings = shadows;
            _dirty = true;
        }
        int resolutionIndex = shadows.Resolution == 128 ? 0 : shadows.Resolution == 256 ? 1 : shadows.Resolution == 512 ? 2 : shadows.Resolution == 1024 ? 3 : 4;
        if (ImGui.Combo("Cascade resolution", ref resolutionIndex, "128\0256\0512\01024\02048\0"))
        {
            _settings.Shadows = shadows = shadows with { Resolution = 128 << resolutionIndex };
            _renderer.ShadowSettings = shadows;
            _dirty = true;
        }
        float shadowDistance = shadows.Distance;
        if (ImGui.SliderFloat("Shadow distance", ref shadowDistance, 10f, 300f))
        {
            _settings.Shadows = shadows = shadows with { Distance = shadowDistance };
            _renderer.ShadowSettings = shadows;
            _dirty = true;
        }
        float splitLambda = shadows.SplitLambda;
        if (ImGui.SliderFloat("Cascade distribution", ref splitLambda, 0f, 1f))
        {
            _settings.Shadows = shadows = shadows with { SplitLambda = splitLambda };
            _renderer.ShadowSettings = shadows;
            _dirty = true;
        }
        float normalBias = shadows.NormalBias;
        if (ImGui.SliderFloat("Shadow normal bias", ref normalBias, 0f, 3f))
        {
            _settings.Shadows = shadows = shadows with { NormalBias = normalBias };
            _renderer.ShadowSettings = shadows;
            _dirty = true;
        }
        float receiverBias = shadows.ReceiverBias;
        if (ImGui.SliderFloat("Shadow receiver bias", ref receiverBias, 0f, 2f))
        {
            _settings.Shadows = shadows = shadows with { ReceiverBias = receiverBias };
            _renderer.ShadowSettings = shadows;
            _dirty = true;
        }
        if (ImGui.SliderFloat("Shadow depth bias", ref _settings.ShadowConstantBias, 0f, 4f))
        {
            _renderer.ShadowConstantBias = _settings.ShadowConstantBias;
            _dirty = true;
        }
        if (ImGui.SliderFloat("Shadow slope bias", ref _settings.ShadowSlopeBias, 0f, 4f))
        {
            _renderer.ShadowSlopeBias = _settings.ShadowSlopeBias;
            _dirty = true;
        }
        if (shadows.Quality == ShadowQuality.Soft)
        {
            float angularRadius = shadows.AngularRadius;
            if (ImGui.SliderFloat("Sun angular radius", ref angularRadius, 0f, 0.05f, "%.4f rad"))
            {
                _settings.Shadows = shadows = shadows with { AngularRadius = angularRadius };
                _renderer.ShadowSettings = shadows;
                _dirty = true;
            }
        }
        bool debugCascades = shadows.DebugCascades;
        if (ImGui.Checkbox("Show shadow cascades", ref debugCascades))
        {
            _settings.Shadows = shadows = shadows with { DebugCascades = debugCascades };
            _renderer.ShadowSettings = shadows;
            _dirty = true;
        }
    }

    private void DrawSsgiSettings()
    {
        ImGui.TextUnformatted("Global illumination");
        ImGui.Separator();
        if (ImGui.Checkbox("Screen-space GI", ref _settings.ScreenSpaceGiEnabled))
        {
            _renderer.ScreenSpaceGIEnabled = _settings.ScreenSpaceGiEnabled;
            _dirty = true;
        }
        if (_settings.ScreenSpaceGiEnabled)
        {
            if (ImGui.SliderFloat("GI intensity", ref _settings.ScreenSpaceGiIntensity, 0f, 2f))
            {
                _renderer.ScreenSpaceGIIntensity = _settings.ScreenSpaceGiIntensity;
                _dirty = true;
            }
            if (ImGui.SliderFloat("GI radius", ref _settings.ScreenSpaceGiRadius, 0.25f, 4f))
            {
                _renderer.ScreenSpaceGIRadius = _settings.ScreenSpaceGiRadius;
                _dirty = true;
            }
        }
    }

    private void Save()
    {
        if (!_dirty)
            return;
        _dirty = false;
        try
        {
            _settings.Save(_path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public sealed class SceneSettingsPanel : EditorPanel
{
    private readonly EditorContext _context;
    private readonly RendererModule _renderer;
    private System.Numerics.Vector4 _skyColor;

    public override string Title => "Scene Settings";

    public SceneSettingsPanel(EditorContext context, RendererModule renderer)
    {
        _context = context;
        _renderer = renderer;
        IsOpen = false;
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
        Scene.Scene scene = _context.Document.Scene;
        Vector4 sceneSky = scene.SkyColor;
        if (!sceneSky.Equals(new Vector4(_skyColor.X, _skyColor.Y, _skyColor.Z, _skyColor.W)))
            _skyColor = new System.Numerics.Vector4(sceneSky.X, sceneSky.Y, sceneSky.Z, sceneSky.W);
        if (ImGui.ColorEdit4("Sky color", ref _skyColor))
        {
            scene.SkyColor = new Vector4(_skyColor.X, _skyColor.Y, _skyColor.Z, _skyColor.W);
            _renderer.SkyColor = scene.SkyColor;
        }
        ImGui.End();
    }
}
