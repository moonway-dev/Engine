using ImGuiNET;
using System.Numerics;

namespace Engine.Editor;

public static class EditorTheme
{
    public static readonly Vector4 Accent = Color(68, 138, 255);
    public static readonly Vector4 AccentHovered = Color(112, 169, 255);
    public static readonly Vector4 AccentActive = Color(43, 99, 196);
    public static readonly Vector4 Text = Color(224, 232, 240);
    public static readonly Vector4 MutedText = Color(112, 128, 146);
    public static readonly Vector4 Error = Color(255, 91, 105);
    public static readonly Vector4 Warning = Color(255, 190, 76);
    public static readonly Vector4 Success = Color(72, 211, 145);

    public static void Apply()
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        style.WindowPadding = new Vector2(5f, 5f);
        style.FramePadding = new Vector2(4f, 2f);
        style.CellPadding = new Vector2(4f, 2f);
        style.ItemSpacing = new Vector2(4f, 3f);
        style.ItemInnerSpacing = new Vector2(4f, 2f);
        style.TouchExtraPadding = Vector2.Zero;
        style.IndentSpacing = 14f;
        style.ScrollbarSize = 10f;
        style.GrabMinSize = 8f;
        style.WindowBorderSize = 1f;
        style.ChildBorderSize = 1f;
        style.PopupBorderSize = 1f;
        style.FrameBorderSize = 0f;
        style.TabBorderSize = 0f;
        style.WindowRounding = 5f;
        style.ChildRounding = 4f;
        style.FrameRounding = 3f;
        style.PopupRounding = 5f;
        style.ScrollbarRounding = 6f;
        style.GrabRounding = 3f;
        style.TabRounding = 5f;
        style.WindowTitleAlign = new Vector2(0.02f, 0.5f);
        style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        style.SelectableTextAlign = new Vector2(0f, 0.5f);

