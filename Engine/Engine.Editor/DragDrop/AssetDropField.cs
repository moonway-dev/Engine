using Engine.Editor.Models;
using ImGuiNET;
using System.Numerics;

namespace Engine.Editor.DragDrop;

internal static class AssetDropField
{
    public static AssetDragPayload? Draw(string id, string displayName, AssetKind acceptedKind)
    {
        ImGui.PushID(id);
        bool assigned = !string.IsNullOrWhiteSpace(displayName);
        ImGui.PushStyleColor(ImGuiCol.Button, !assigned
            ? new Vector4(0.055f, 0.055f, 0.055f, 1f)
            : new Vector4(0.10f, 0.16f, 0.25f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.13f, 0.20f, 0.30f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.17f, 0.29f, 0.46f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.Border, !assigned
            ? new Vector4(0.20f, 0.20f, 0.20f, 1f)
            : EditorTheme.AccentActive);
        string text = assigned ? displayName : "None";
        if (!assigned)
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.MutedText);
        ImGui.Button(text + "##AssetDropField", new Vector2(-1f, 0f));
        if (!assigned)
            ImGui.PopStyleColor();

        AssetDragPayload? dropped = null;
        if (ImGui.BeginDragDropTarget())
        {
            ImGui.PushStyleColor(ImGuiCol.DragDropTarget, EditorTheme.AccentHovered);
            AssetDragPayload? payload = ImGuiDragPayloads.AcceptAsset();
            if (payload is { } value && value.Kind == acceptedKind)
                dropped = value;
            ImGui.PopStyleColor();
            ImGui.EndDragDropTarget();
        }
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar();
        ImGui.PopID();
        return dropped;
    }
}
