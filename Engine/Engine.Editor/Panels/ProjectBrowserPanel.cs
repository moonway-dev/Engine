using Engine.Editor.Commands;
using Engine.Editor.Documents;
using Engine.Editor.DragDrop;
using Engine.Editor.Models;
using Engine.Scene;
using ImGuiNET;

namespace Engine.Editor.Panels;

public sealed class ProjectBrowserPanel : EditorPanel
{
    private readonly EditorContext _context;
    private readonly ProjectBrowserModel _model;
    private string? _error;
    private bool _createRequested;
    private string _createPath = "Materials/NewMaterial.mat";
    private AssetReference _createParent;

    public override string Title => "Project Browser";

    public ProjectBrowserModel Model => _model;

    public ProjectBrowserPanel(EditorContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _model = new ProjectBrowserModel(context.AssetsRoot);
        _model.Refresh();
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
        string filter = _model.Filter;
        ImGui.SetNextItemWidth(MathF.Max(120f, ImGui.GetContentRegionAvail().X - 90f));
        if (ImGui.InputTextWithHint("##AssetFilter", "Filter assets", ref filter, 256))
            _model.Filter = filter;
        ImGui.SameLine(0f, 6f);
        if (ImGui.SmallButton("Refresh"))
            Refresh();
        if (ImGui.SmallButton("+ Material"))
            RequestCreate(default);
        DrawCreateDialog();
        ImGui.Spacing();
        ImGui.Separator();

        if (_error is not null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Error);
            ImGui.TextWrapped(_error);
            ImGui.PopStyleColor();
            ImGui.Separator();
        }

        if (string.IsNullOrWhiteSpace(_model.Filter))
        {
            foreach (AssetEntry root in _model.Roots)
                DrawTree(root);
        }
        else
        {
            foreach (AssetEntry entry in _model.EnumerateVisible().Where(value => !value.IsDirectory))
                DrawEntry(entry, entry.RelativePath);
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
        catch (Exception exception)
        {
            _error = exception.Message;
        }
    }

    private void DrawTree(AssetEntry entry)
    {
        ImGui.PushID(entry.FullPath);
        if (entry.IsDirectory)
        {
            ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
            if (entry.Children.Count == 0)
                flags |= ImGuiTreeNodeFlags.Leaf;
            bool open = ImGui.TreeNodeEx(entry.Name, flags);
            if (ImGui.BeginPopupContextItem("FolderContext"))
            {
                if (ImGui.MenuItem("Create Material"))
                {
                    RequestCreate(default);
                    _createPath = Path.Combine(entry.RelativePath, "NewMaterial.mat").Replace('\\', '/');
                }
                ImGui.EndPopup();
            }
            if (open)
            {
                foreach (AssetEntry child in entry.Children)
                    DrawTree(child);
                ImGui.TreePop();
            }
        }
        else
        {
            DrawEntry(entry, entry.Name);
        }
        ImGui.PopID();
    }

    private void DrawEntry(AssetEntry entry, string label)
    {
        float rowStart = ImGui.GetCursorPosX();
        ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.MutedText);
        ImGui.TextUnformatted(entry.Kind.ToString());
        ImGui.PopStyleColor();
        ImGui.SameLine(rowStart + 80f);
        if (ImGui.Selectable(label, entry.Kind == AssetKind.Material && _context.SelectedMaterial is { } selected && selected.Path.Equals(entry.FullPath, StringComparison.OrdinalIgnoreCase), ImGuiSelectableFlags.AllowDoubleClick))
        {
            if (entry.Kind == AssetKind.Material)
                Activate(entry);
        }
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            Activate(entry);
        if (ImGui.BeginDragDropSource())
        {
            ImGuiDragPayloads.SetAsset(new AssetDragPayload(entry.Kind, entry.RelativePath));
            ImGui.TextUnformatted(entry.RelativePath);
            ImGui.EndDragDropSource();
        }
        if (ImGui.BeginPopupContextItem("AssetContext"))
        {
            if (entry.Kind == AssetKind.Material && ImGui.MenuItem("Create Variant"))
                RequestCreate(entry.RelativePath);
            if (entry.Kind == AssetKind.Scene && ImGui.MenuItem("Open Scene"))
                Activate(entry);
            if (entry.Kind == AssetKind.Prefab && ImGui.MenuItem("Instantiate Prefab"))
                Activate(entry);
            if (ImGui.MenuItem("Refresh"))
                Refresh();
            ImGui.EndPopup();
        }
    }

    private void Activate(AssetEntry entry)
    {
        try
        {
            switch (entry.Kind)
            {
                case AssetKind.Material:
                    _context.SelectMaterial(entry.RelativePath);
                    break;
                case AssetKind.Scene:
                    _context.Document.Open(entry.FullPath);
                    break;
                case AssetKind.Prefab:
                    InstantiatePrefab(entry);
                    break;
            }
            _error = null;
        }
        catch (Exception exception)
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
        ImGui.TextUnformatted("Path relative to Assets");
        ImGui.SetNextItemWidth(360f);
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
