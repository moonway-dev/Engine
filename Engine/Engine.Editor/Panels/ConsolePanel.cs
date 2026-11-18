using Engine.Core;
using Engine.Editor.Models;
using ImGuiNET;

namespace Engine.Editor.Panels;

public sealed class ConsolePanel : EditorPanel
{
    private readonly MemoryLogSink _sink;
    private readonly ConsoleFilterModel _filter = new();
    private bool _autoScroll = true;
    private int _lastEntryCount;

    public override string Title => "Console";

    public ConsoleFilterModel Filter => _filter;

    public ConsolePanel(MemoryLogSink sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
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
        if (ImGui.Button("Clear"))
        {
            _sink.Clear();
            _lastEntryCount = 0;
        }
        ImGui.SameLine();
        ImGui.Checkbox("Auto-scroll", ref _autoScroll);
        ImGui.SameLine(0f, 12f);
        if (ImGui.SmallButton("All"))
            _filter.EnableAllLevels();
        ImGui.SameLine(0f, 4f);
        DrawLevelToggle("T", LogLevel.Trace);
        ImGui.SameLine(0f, 4f);
        DrawLevelToggle("D", LogLevel.Debug);
        ImGui.SameLine(0f, 4f);
        DrawLevelToggle("I", LogLevel.Information);
        ImGui.SameLine(0f, 4f);
        DrawLevelToggle("W", LogLevel.Warning);
        ImGui.SameLine(0f, 4f);
        DrawLevelToggle("E", LogLevel.Error);
        ImGui.SameLine(0f, 4f);
        DrawLevelToggle("C", LogLevel.Critical);

        string category = _filter.Category;
        ImGui.SetNextItemWidth(140f);
        if (ImGui.InputTextWithHint("##Category", "Category", ref category, 256))
            _filter.Category = category;
        ImGui.SameLine();
        string text = _filter.Text;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputTextWithHint("##ConsoleFilter", "Search", ref text, 512))
            _filter.Text = text;
        ImGui.Separator();

        IReadOnlyList<LogEntry> entries = _sink.GetEntries();
        bool hasNewEntries = entries.Count > _lastEntryCount;
        _lastEntryCount = entries.Count;
        if (ImGui.BeginChild("ConsoleEntries", new System.Numerics.Vector2(0f, 0f), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
        {
            foreach (LogEntry entry in entries)
            {
                if (!_filter.Matches(entry))
                    continue;
                ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.LogColor(entry.Level));
                ImGui.TextUnformatted($"{entry.Timestamp:HH:mm:ss.fff} [{entry.Level}] [{entry.Category}] {entry.Message}");
                ImGui.PopStyleColor();
                if (entry.Exception is not null)
                {
                    ImGui.Indent();
                    ImGui.TextWrapped(entry.Exception.ToString());
                    ImGui.Unindent();
                }
            }
            if (_autoScroll && hasNewEntries)
                ImGui.SetScrollHereY(1f);
        }
        ImGui.EndChild();
        ImGui.End();
    }

    private void DrawLevelToggle(string label, LogLevel level)
    {
        bool enabled = _filter.IsLevelEnabled(level);
        System.Numerics.Vector4 color = EditorTheme.LogColor(level);
        EditorTheme.PushSelectedButton(enabled);
        ImGui.PushStyleColor(ImGuiCol.Text, enabled ? EditorTheme.Text : color);
        if (ImGui.SmallButton(label))
            _filter.SetLevelEnabled(level, !enabled);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(level.ToString());
        ImGui.PopStyleColor();
        EditorTheme.PopSelectedButton(enabled);
    }
}
