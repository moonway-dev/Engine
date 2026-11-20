using Engine.Editor.Documents;
using Engine.Scene;
using ImGuiNET;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector4 = System.Numerics.Vector4;

namespace Engine.Editor.Panels;

public sealed class ToolbarPanel
{
    private const float IconSize = 11f;
    private const float IconFramePadding = 3f;
    private static readonly NumericsVector2 IconUvMax = new(1f, 1f);
    private static readonly NumericsVector4 Transparent = new(0f, 0f, 0f, 0f);

    private readonly EditorContext _context;
    private readonly PlayModeController? _playMode;
    private readonly Func<string, IntPtr>? _icons;
    private string _pathBuffer = string.Empty;
    private bool _showOpen;
    private bool _showSaveAs;
    private string? _error;

    public ToolbarPanel(EditorContext context, PlayModeController? playMode = null, Func<string, IntPtr>? icons = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _playMode = playMode;
        _icons = icons;
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
            DrawPlayControls();
            ImGui.EndMainMenuBar();
        }
        DrawPathDialog();
    }

    public void RequestOpen() => OpenPathDialog(saveAs: false);

    public void RequestSaveAs() => OpenPathDialog(saveAs: true);

    private void DrawPlayControls()
    {
        if (_playMode is null)
            return;
        EditorPlayState state = _playMode.State;
        float buttonWidth = IconSize + IconFramePadding * 2f;
        float totalWidth = buttonWidth * 3 + ImGui.GetStyle().ItemSpacing.X * 2f;
        float right = ImGui.GetWindowWidth() - totalWidth;
        ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), right));

        bool playing = state != EditorPlayState.Stopped;
        if (DrawIconButton(playing ? "stop" : "play", "##PlayStop",
                playing ? EditorTheme.Error : EditorTheme.Success))
        {
            if (playing)
                _playMode.ExitPlay();
            else
                _playMode.EnterPlay();
        }
        DrawIconButtonTooltip(playing ? "Stop (Ctrl+P)" : "Play (Ctrl+P)");
        ImGui.SameLine();

        if (DrawIconButton(state == EditorPlayState.Paused ? "play" : "pause",
                "##PauseResume",
                state == EditorPlayState.Playing ? EditorTheme.Warning : EditorTheme.MutedText,
                enabled: state != EditorPlayState.Stopped))
        {
            if (state == EditorPlayState.Playing)
                _playMode.Pause();
            else if (state == EditorPlayState.Paused)
                _playMode.Resume();
        }
        DrawIconButtonTooltip(state == EditorPlayState.Paused ? "Resume (Ctrl+Shift+P)" : "Pause (Ctrl+Shift+P)");
        ImGui.SameLine();

        if (DrawIconButton("step", "##Step", EditorTheme.MutedText, enabled: state == EditorPlayState.Paused))
            _playMode.Step();
        DrawIconButtonTooltip("Step One Frame (F10)");
    }

    private bool DrawIconButton(string icon, string id, NumericsVector4 tint, bool enabled = true)
    {
        if (!enabled)
            ImGui.BeginDisabled();
        IntPtr texture = _icons?.Invoke(icon) ?? IntPtr.Zero;
        bool clicked;
        if (texture == IntPtr.Zero)
        {
            clicked = ImGui.Button(id, new NumericsVector2(IconSize + IconFramePadding * 2f, IconSize + IconFramePadding * 2f));
        }
        else
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new NumericsVector2(IconFramePadding, IconFramePadding));
            ImGui.PushStyleColor(ImGuiCol.Button, Transparent);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new NumericsVector4(1f, 1f, 1f, 0.08f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new NumericsVector4(1f, 1f, 1f, 0.16f));
            clicked = ImGui.ImageButton(id, texture, new NumericsVector2(IconSize, IconSize), NumericsVector2.Zero, IconUvMax, Transparent, tint);
            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar();
        }
        if (!enabled)
            ImGui.EndDisabled();
        return clicked && enabled;
    }

    private static void DrawIconButtonTooltip(string tooltip)
    {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
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
        ImGui.SetNextItemWidth(420f);
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
