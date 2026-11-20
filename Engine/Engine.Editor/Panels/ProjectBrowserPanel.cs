using Engine.Editor.Commands;
using Engine.Editor.Documents;
using Engine.Editor.DragDrop;
using Engine.Editor.Models;
using Engine.Scene;
using ImGuiNET;
using NumericsVector2 = System.Numerics.Vector2;

namespace Engine.Editor.Panels;

public sealed class ProjectBrowserPanel : EditorPanel
{
    private const float TreeIndentWidth = 14f;
    private const float MinZoom = 32f;
    private const float MaxZoom = 144f;
    private readonly EditorContext _context;
    private readonly ProjectBrowserModel _model;
    private readonly Func<string, IntPtr>? _icons;
    private readonly Func<AssetEntry, AssetPreview>? _previews;
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase) { string.Empty };
    private string? _error;
    private string? _selectedPath;
    private string? _scrollToPath;
    private float _zoom = 88f;
    private bool _createRequested;
    private string _createPath = "Materials/NewMaterial.mat";
    private AssetReference _createParent;

    public override string Title => "Project Browser";
    public ProjectBrowserModel Model => _model;

    public ProjectBrowserPanel(EditorContext context, Func<string, IntPtr>? icons = null, Func<AssetEntry, AssetPreview>? previews = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _icons = icons;
        _previews = previews;
        _model = new ProjectBrowserModel(context.AssetsRoot);
        Refresh();
    }

    public override void Draw()
    {
        if (!IsOpen)
            return;
        bool open = IsOpen;
        ImGui.SetNextWindowSize(new NumericsVector2(640f, 320f), ImGuiCond.FirstUseEver);
        bool visible = ImGui.Begin(Title, ref open);
        IsOpen = open;
        if (visible)
        {
            DrawToolbarRow();
            float footerHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y;
            NumericsVector2 size = ImGui.GetContentRegionAvail();
            float height = MathF.Max(1f, size.Y - footerHeight);
            float treeWidth = MathF.Min(190f, size.X * 0.35f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new NumericsVector2(4f, 4f));
            if (ImGui.BeginChild("##BrowserTree", new NumericsVector2(treeWidth, height), ImGuiChildFlags.ResizeX | ImGuiChildFlags.Borders))
            {
                DrawFavorites();
                ImGui.Spacing();
                foreach (AssetEntry root in _model.Roots)
                    DrawFolderNode(root, 0);
            }
            ImGui.EndChild();
            ImGui.SameLine(0f, 1f);
            if (ImGui.BeginChild("##BrowserContents", new NumericsVector2(0f, height), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                DrawBreadcrumbRow();
                ImGui.Separator();
                if (ImGui.BeginChild("##BrowserGrid", NumericsVector2.Zero))
                    DrawGrid();
                ImGui.EndChild();
            }
            ImGui.EndChild();
            ImGui.PopStyleVar();
            DrawStatusBar();
            DrawCreateDialog();
        }
        ImGui.End();
    }

    public void Refresh()
    {
        try
        {
            _model.Refresh();
            _error = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _error = exception.Message;
        }
    }

    private void DrawToolbarRow()
    {
        float width = ImGui.GetContentRegionAvail().X;
        if (ImGui.SmallButton("Create"))
            RequestCreate(default);
        if (width >= 240f)
        {
            ImGui.SameLine(0f, 6f);
            if (ImGui.SmallButton("Refresh"))
                Refresh();
        }
        ImGui.SameLine(0f, 8f);
        ImGui.SetNextItemWidth(MathF.Max(20f, ImGui.GetContentRegionAvail().X));
        string filter = _model.Filter;
        if (ImGui.InputTextWithHint("##AssetFilter", "Search assets", ref filter, 256))
        {
            _model.Filter = filter;
            _selectedPath = null;
        }
        if (_error is not null && ImGui.IsItemHovered())
            ImGui.SetTooltip(_error);
    }

    private void DrawBreadcrumbRow()
    {
        float height = ImGui.GetFrameHeight();
        if (ImGui.BeginChild("##Breadcrumb", new NumericsVector2(0f, height), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (_model.Favorites != FavoritesFilter.None || _model.Filter.Trim().Length > 0)
            {
                string label = _model.Favorites switch
                {
                    FavoritesFilter.AllMaterials => "All Materials",
                    FavoritesFilter.AllModels => "All Models",
                    FavoritesFilter.AllPrefabs => "All Prefabs",
                    _ => "Search results",
                };
                ImGui.TextUnformatted(Shorten(label, ImGui.GetContentRegionAvail().X));
            }
            else
            {
                List<(string Path, string Name)> crumbs = [(string.Empty, _model.Roots.FirstOrDefault()?.Name ?? "Assets")];
                string path = string.Empty;
                foreach (string segment in _model.CurrentDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    path = path.Length == 0 ? segment : path + "/" + segment;
                    crumbs.Add((path, segment));
                }
                float width = ImGui.GetContentRegionAvail().X;
                float total = crumbs.Sum(crumb => ImGui.CalcTextSize(crumb.Name).X + 24f);
                bool collapse = total > width && crumbs.Count > 2;
                for (int i = 0; i < crumbs.Count; i++)
                {
                    if (i > 0)
                    {
                        ImGui.SameLine(0f, 3f);
                        ImGui.TextDisabled(">");
                        ImGui.SameLine(0f, 3f);
                    }
                    if (collapse && i == 1)
                    {
                        if (ImGui.SmallButton("…"))
                            ImGui.OpenPopup("Parent folders");
                        if (ImGui.BeginPopup("Parent folders"))
                        {
                            foreach (var crumb in crumbs.Skip(1).SkipLast(1))
                                if (ImGui.Selectable(crumb.Path))
                                    Navigate(crumb.Path);
                            ImGui.EndPopup();
                        }
                        i = crumbs.Count - 2;
                        continue;
                    }
                    var current = crumbs[i];
                    float remaining = ImGui.GetContentRegionAvail().X;
                    string name = Shorten(current.Name, MathF.Max(0f, remaining - ImGui.GetStyle().FramePadding.X * 2f));
                    ImGui.PushID(current.Path);
                    if (ImGui.SmallButton(name + "##Crumb"))
                        Navigate(current.Path);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(current.Path.Length == 0 ? _model.AssetsRoot : current.Path);
                    ImGui.PopID();
                }
            }
        }
        ImGui.EndChild();
    }

    private void Navigate(string path)
    {
        if (!_model.Navigate(path))
            return;
        _model.Favorites = FavoritesFilter.None;
        _model.Filter = string.Empty;
        _selectedPath = null;
        string prefix = string.Empty;
        foreach (string segment in _model.CurrentDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            _expanded.Add(prefix);
            prefix = prefix.Length == 0 ? segment : prefix + "/" + segment;
        }
    }

    private IntPtr Icon(string name) => _icons?.Invoke(name) ?? IntPtr.Zero;
    private static string Shorten(string text, float width) => ProjectBrowserLayout.Ellipsize(text, width, value => ImGui.CalcTextSize(value).X);

    private void DrawFavorites()
    {
        ImGui.TextDisabled("Favorites");
        DrawFavoriteItem("material", "All Materials", FavoritesFilter.AllMaterials);
        DrawFavoriteItem("mesh", "All Models", FavoritesFilter.AllModels);
        DrawFavoriteItem("prefab", "All Prefabs", FavoritesFilter.AllPrefabs);
    }

    private void DrawFavoriteItem(string icon, string label, FavoritesFilter filter)
    {
        ImGui.PushID((int)filter);
        NumericsVector2 start = ImGui.GetCursorScreenPos();
        float height = ImGui.GetTextLineHeight() + 6f;
        if (ImGui.Selectable("##Favorite", _model.Favorites == filter, ImGuiSelectableFlags.None, new NumericsVector2(0f, height)))
        {
            _model.Favorites = filter;
            _selectedPath = null;
        }
        DrawRowContent(start, ImGui.GetItemRectSize().X, height, 4f, Icon(icon), label);
        ImGui.PopID();
    }

    private void DrawFolderNode(AssetEntry entry, int depth)
    {
        ImGui.PushID(entry.FullPath);
        NumericsVector2 start = ImGui.GetCursorScreenPos();
        float height = ImGui.GetTextLineHeight() + 6f;
        bool selected = entry.RelativePath.Equals(_model.CurrentDirectory, StringComparison.OrdinalIgnoreCase) && _model.Favorites == FavoritesFilter.None;
        bool hasChildren = entry.Children.Any(child => child.IsDirectory);
        bool expanded = _expanded.Contains(entry.RelativePath);
        float indent = depth * TreeIndentWidth;
        if (ImGui.Selectable("##Folder", selected, ImGuiSelectableFlags.AllowDoubleClick, new NumericsVector2(0f, height)))
        {
            float mouseX = ImGui.GetMousePos().X - start.X;
            if (hasChildren && (mouseX >= indent && mouseX < indent + 16f || ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)))
            {
                if (expanded)
                    _expanded.Remove(entry.RelativePath);
                else
                    _expanded.Add(entry.RelativePath);
                expanded = !expanded;
            }
            else
                Navigate(entry.RelativePath);
        }
        if (hasChildren)
        {
            ImDrawListPtr list = ImGui.GetWindowDrawList();
            NumericsVector2 center = start + new NumericsVector2(indent + 7f, height * 0.5f);
            uint color = ImGui.GetColorU32(EditorTheme.MutedText);
            if (expanded)
                list.AddTriangleFilled(center + new NumericsVector2(-4f, -2f), center + new NumericsVector2(4f, -2f), center + new NumericsVector2(0f, 3f), color);
            else
                list.AddTriangleFilled(center + new NumericsVector2(-2f, -4f), center + new NumericsVector2(-2f, 4f), center + new NumericsVector2(3f, 0f), color);
        }
        DrawRowContent(start, ImGui.GetItemRectSize().X, height, indent + 17f, Icon("folder"), entry.Name);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(entry.RelativePath.Length == 0 ? _model.AssetsRoot : entry.RelativePath);
        DrawFolderContextMenu(entry);
        if (expanded)
            foreach (AssetEntry child in entry.Children.Where(child => child.IsDirectory))
                DrawFolderNode(child, depth + 1);
        ImGui.PopID();
    }

    private static void DrawRowContent(NumericsVector2 start, float width, float height, float offset, IntPtr texture, string name)
    {
        float iconSize = MathF.Min(16f, height - 4f);
        ImDrawListPtr list = ImGui.GetWindowDrawList();
        if (texture != IntPtr.Zero)
        {
            NumericsVector2 image = start + new NumericsVector2(offset, (height - iconSize) * 0.5f);
            list.AddImage(texture, image, image + new NumericsVector2(iconSize));
        }
        float textX = offset + iconSize + 5f;
        list.AddText(start + new NumericsVector2(textX, (height - ImGui.GetTextLineHeight()) * 0.5f), ImGui.GetColorU32(EditorTheme.Text), Shorten(name, MathF.Max(0f, width - textX - 4f)));
    }

    private void DrawFolderContextMenu(AssetEntry folder)
    {
        if (!ImGui.BeginPopupContextItem("FolderContext"))
            return;
        if (ImGui.MenuItem("Create Material"))
        {
            RequestCreate(default);
            _createPath = (folder.RelativePath.Length == 0 ? string.Empty : folder.RelativePath + "/") + "NewMaterial.mat";
        }
        if (ImGui.MenuItem("Open"))
            Navigate(folder.RelativePath);
        if (ImGui.MenuItem("Refresh"))
            Refresh();
        ImGui.EndPopup();
    }

    private unsafe void DrawGrid()
    {
        IReadOnlyList<AssetEntry> entries = _model.CurrentEntries();
        if (entries.Count == 0)
        {
            ImGui.TextDisabled("No assets");
            return;
        }
        ProjectBrowserLayout layout = ProjectBrowserLayout.Create(ImGui.GetContentRegionAvail().X, _zoom, ImGui.GetTextLineHeight(), entries.Count);
        HandleKeyboard(entries, layout);
        int target = _scrollToPath is null ? -1 : FindIndex(entries, _scrollToPath);
        if (target >= 0)
        {
            float top = target / layout.Columns * layout.RowHeight;
            float bottom = top + layout.RowHeight;
            if (top < ImGui.GetScrollY())
                ImGui.SetScrollY(top);
            else if (bottom > ImGui.GetScrollY() + ImGui.GetWindowHeight())
                ImGui.SetScrollY(bottom - ImGui.GetWindowHeight());
            _scrollToPath = null;
        }
        float originX = ImGui.GetCursorPosX();
        ImGuiListClipperPtr clipper = new(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
        clipper.Begin(layout.Rows, layout.RowHeight);
        while (clipper.Step())
        {
            for (int row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
            {
                float y = ImGui.GetCursorPosY();
                for (int column = 0; column < layout.Columns; column++)
                {
                    int index = row * layout.Columns + column;
                    if (index >= entries.Count)
                        break;
                    ImGui.SetCursorPos(new NumericsVector2(originX + column * (layout.CellWidth + 8f), y));
                    DrawTile(entries[index], layout);
                }
                ImGui.SetCursorPos(new NumericsVector2(originX, y));
                ImGui.Dummy(new NumericsVector2(1f, layout.RowHeight - ImGui.GetStyle().ItemSpacing.Y));
            }
        }
        clipper.End();
        clipper.Destroy();
    }

    private static int FindIndex(IReadOnlyList<AssetEntry> entries, string? path)
    {
        for (int i = 0; i < entries.Count; i++)
            if (string.Equals(path, entries[i].FullPath, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private void HandleKeyboard(IReadOnlyList<AssetEntry> entries, ProjectBrowserLayout layout)
    {
        if (!ImGui.IsWindowFocused() || ImGui.GetIO().WantTextInput || ImGui.IsAnyItemActive())
            return;
        int selected = FindIndex(entries, _selectedPath);
        int delta = 0;
        if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow)) delta = -1;
        if (ImGui.IsKeyPressed(ImGuiKey.RightArrow)) delta = 1;
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow)) delta = -layout.Columns;
        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow)) delta = layout.Columns;
        if (delta != 0)
        {
            selected = selected < 0 ? 0 : Math.Clamp(selected + delta, 0, entries.Count - 1);
            Select(entries[selected]);
            _scrollToPath = entries[selected].FullPath;
        }
        if (selected >= 0 && ImGui.IsKeyPressed(ImGuiKey.Enter))
            Activate(entries[selected]);
    }

    private void DrawTile(AssetEntry entry, ProjectBrowserLayout layout)
    {
        NumericsVector2 start = ImGui.GetCursorScreenPos();
        ImGui.PushID(entry.FullPath);
        float height = layout.RowHeight - (layout.IsList ? 0f : 8f);
        ImGui.InvisibleButton("##Tile", new NumericsVector2(layout.CellWidth, height));
        bool hovered = ImGui.IsItemHovered();
        bool selected = string.Equals(_selectedPath, entry.FullPath, StringComparison.OrdinalIgnoreCase);
        ImDrawListPtr list = ImGui.GetWindowDrawList();
        if (selected || hovered)
            list.AddRectFilled(start, start + new NumericsVector2(layout.CellWidth, height), selected ? ImGui.GetColorU32(EditorTheme.AccentActive) : 0x14FFFFFF);
        AssetPreview preview = layout.IsList ? default : _previews?.Invoke(entry) ?? default;
        IntPtr texture = preview.Texture != IntPtr.Zero ? preview.Texture : Icon(entry.IsDirectory ? "folder" : IconName(entry.Kind));
        if (layout.IsList)
            DrawRowContent(start, layout.CellWidth, height, 4f, texture, entry.Name);
        else
        {
            NumericsVector2 area = new(layout.PreviewSize);
            NumericsVector2 imageSize = preview.Texture != IntPtr.Zero
                ? ProjectBrowserLayout.FitImage(preview.Width, preview.Height, area)
                : area * 0.72f;
            NumericsVector2 imageMin = start + new NumericsVector2(4f) + (area - imageSize) * 0.5f;
            if (preview.HasTransparency)
                DrawCheckerboard(list, imageMin, imageSize);
            if (texture != IntPtr.Zero)
                list.AddImage(texture, imageMin, imageMin + imageSize);
            string name = Shorten(Path.GetFileNameWithoutExtension(entry.Name), layout.CellWidth - 4f);
            NumericsVector2 textSize = ImGui.CalcTextSize(name);
            list.AddText(start + new NumericsVector2((layout.CellWidth - textSize.X) * 0.5f, layout.CellWidth + 1f), ImGui.GetColorU32(EditorTheme.Text), name);
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            Select(entry);
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            Activate(entry);
        if (hovered)
            ImGui.SetTooltip(preview.Error is null ? entry.RelativePath : entry.RelativePath + "\n" + preview.Error);
        if (ImGui.BeginDragDropSource())
        {
            ImGuiDragPayloads.SetAsset(new AssetDragPayload(entry.Kind, entry.RelativePath));
            ImGui.TextUnformatted(entry.Name);
            ImGui.EndDragDropSource();
        }
        if (ImGui.BeginPopupContextItem("AssetContext"))
        {
            if (entry.IsDirectory && ImGui.MenuItem("Create Material"))
            {
                RequestCreate(default);
                _createPath = entry.RelativePath + "/NewMaterial.mat";
            }
            if (entry.Kind == AssetKind.Material && ImGui.MenuItem("Create Variant"))
                RequestCreate(entry.RelativePath);
            if (ImGui.MenuItem(entry.IsDirectory ? "Open Folder" : "Open"))
                Activate(entry);
            if (ImGui.MenuItem("Refresh"))
                Refresh();
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private static void DrawCheckerboard(ImDrawListPtr list, NumericsVector2 start, NumericsVector2 size)
    {
        const float step = 8f;
        for (int y = 0; y < MathF.Ceiling(size.Y / step); y++)
            for (int x = 0; x < MathF.Ceiling(size.X / step); x++)
                list.AddRectFilled(start + new NumericsVector2(x * step, y * step), start + new NumericsVector2(MathF.Min(size.X, (x + 1) * step), MathF.Min(size.Y, (y + 1) * step)), (x + y) % 2 == 0 ? 0xFF555555 : 0xFF444444);
    }

    private void Select(AssetEntry entry)
    {
        _selectedPath = entry.FullPath;
        if (entry.Kind == AssetKind.Material)
        {
            try { _context.SelectMaterial(entry.RelativePath); }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException) { _error = exception.Message; }
        }
    }

    private static string IconName(AssetKind kind) => kind switch
    {
        AssetKind.Scene => "scene",
        AssetKind.Prefab => "prefab",
        AssetKind.Mesh => "mesh",
        AssetKind.Texture => "texture",
        AssetKind.Material => "material",
        AssetKind.Shader => "shader",
        AssetKind.Script => "script",
        _ => "document",
    };

    private void DrawStatusBar()
    {
        NumericsVector2 start = ImGui.GetCursorScreenPos();
        float width = ImGui.GetContentRegionAvail().X;
        float zoomWidth = MathF.Min(110f, width * 0.3f);
        float height = ImGui.GetFrameHeight();
        string label = _error ?? (_selectedPath is null ? _model.CurrentDirectory : Path.GetRelativePath(_model.AssetsRoot, _selectedPath));
        ImGui.GetWindowDrawList().AddText(start + new NumericsVector2(2f, (height - ImGui.GetTextLineHeight()) * 0.5f), ImGui.GetColorU32(_error is null ? EditorTheme.MutedText : EditorTheme.Error), Shorten(label, MathF.Max(0f, width - zoomWidth - 12f)));
        ImGui.SetCursorScreenPos(start + new NumericsVector2(width - zoomWidth, 0f));
        ImGui.SetNextItemWidth(zoomWidth);
        ImGui.SliderFloat("##Zoom", ref _zoom, MinZoom, MaxZoom, string.Empty);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Thumbnail size");
    }

    private void Activate(AssetEntry entry)
    {
        try
        {
            switch (entry.Kind)
            {
                case AssetKind.Folder: Navigate(entry.RelativePath); break;
                case AssetKind.Material: _context.SelectMaterial(entry.RelativePath); break;
                case AssetKind.Scene: _context.Document.Open(entry.FullPath); break;
                case AssetKind.Prefab: InstantiatePrefab(entry); break;
            }
            _error = null;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            _error = exception.Message;
        }
    }

    private void RequestCreate(AssetReference parent)
    {
        _createParent = parent;
        _createPath = parent.IsEmpty ? "Materials/NewMaterial.mat" : "Materials/NewVariant.mat";
        _createRequested = true;
    }

    private void DrawCreateDialog()
    {
        if (_createRequested)
        {
            ImGui.OpenPopup("Create Material");
            _createRequested = false;
        }
        bool open = true;
        if (!ImGui.BeginPopupModal("Create Material", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;
        ImGui.TextUnformatted("Path relative to project");
        ImGui.SetNextItemWidth(280f);
        ImGui.InputText("##MaterialPath", ref _createPath, 1024);
        if (_error is not null)
            ImGui.TextWrapped(_error);
        if (ImGui.Button("Create"))
        {
            try
            {
                if (!Path.GetExtension(_createPath).Equals(".mat", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Material path must end in .mat.");
                AssetReference parent = _createParent.IsEmpty ? default : _context.Materials.GetReference(_createParent.Key);
                _context.CreateMaterial(_createPath, parent);
                Refresh();
                ImGui.CloseCurrentPopup();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
            {
                _error = exception.Message;
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void InstantiatePrefab(AssetEntry entry)
    {
        SceneDocument document = _context.Document;
        Guid instantiatedId = Guid.Empty;
        document.Execute(new SnapshotCommand("Instantiate Prefab", document, () =>
        {
            PrefabAsset prefab = PrefabAsset.Load(entry.FullPath, entry.RelativePath);
            GameObject instantiated = prefab.Instantiate(document.Scene, document.SelectedObject);
            instantiatedId = instantiated.Id;
            document.Select(instantiated);
        }), false);
        document.Select(instantiatedId);
    }
}
