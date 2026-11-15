using Engine.Editor.Documents;
using Engine.Editor.DragDrop;
using Engine.Scene;
using ImGuiNET;

namespace Engine.Editor.Panels;

public sealed class HierarchyPanel : EditorPanel
{
    private readonly EditorContext _context;
    private Guid? _renamingId;
    private string _renameBuffer = string.Empty;
    private bool _focusRename;

    public override string Title => "Hierarchy";

    public HierarchyPanel(EditorContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
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
        DrawToolbar();
        SceneDocument document = _context.Document;
        foreach (GameObject root in document.Scene.RootObjects.ToArray())
            DrawObjectNode(root);
        if (ImGui.BeginDragDropTarget())
        {
            Guid? dropped = ImGuiDragPayloads.AcceptGameObject();
            if (dropped is Guid id)
                _context.Hierarchy.Reparent(document.Scene.FindById(id), null);
            ImGui.EndDragDropTarget();
        }
        if (ImGui.BeginPopupContextWindow("HierarchyContext", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            if (ImGui.MenuItem("Create Empty"))
                _context.Hierarchy.Create();
            ImGui.EndPopup();
        }
        ImGui.End();
    }

    public void BeginRenameSelected()
    {
        if (_context.Document.SelectedObject is { } selected)
            BeginRename(selected);
    }

    private void DrawToolbar()
    {
        GameObject? selected = _context.Document.SelectedObject;
        if (ImGui.SmallButton("+ Create"))
            _context.Hierarchy.Create(parent: selected);
        ImGui.SameLine(0f, 4f);
        if (ImGui.SmallButton("Actions"))
            ImGui.OpenPopup("HierarchyActions");
        if (ImGui.BeginPopup("HierarchyActions"))
        {
            ImGui.BeginDisabled(selected is null);
            if (ImGui.MenuItem("Duplicate", "Ctrl+D"))
                _context.Hierarchy.Duplicate(selected);
            if (ImGui.MenuItem("Delete", "Delete"))
                _context.Hierarchy.Delete(selected);
            ImGui.EndDisabled();
            ImGui.EndPopup();
        }
        ImGui.Spacing();
        ImGui.Separator();
    }

    private void DrawObjectNode(GameObject gameObject)
    {
        SceneDocument document = _context.Document;
        ImGui.PushID(gameObject.Id.ToString("N"));
        if (_renamingId == gameObject.Id)
        {
            if (_focusRename)
            {
                ImGui.SetKeyboardFocusHere();
                _focusRename = false;
            }
            bool submitted = ImGui.InputText("##Rename", ref _renameBuffer, 256, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
            bool cancel = ImGui.IsKeyPressed(ImGuiKey.Escape);
            if (submitted)
            {
                _context.Hierarchy.Rename(gameObject, _renameBuffer);
                _renamingId = null;
            }
            else if (cancel)
            {
                _renamingId = null;
            }
            else if (ImGui.IsItemDeactivated())
            {
                _context.Hierarchy.Rename(gameObject, _renameBuffer);
                _renamingId = null;
            }
            ImGui.PopID();
            return;
        }

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow
            | ImGuiTreeNodeFlags.SpanAvailWidth
            | ImGuiTreeNodeFlags.FramePadding;
        if (document.SelectedObjectId == gameObject.Id)
            flags |= ImGuiTreeNodeFlags.Selected;
        if (gameObject.Children.Count == 0)
            flags |= ImGuiTreeNodeFlags.Leaf;
        if (!gameObject.Active)
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.MutedText);
        bool treeOpen = ImGui.TreeNodeEx($"{gameObject.Name}##Node", flags);
        if (!gameObject.Active)
            ImGui.PopStyleColor();
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
        {
            _context.SelectScene();
            document.Select(gameObject);
        }
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            BeginRename(gameObject);
        if (ImGui.BeginDragDropSource())
        {
            ImGuiDragPayloads.SetGameObject(gameObject.Id);
            ImGui.TextUnformatted(gameObject.Name);
            ImGui.EndDragDropSource();
        }
        if (ImGui.BeginDragDropTarget())
        {
            Guid? dropped = ImGuiDragPayloads.AcceptGameObject();
            if (dropped is Guid id)
                _context.Hierarchy.Reparent(document.Scene.FindById(id), gameObject);
            ImGui.EndDragDropTarget();
        }
        DrawContextMenu(gameObject);
        if (treeOpen)
        {
            foreach (GameObject child in gameObject.Children.ToArray())
                DrawObjectNode(child);
            ImGui.TreePop();
        }
        ImGui.PopID();
    }

    private void DrawContextMenu(GameObject gameObject)
    {
        if (!ImGui.BeginPopupContextItem("ObjectContext"))
            return;
        _context.Document.Select(gameObject);
        if (ImGui.MenuItem("Create Child"))
            _context.Hierarchy.Create(parent: gameObject);
        if (ImGui.MenuItem("Rename", "F2"))
            BeginRename(gameObject);
        if (ImGui.MenuItem("Duplicate", "Ctrl+D"))
            _context.Hierarchy.Duplicate(gameObject);
        if (ImGui.MenuItem("Delete", "Delete"))
            _context.Hierarchy.Delete(gameObject);
        ImGui.EndPopup();
    }

    private void BeginRename(GameObject gameObject)
    {
        _renamingId = gameObject.Id;
        _renameBuffer = gameObject.Name;
        _focusRename = true;
    }
}
