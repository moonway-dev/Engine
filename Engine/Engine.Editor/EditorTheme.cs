using ImGuiNET;
using System.Numerics;

namespace Engine.Editor;

public static class EditorTheme
{
    public static readonly Vector4 Accent = Color(74, 144, 226);
    public static readonly Vector4 AccentHovered = Color(92, 161, 242);
    public static readonly Vector4 AccentActive = Color(56, 119, 196);
    public static readonly Vector4 Text = Color(220, 224, 230);
    public static readonly Vector4 MutedText = Color(137, 145, 157);
    public static readonly Vector4 Error = Color(234, 92, 92);
    public static readonly Vector4 Warning = Color(230, 177, 72);
    public static readonly Vector4 Success = Color(91, 190, 125);

    public static void Apply()
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        style.WindowPadding = new Vector2(10f, 10f);
        style.FramePadding = new Vector2(8f, 5f);
        style.CellPadding = new Vector2(7f, 5f);
        style.ItemSpacing = new Vector2(8f, 6f);
        style.ItemInnerSpacing = new Vector2(6f, 4f);
        style.TouchExtraPadding = Vector2.Zero;
        style.IndentSpacing = 18f;
        style.ScrollbarSize = 13f;
        style.GrabMinSize = 10f;
        style.WindowBorderSize = 1f;
        style.ChildBorderSize = 1f;
        style.PopupBorderSize = 1f;
        style.FrameBorderSize = 0f;
        style.TabBorderSize = 0f;
        style.WindowRounding = 0f;
        style.ChildRounding = 3f;
        style.FrameRounding = 4f;
        style.PopupRounding = 5f;
        style.ScrollbarRounding = 7f;
        style.GrabRounding = 3f;
        style.TabRounding = 4f;
        style.WindowTitleAlign = new Vector2(0.02f, 0.5f);
        style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        style.SelectableTextAlign = new Vector2(0f, 0.5f);

        RangeAccessor<Vector4> colors = style.Colors;
        colors[(int)ImGuiCol.Text] = Text;
        colors[(int)ImGuiCol.TextDisabled] = MutedText;
        colors[(int)ImGuiCol.WindowBg] = Color(28, 31, 36);
        colors[(int)ImGuiCol.ChildBg] = Color(24, 27, 31);
        colors[(int)ImGuiCol.PopupBg] = Color(32, 35, 41, 250);
        colors[(int)ImGuiCol.Border] = Color(50, 55, 63);
        colors[(int)ImGuiCol.BorderShadow] = Color(0, 0, 0, 0);
        colors[(int)ImGuiCol.FrameBg] = Color(39, 43, 50);
        colors[(int)ImGuiCol.FrameBgHovered] = Color(49, 55, 64);
        colors[(int)ImGuiCol.FrameBgActive] = Color(56, 63, 73);
        colors[(int)ImGuiCol.TitleBg] = Color(25, 28, 33);
        colors[(int)ImGuiCol.TitleBgActive] = Color(31, 35, 41);
        colors[(int)ImGuiCol.TitleBgCollapsed] = Color(25, 28, 33);
        colors[(int)ImGuiCol.MenuBarBg] = Color(24, 27, 32);
        colors[(int)ImGuiCol.ScrollbarBg] = Color(22, 25, 29);
        colors[(int)ImGuiCol.ScrollbarGrab] = Color(60, 66, 76);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = Color(77, 84, 96);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = Color(91, 99, 112);
        colors[(int)ImGuiCol.CheckMark] = Accent;
        colors[(int)ImGuiCol.SliderGrab] = Accent;
        colors[(int)ImGuiCol.SliderGrabActive] = AccentHovered;
        colors[(int)ImGuiCol.Button] = Color(45, 50, 58);
        colors[(int)ImGuiCol.ButtonHovered] = Color(58, 65, 75);
        colors[(int)ImGuiCol.ButtonActive] = Color(67, 75, 87);
        colors[(int)ImGuiCol.Header] = Color(50, 62, 77);
        colors[(int)ImGuiCol.HeaderHovered] = Color(57, 77, 99);
        colors[(int)ImGuiCol.HeaderActive] = Color(62, 86, 112);
        colors[(int)ImGuiCol.Separator] = Color(48, 53, 61);
        colors[(int)ImGuiCol.SeparatorHovered] = Accent;
        colors[(int)ImGuiCol.SeparatorActive] = AccentActive;
        colors[(int)ImGuiCol.ResizeGrip] = Color(74, 144, 226, 60);
        colors[(int)ImGuiCol.ResizeGripHovered] = Color(74, 144, 226, 150);
        colors[(int)ImGuiCol.ResizeGripActive] = Accent;
        colors[(int)ImGuiCol.Tab] = Color(30, 34, 40);
        colors[(int)ImGuiCol.TabHovered] = Color(51, 66, 84);
        colors[(int)ImGuiCol.TabSelected] = Color(43, 53, 66);
        colors[(int)ImGuiCol.TabSelectedOverline] = Accent;
        colors[(int)ImGuiCol.TabDimmed] = Color(25, 28, 33);
        colors[(int)ImGuiCol.TabDimmedSelected] = Color(34, 39, 46);
        colors[(int)ImGuiCol.TabDimmedSelectedOverline] = Color(74, 144, 226, 130);
        colors[(int)ImGuiCol.DockingPreview] = Color(74, 144, 226, 120);
        colors[(int)ImGuiCol.DockingEmptyBg] = Color(20, 23, 27);
        colors[(int)ImGuiCol.PlotLines] = Accent;
        colors[(int)ImGuiCol.PlotLinesHovered] = AccentHovered;
        colors[(int)ImGuiCol.PlotHistogram] = Warning;
        colors[(int)ImGuiCol.PlotHistogramHovered] = Color(243, 194, 93);
        colors[(int)ImGuiCol.TableHeaderBg] = Color(37, 42, 49);
        colors[(int)ImGuiCol.TableBorderStrong] = Color(50, 55, 63);
        colors[(int)ImGuiCol.TableBorderLight] = Color(42, 47, 54);
        colors[(int)ImGuiCol.TableRowBg] = Color(0, 0, 0, 0);
        colors[(int)ImGuiCol.TableRowBgAlt] = Color(255, 255, 255, 7);
        colors[(int)ImGuiCol.TextLink] = AccentHovered;
        colors[(int)ImGuiCol.TextSelectedBg] = Color(74, 144, 226, 95);
        colors[(int)ImGuiCol.DragDropTarget] = Color(93, 174, 255);
        colors[(int)ImGuiCol.NavCursor] = Accent;
        colors[(int)ImGuiCol.NavWindowingHighlight] = Color(255, 255, 255, 170);
        colors[(int)ImGuiCol.NavWindowingDimBg] = Color(12, 14, 17, 140);
        colors[(int)ImGuiCol.ModalWindowDimBg] = Color(12, 14, 17, 170);
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
