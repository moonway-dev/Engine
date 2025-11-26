using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace Engine.Editor;

public class FileBrowserWindow
{
    private string _currentPath;
    private string _selectedFile = string.Empty;
    private string[] _fileExtensions;
    private string _title;
    private bool _visible;
    private string _filterText = string.Empty;
    private List<string> _history = new List<string>();
    private int _historyIndex = -1;

    public bool Visible
    {
        get => _visible;
        set => _visible = value;
    }

    public string? SelectedFilePath { get; private set; }

    public FileBrowserWindow(string title, string[] fileExtensions, string? initialPath = null)
    {
        _title = title;
        _fileExtensions = fileExtensions;
        _currentPath = initialPath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        
        if (!Directory.Exists(_currentPath))
        {
            _currentPath = Environment.CurrentDirectory;
        }
    }

    public bool Show()
    {
        if (!_visible)
            return false;

        bool result = false;
        ImGui.SetNextWindowSize(new Vector2(700, 500), ImGuiCond.FirstUseEver);
        
        if (ImGui.Begin(_title, ref _visible, ImGuiWindowFlags.None))
        {
            DrawNavigationBar();

            ImGui.Separator();

            ImGui.Text("Filter:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            if (ImGui.InputText("##filter", ref _filterText, 256))
            {
            }

            ImGui.Separator();

            DrawFileList();

            ImGui.Separator();

            ImGui.Text("Selected:");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1.0f, 1.0f), _selectedFile);

            ImGui.Separator();

            ImGui.BeginDisabled(string.IsNullOrEmpty(_selectedFile));
            if (ImGui.Button("Open", new Vector2(120, 0)))
            {
                SelectedFilePath = Path.Combine(_currentPath, _selectedFile);
                result = true;
                _visible = false;
            }
            ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                SelectedFilePath = null;
                _visible = false;
            }
        }
        ImGui.End();

        return result;
    }

    private void DrawNavigationBar()
    {
        ImGui.BeginDisabled(_historyIndex <= 0);
        if (ImGui.Button("←"))
        {
            if (_historyIndex > 0)
            {
                _historyIndex--;
                _currentPath = _history[_historyIndex];
            }
        }
        ImGui.EndDisabled();

        ImGui.SameLine();

        ImGui.BeginDisabled(_historyIndex >= _history.Count - 1);
        if (ImGui.Button("→"))
        {
            if (_historyIndex < _history.Count - 1)
            {
                _historyIndex++;
                _currentPath = _history[_historyIndex];
            }
        }
        ImGui.EndDisabled();

        ImGui.SameLine();

        ImGui.BeginDisabled(_currentPath == Path.GetPathRoot(_currentPath));
        if (ImGui.Button("↑"))
        {
            NavigateToParent();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();

        ImGui.SetNextItemWidth(-1);
        string pathText = _currentPath;
        if (ImGui.InputText("##path", ref pathText, 512, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            if (Directory.Exists(pathText))
            {
                NavigateTo(pathText);
            }
        }
    }

    private void DrawFileList()
    {
        if (ImGui.BeginChild("FileList", new Vector2(0, -60), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
        {
            try
            {
                var directories = Directory.GetDirectories(_currentPath)
                    .Select(d => new DirectoryInfo(d))
                    .Where(d => string.IsNullOrEmpty(_filterText) || d.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => d.Name)
                    .ToList();

                foreach (var dir in directories)
                {
                    ImGui.PushID(dir.FullName);
                    bool isSelected = _selectedFile == dir.Name && Directory.Exists(Path.Combine(_currentPath, dir.Name));
                    
                    if (ImGui.Selectable($"📁 {dir.Name}", isSelected))
                    {
                        NavigateTo(dir.FullName);
                        _selectedFile = string.Empty;
                    }
                    ImGui.PopID();
                }

                var files = Directory.GetFiles(_currentPath)
                    .Select(f => new FileInfo(f))
                    .Where(f => IsFileExtensionAllowed(f.Extension))
                    .Where(f => string.IsNullOrEmpty(_filterText) || f.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f.Name)
                    .ToList();

                foreach (var file in files)
                {
                    ImGui.PushID(file.FullName);
                    bool isSelected = _selectedFile == file.Name;
                    
                    if (ImGui.Selectable($"📄 {file.Name}", isSelected))
                    {
                        _selectedFile = file.Name;
                    }
                    ImGui.PopID();
                }
            }
            catch (Exception ex)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error: {ex.Message}");
            }
        }
        ImGui.EndChild();
    }

    private bool IsFileExtensionAllowed(string extension)
    {
        if (_fileExtensions == null || _fileExtensions.Length == 0)
            return true;

        extension = extension.ToLowerInvariant();
        if (extension.StartsWith("."))
            extension = extension.Substring(1);

        return _fileExtensions.Any(ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    private void NavigateTo(string path)
    {
        if (Directory.Exists(path))
        {
            if (_historyIndex >= 0 && _historyIndex < _history.Count)
            {
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
            }
            
            _history.Add(path);
            _historyIndex = _history.Count - 1;
            
            if (_history.Count > 50)
            {
                _history.RemoveAt(0);
                _historyIndex--;
            }

            _currentPath = path;
            _selectedFile = string.Empty;
        }
    }

    private void NavigateToParent()
    {
        var parent = Directory.GetParent(_currentPath);
        if (parent != null && !string.IsNullOrEmpty(parent.FullName) && Directory.Exists(parent.FullName))
        {
            NavigateTo(parent.FullName);
        }
    }

    public void Reset()
    {
        _selectedFile = string.Empty;
        SelectedFilePath = null;
        _filterText = string.Empty;
    }
}

