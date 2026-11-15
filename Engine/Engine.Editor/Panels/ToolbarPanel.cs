using Engine.Editor.Documents;
using Engine.Scene;
using ImGuiNET;

namespace Engine.Editor.Panels;

public sealed class ToolbarPanel
{
    private readonly EditorContext _context;
    private string _pathBuffer = string.Empty;
    private bool _showOpen;
    private bool _showSaveAs;
    private string? _error;

    public ToolbarPanel(EditorContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public void Draw(Action? drawAdditionalMenus = null)
    {
        SceneDocument document = _context.Document;
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New", "Ctrl+N"))
                    document.New();
                if (ImGui.MenuItem("Open...", "Ctrl+O"))
                    OpenPathDialog(saveAs: false);
                if (ImGui.MenuItem("Save", "Ctrl+S"))
                {
                    if (_context.SelectedMaterial is null && document.Path is null)
                        OpenPathDialog(saveAs: true);
                    else
                        Try(_context.SaveActive);
                }
                if (ImGui.MenuItem("Save All"))
                {
                    if (Try(_context.SaveAll) == false && document.IsDirty && document.Path is null)
                        OpenPathDialog(saveAs: true);
                }
                if (ImGui.MenuItem("Save Scene As...", "Ctrl+Shift+S"))
                    OpenPathDialog(saveAs: true);
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Edit"))
            {
                if (ImGui.MenuItem(_context.History.UndoName is { } undo ? $"Undo {undo}" : "Undo", "Ctrl+Z", false, _context.History.CanUndo))
                    _context.Undo();
                if (ImGui.MenuItem(_context.History.RedoName is { } redo ? $"Redo {redo}" : "Redo", "Ctrl+Y", false, _context.History.CanRedo))
                    _context.Redo();
                ImGui.Separator();
                if (ImGui.MenuItem("Duplicate", "Ctrl+D", false, _context.SelectedMaterial is null && document.SelectedObject is not null))
                    _context.Hierarchy.Duplicate(document.SelectedObject);
                if (ImGui.MenuItem("Delete", "Delete", false, _context.SelectedMaterial is null && document.SelectedObject is not null))
                    _context.Hierarchy.Delete(document.SelectedObject);
                ImGui.EndMenu();
            }
            drawAdditionalMenus?.Invoke();
            ImGui.Separator();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(10f, 4f));
            DrawModeButton("Move", TransformMode.Translate);
            ImGui.SameLine(0f, 4f);
            DrawModeButton("Rotate", TransformMode.Rotate);
            ImGui.SameLine(0f, 4f);
            DrawModeButton("Scale", TransformMode.Scale);
            ImGui.SameLine(0f, 14f);
            bool localScale = _context.TransformMode == TransformMode.Scale;
            ImGui.BeginDisabled(localScale);
            bool local = localScale || _context.TransformSpace == TransformSpace.Local;
            EditorTheme.PushSelectedButton(local);
            if (ImGui.SmallButton("Local"))
                _context.TransformSpace = TransformSpace.Local;
            EditorTheme.PopSelectedButton(local);
            ImGui.SameLine(0f, 4f);
            EditorTheme.PushSelectedButton(!local);
            if (ImGui.SmallButton("Global"))
                _context.TransformSpace = TransformSpace.Global;
            EditorTheme.PopSelectedButton(!local);
            ImGui.EndDisabled();
            if (localScale && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Scale uses local axes.");
            ImGui.PopStyleVar();
            ImGui.SameLine(0f, 14f);
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.MutedText);
            ImGui.TextUnformatted(_context.ActiveTitle);
            ImGui.PopStyleColor();
            ImGui.EndMainMenuBar();
        }
        DrawPathDialog();
    }

    public void RequestOpen() => OpenPathDialog(saveAs: false);

    public void RequestSaveAs() => OpenPathDialog(saveAs: true);

    private void DrawModeButton(string label, TransformMode mode)
    {
        bool selected = _context.TransformMode == mode;
        EditorTheme.PushSelectedButton(selected);
        if (ImGui.SmallButton(label))
            _context.TransformMode = mode;
        EditorTheme.PopSelectedButton(selected);
    }

    private void OpenPathDialog(bool saveAs)
    {
        _pathBuffer = _context.Document.Path
            ?? Path.Combine(_context.AssetsRoot, "Scenes", "Untitled.scene");
        _showSaveAs = saveAs;
        _showOpen = !saveAs;
        _error = null;
    }

    private void DrawPathDialog()
    {
        if (_showOpen)
            ImGui.OpenPopup("Open Scene");
        if (_showSaveAs)
            ImGui.OpenPopup("Save Scene As");
        DrawDialog("Open Scene", ref _showOpen, "Open", path => _context.Document.Open(path));
        DrawDialog("Save Scene As", ref _showSaveAs, "Save", path => _context.Document.SaveAs(path));
    }

    private void DrawDialog(string title, ref bool requested, string actionLabel, Action<string> action)
    {
        bool visible = true;
        if (!ImGui.BeginPopupModal(title, ref visible, ImGuiWindowFlags.AlwaysAutoResize))
            return;
        requested = false;
        ImGui.SetNextItemWidth(560f);
        ImGui.InputText("Path", ref _pathBuffer, 2048);
        if (_error is not null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Error);
            ImGui.TextWrapped(_error);
            ImGui.PopStyleColor();
        }
        if (ImGui.Button(actionLabel))
        {
            if (Try(() => action(_pathBuffer)))
            {
                visible = false;
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            visible = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private bool Try(Action action)
    {
        try
        {
            action();
            _error = null;
            return true;
        }
        catch (Exception exception)
        {
            _error = exception.Message;
            _context.Error = exception.Message;
            return false;
        }
    }

    private static string UndoLabel(SceneDocument document) =>
        document.History.UndoName is { } name ? $"Undo {name}" : "Undo";

    private static string RedoLabel(SceneDocument document) =>
        document.History.RedoName is { } name ? $"Redo {name}" : "Redo";
}
