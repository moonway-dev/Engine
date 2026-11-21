using System.Diagnostics;
using Engine.Editor.Build;
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
    private readonly Action<string>? _openProject;
    private string _pathBuffer = string.Empty;
    private bool _showOpenProject;
    private bool _showSaveAs;
    private string? _error;

    public ToolbarPanel(EditorContext context, PlayModeController? playMode = null, Func<string, IntPtr>? icons = null, Action<string>? openProject = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _playMode = playMode;
        _icons = icons;
        _openProject = openProject;
    }

    public void Draw(Action? drawAdditionalMenus = null)
    {
        SceneDocument document = _context.Document;
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New Scene", "Ctrl+N"))
                    document.New();
                if (ImGui.MenuItem("Open Project...", "Ctrl+O"))
                    OpenProjectDialog();
                ImGui.Separator();
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
                ImGui.Separator();
                if (ImGui.MenuItem("Build Settings..."))
                    _showBuildSettings = true;
                if (ImGui.MenuItem("Build Game", !_buildRunning))
                    RunBuild();
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
        DrawOpenProjectDialog();
        DrawBuildSettingsWindow();
    }

    public void RequestOpen() => OpenProjectDialog();

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
        _error = null;
    }

    private void OpenProjectDialog()
    {
        _pathBuffer = FindProjectFile(_context.AssetsRoot) ?? Path.Combine(_context.AssetsRoot, "Project.project");
        _showOpenProject = true;
        _error = null;
    }

    private static string? FindProjectFile(string directory)
    {
        string? current = directory;
        for (int depth = 0; depth < 3 && current is not null; depth++)
        {
            try
            {
                string? match = Directory.EnumerateFiles(current, "*.project", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (match is not null)
                    return Path.GetFullPath(match);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
            current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        return null;
    }

    private void DrawOpenProjectDialog()
    {
        if (_showOpenProject)
            ImGui.OpenPopup("Open Project");
        bool visible = true;
        if (!ImGui.BeginPopupModal("Open Project", ref visible, ImGuiWindowFlags.AlwaysAutoResize))
            return;
        _showOpenProject = false;
        ImGui.TextWrapped("Select a .project file to switch the editor to that project.");
        ImGui.Separator();
        ImGui.SetNextItemWidth(460f);
        ImGui.InputText("##ProjectPath", ref _pathBuffer, 2048);
        if (_error is not null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Error);
            ImGui.TextWrapped(_error);
            ImGui.PopStyleColor();
        }
        if (ImGui.Button("Open"))
        {
            string candidate = _pathBuffer.Trim();
            if (!File.Exists(candidate))
                _error = $"File not found: {candidate}";
            else if (!candidate.EndsWith(".project", StringComparison.OrdinalIgnoreCase))
                _error = "Please choose a .project file.";
            else
            {
                string resolved = Path.GetFullPath(candidate);
                _openProject?.Invoke(resolved);
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

    private void DrawPathDialog()
    {
        if (_showSaveAs)
            ImGui.OpenPopup("Save Scene As");
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

    private bool _showBuildSettings;
    private bool _buildRunning;
    private BuildRunner? _runner;
    private ProjectSettings? _projectSettings;
    private string? _projectSettingsPath;
    private string? _engineRoot;
    private string _productName = "Game";
    private string _version = "1.0.0";
    private int _platformIndex;
    private int _modeIndex;
    private int _configurationIndex;
    private readonly List<string> _buildLog = [];
    private BuildReport? _lastReport;
    private string? _buildError;
    private static readonly string[] Platforms = ["win-x64", "linux-x64", "osx-arm64"];

    public void ConfigureBuild(ProjectSettings? projectSettings, string? projectSettingsPath, string? engineRoot)
    {
        _projectSettings = projectSettings;
        _projectSettingsPath = projectSettingsPath;
        _engineRoot = engineRoot;
        if (projectSettings is null || _productNameLoaded)
            return;
        _productNameLoaded = true;
        EngineBuildProfile profile = projectSettings.Build;
        _productName = string.IsNullOrWhiteSpace(profile.ProductName) ? "Game" : profile.ProductName;
        _version = string.IsNullOrWhiteSpace(profile.Version) ? "1.0.0" : profile.Version;
        _platformIndex = Math.Clamp(Array.IndexOf(Platforms, profile.Platform), 0, Platforms.Length - 1);
        _modeIndex = profile.Mode.Equals("Managed", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _configurationIndex = profile.Configuration.Equals("Release", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private bool _productNameLoaded;

    private EngineBuildProfile Profile => new()
    {
        ProductName = _productName.Trim(),
        Version = _version.Trim(),
        Platform = Platforms[_platformIndex],
        Mode = _modeIndex == 1 ? "Managed" : "Aot",
        Scenes = _projectSettings?.Build.Scenes ?? [],
        StartupScene = _projectSettings?.Build.StartupScene ?? string.Empty,
        Configuration = _configurationIndex == 1 ? "Release" : "Debug",
        OutputDirectory = _projectSettings?.Build.OutputDirectory ?? "Builds",
    };

    private void RunBuild()
    {
        if (_buildRunning || _projectSettings is null)
            return;
        _buildError = null;
        _lastReport = null;
        _buildLog.Clear();
        _buildRunning = true;
        _projectSettings.Build.ProductName = _productName.Trim();
        _projectSettings.Build.Version = _version.Trim();
        _projectSettings.Build.Platform = Platforms[_platformIndex];
        _projectSettings.Build.Mode = _modeIndex == 1 ? "Managed" : "Aot";
        _projectSettings.Build.Configuration = _configurationIndex == 1 ? "Release" : "Debug";
        try
        {
            _projectSettings.Save(_projectSettingsPath!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _buildLog.Add($"warning: could not save build settings: {exception.Message}");
        }
        BuildRequest request = new()
        {
            ProjectRoot = _context.AssetsRoot,
            EngineRoot = string.IsNullOrWhiteSpace(_engineRoot) ? FindEngineRoot(_context.AssetsRoot) ?? _context.AssetsRoot : _engineRoot!,
            Profile = Profile,
            Log = line => _buildLog.Add(line),
        };
        _runner = new BuildRunner(request);
        Task.Run(() =>
        {
            try
            {
                BuildReport report = _runner.Run();
                _lastReport = report;
                if (!report.Success)
                    _buildError = report.Errors.FirstOrDefault();
            }
            catch (Exception exception)
            {
                _buildError = exception.Message;
                _buildLog.Add($"error: {exception.Message}");
            }
            finally
            {
                _buildRunning = false;
            }
        });
    }

    private static string? FindEngineRoot(string directory)
    {
        string? current = directory;
        for (int depth = 0; depth < 8 && current is not null; depth++)
        {
            if (File.Exists(Path.Combine(current, "Engine.sln")))
                return current;
            current = Path.GetDirectoryName(current);
        }
        return null;
    }

    private void DrawBuildSettingsWindow()
    {
        if (!_showBuildSettings)
            return;
        if (ImGui.Begin("Build Settings", ref _showBuildSettings))
        {
            ImGui.InputText("Product Name", ref _productName, 128);
            ImGui.InputText("Version", ref _version, 32);
            ImGui.Combo("Platform", ref _platformIndex, Platforms, Platforms.Length);
            ImGui.Combo("Mode", ref _modeIndex, BuildModes, BuildModes.Length);
            ImGui.Combo("Configuration", ref _configurationIndex, BuildConfigurations, BuildConfigurations.Length);
            if (_configurationIndex == 0)
                ImGui.TextDisabled("Debug: same layout plus the build report for launcher tooling.");
            else
                ImGui.TextDisabled("Release: distributable only — no report or auxiliary files.");
            if (_modeIndex == 0)
                ImGui.TextDisabled("NativeAOT: scripts compiled into the native executable, no IL.");
            else
                ImGui.TextDisabled("Managed single-file: .NET runtime bundled, scripts shipped as a compiled assembly.");

            EngineBuildProfile current = _projectSettings?.Build ?? new EngineBuildProfile();
            List<string> available = ListSceneFiles(_context.AssetsRoot);
            if (available.Count == 0)
                ImGui.TextDisabled("No .scene files found in the project.");
            else
            {
                ImGui.SeparatorText("Scenes In Build");
                HashSet<string> selected = new(current.Scenes, StringComparer.OrdinalIgnoreCase);
                bool startupMissing = !string.IsNullOrEmpty(current.StartupScene) && !available.Contains(current.StartupScene, StringComparer.OrdinalIgnoreCase);
                foreach (string scene in available)
                {
                    bool included = selected.Contains(scene);
                    if (ImGui.Checkbox(scene, ref included))
                    {
                        if (included)
                            current.Scenes.Add(scene);
                        else
                            current.Scenes.RemoveAll(value => value.Equals(scene, StringComparison.OrdinalIgnoreCase));
                    }
                }
                ImGui.SeparatorText("Startup Scene");
                string[] startupOptions = [.. available, "(first included)"];
                int startupIndex = Math.Max(0, available.FindIndex(value => value.Equals(current.StartupScene, StringComparison.OrdinalIgnoreCase)));
                if (startupMissing)
                {
                    startupOptions = [current.StartupScene, .. startupOptions];
                    startupIndex = 0;
                }
                if (ImGui.Combo("##StartupScene", ref startupIndex, startupOptions, startupOptions.Length))
                {
                    int offset = startupMissing ? 1 : 0;
                    current.StartupScene = startupIndex - offset >= 0 && startupIndex - offset < available.Count
                        ? available[startupIndex - offset]
                        : string.Empty;
                }
            }
            ImGui.SeparatorText("Build");
            ImGui.BeginDisabled(_buildRunning || _projectSettings is null);
            if (ImGui.Button(_buildRunning ? "Building..." : "Build"))
                RunBuild();
            ImGui.EndDisabled();
            ImGui.SameLine();
            string? output = _lastReport?.OutputDirectory;
            ImGui.BeginDisabled(output is null || !Directory.Exists(output));
            if (ImGui.Button("Open Folder") && output is not null)
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{output}\"") { UseShellExecute = true });
            ImGui.EndDisabled();
            if (_buildError is not null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Error);
                ImGui.TextWrapped($"Build failed: {_buildError}");
                ImGui.PopStyleColor();
            }
            if (_lastReport is { } report)
            {
                ImGui.Text($"Last build: {report.Success} in {report.Duration.TotalSeconds:F1}s, {report.FileCount} files, {report.TotalBytes / (1024.0 * 1024.0):F1} MB");
                if (report.Warnings.Count > 0)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Warning);
                    ImGui.TextWrapped(string.Join("\n", report.Warnings.Take(8)));
                    ImGui.PopStyleColor();
                }
            }
            if (_buildLog.Count > 0)
            {
                ImGui.SeparatorText("Build Log");
                ImGui.BeginChild("build_log", new NumericsVector2(0f, 180f), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar);
                foreach (string line in _buildLog.TakeLast(200))
                    ImGui.TextUnformatted(line);
                if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4f)
                    ImGui.SetScrollHereY(1f);
                ImGui.EndChild();
            }
        }
        ImGui.End();
    }

    private static List<string> ListSceneFiles(string projectRoot)
    {
        string scenesRoot = Path.Combine(projectRoot, "Scenes");
        List<string> scenes = [];
        if (!Directory.Exists(scenesRoot))
            return scenes;
        foreach (string file in Directory.EnumerateFiles(scenesRoot, "*.scene", SearchOption.AllDirectories))
        {
            try
            {
                scenes.Add(Path.GetRelativePath(projectRoot, file).Replace(Path.DirectorySeparatorChar, '/'));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
        scenes.Sort(StringComparer.OrdinalIgnoreCase);
        return scenes;
    }

    private static readonly string[] BuildModes = ["Native AOT", "Managed"];
    private static readonly string[] BuildConfigurations = ["Debug", "Release"];

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