        RangeAccessor<Vector4> colors = style.Colors;
        colors[(int)ImGuiCol.Text] = Text;
        colors[(int)ImGuiCol.TextDisabled] = MutedText;
        colors[(int)ImGuiCol.WindowBg] = Color(24, 28, 36);
        colors[(int)ImGuiCol.ChildBg] = Color(21, 25, 33);
        colors[(int)ImGuiCol.PopupBg] = Color(29, 34, 43, 252);
        colors[(int)ImGuiCol.Border] = Color(48, 55, 68);
        colors[(int)ImGuiCol.BorderShadow] = Color(0, 0, 0, 100);
        colors[(int)ImGuiCol.FrameBg] = Color(31, 36, 46);
        colors[(int)ImGuiCol.FrameBgHovered] = Color(40, 48, 62);
        colors[(int)ImGuiCol.FrameBgActive] = Color(47, 61, 83);
        colors[(int)ImGuiCol.TitleBg] = Color(20, 24, 31);
        colors[(int)ImGuiCol.TitleBgActive] = Color(28, 33, 42);
        colors[(int)ImGuiCol.TitleBgCollapsed] = Color(18, 22, 29);
        colors[(int)ImGuiCol.MenuBarBg] = Color(20, 24, 31);
        colors[(int)ImGuiCol.ScrollbarBg] = Color(19, 23, 30);
        colors[(int)ImGuiCol.ScrollbarGrab] = Color(46, 53, 66);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = Color(61, 72, 91);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = AccentActive;
        colors[(int)ImGuiCol.CheckMark] = Accent;
        colors[(int)ImGuiCol.SliderGrab] = AccentActive;
        colors[(int)ImGuiCol.SliderGrabActive] = AccentHovered;
        colors[(int)ImGuiCol.Button] = Color(31, 37, 48);
        colors[(int)ImGuiCol.ButtonHovered] = Color(42, 53, 70);
        colors[(int)ImGuiCol.ButtonActive] = Color(43, 79, 133);
        colors[(int)ImGuiCol.Header] = Color(31, 37, 47);
        colors[(int)ImGuiCol.HeaderHovered] = Color(39, 52, 72);
        colors[(int)ImGuiCol.HeaderActive] = Color(43, 72, 113);
        colors[(int)ImGuiCol.Separator] = Color(48, 56, 70);
        colors[(int)ImGuiCol.SeparatorHovered] = Accent;
        colors[(int)ImGuiCol.SeparatorActive] = AccentActive;
        colors[(int)ImGuiCol.ResizeGrip] = Color(68, 138, 255, 36);
        colors[(int)ImGuiCol.ResizeGripHovered] = Color(68, 138, 255, 115);
        colors[(int)ImGuiCol.ResizeGripActive] = Accent;
        colors[(int)ImGuiCol.Tab] = Color(21, 25, 33);
        colors[(int)ImGuiCol.TabHovered] = Color(37, 45, 59);
        colors[(int)ImGuiCol.TabSelected] = Color(32, 39, 51);
        colors[(int)ImGuiCol.TabSelectedOverline] = Color(0, 0, 0, 0);
        colors[(int)ImGuiCol.TabDimmed] = Color(18, 22, 29);
        colors[(int)ImGuiCol.TabDimmedSelected] = Color(28, 33, 42);
        colors[(int)ImGuiCol.TabDimmedSelectedOverline] = Color(0, 0, 0, 0);
        colors[(int)ImGuiCol.DockingPreview] = Color(68, 138, 255, 100);
        colors[(int)ImGuiCol.DockingEmptyBg] = Color(24, 28, 36);
        colors[(int)ImGuiCol.PlotLines] = Accent;
        colors[(int)ImGuiCol.PlotLinesHovered] = AccentHovered;
        colors[(int)ImGuiCol.PlotHistogram] = Warning;
        colors[(int)ImGuiCol.PlotHistogramHovered] = Color(255, 211, 115);
        colors[(int)ImGuiCol.TableHeaderBg] = Color(29, 29, 29);
        colors[(int)ImGuiCol.TableBorderStrong] = Color(47, 47, 47);
        colors[(int)ImGuiCol.TableBorderLight] = Color(38, 38, 38);
        colors[(int)ImGuiCol.TableRowBg] = Color(0, 0, 0, 0);
        colors[(int)ImGuiCol.TableRowBgAlt] = Color(255, 255, 255, 5);
        colors[(int)ImGuiCol.TextLink] = AccentHovered;
        colors[(int)ImGuiCol.TextSelectedBg] = Color(68, 138, 255, 72);
        colors[(int)ImGuiCol.DragDropTarget] = Color(112, 169, 255);
        colors[(int)ImGuiCol.NavCursor] = Accent;
        colors[(int)ImGuiCol.NavWindowingHighlight] = Color(224, 232, 240, 170);
        colors[(int)ImGuiCol.NavWindowingDimBg] = Color(4, 7, 11, 150);
        colors[(int)ImGuiCol.ModalWindowDimBg] = Color(4, 7, 11, 185);
    }

    public static void PushSelectedButton(bool selected)
    {
        if (!selected)
            return;
        ImGui.PushStyleColor(ImGuiCol.Button, AccentActive);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AccentHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Accent);
    }

    public static void PopSelectedButton(bool selected)
    {
        if (selected)
            ImGui.PopStyleColor(3);
    }

    public static Vector4 LogColor(Core.LogLevel level) => level switch
    {
        Core.LogLevel.Trace => MutedText,
        Core.LogLevel.Debug => Color(121, 171, 225),
        Core.LogLevel.Information => Text,
        Core.LogLevel.Warning => Warning,
        Core.LogLevel.Error => Error,
        Core.LogLevel.Critical => Color(235, 91, 158),
        _ => Text,
    };

    private static Vector4 Color(byte red, byte green, byte blue, byte alpha = 255) =>
        new(red / 255f, green / 255f, blue / 255f, alpha / 255f);
}
