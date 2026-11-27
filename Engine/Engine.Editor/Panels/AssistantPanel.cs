using Engine.Editor.Mcp;
using Engine.Mcp;
using ImGuiNET;

namespace Engine.Editor.Panels;

public sealed class AssistantPanel : EditorPanel
{
    private const string SetupInstructions = "To use the engine MCP tools, configure your external MCP client to point at the running editor. Add the JSON configuration below to your client's mcpServers section while the engine is open.";

    private readonly McpHub _hub;
    private int _portBuffer;

    public AssistantPanel(McpHub hub)
    {
        _hub = hub;
        _portBuffer = hub.Port;
    }

    public override string Title => "Assistant";

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
        McpServer server = _hub.Server;
        bool running = server.IsRunning;
        ImGui.TextUnformatted("Enable engine as MCP server");
        bool enabled = running;
        if (ImGui.Checkbox("##AssistantMcpEnabled", ref enabled))
            _hub.Toggle(enabled);
        if (running)
        {
            ImGui.SameLine();
            ImGui.TextColored(EditorTheme.Success, $"  running on port {server.Port}, {server.ClientCount} client(s) seen");
        }
        else
        {
            ImGui.SameLine();
            ImGui.TextColored(EditorTheme.MutedText, "  stopped");
        }
        ImGui.Spacing();
        ImGui.TextUnformatted("Port");
        ImGui.SetNextItemWidth(120f);
        int portBuffer = _portBuffer;
        if (ImGui.InputInt("##AssistantMcpPort", ref portBuffer))
        {
            portBuffer = Math.Clamp(portBuffer, 1024, 65535);
            _portBuffer = portBuffer;
        }
        ImGui.SameLine();
        if (ImGui.Button("Apply"))
        {
            _hub.SetPort(_portBuffer);
        }
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextWrapped(SetupInstructions);
        if (running)
        {
            ImGui.TextUnformatted("JSON Configuration");
            ImGui.BeginChild("AssistantMcpConfig", new System.Numerics.Vector2(0f, 132f), ImGuiChildFlags.Borders);
            ImGui.TextUnformatted("{");
            ImGui.TextUnformatted("    \"mcpServers\": {");
            ImGui.TextUnformatted("        \"Engine\": {");
            ImGui.TextUnformatted("            \"command\": \"npx\",");
            ImGui.TextUnformatted($"            \"args\": [\"-y\", \"mcp-remote\", \"http://127.0.0.1:{_hub.Port}/mcp\"]");
            ImGui.TextUnformatted("        }");
            ImGui.TextUnformatted("    }");
            ImGui.TextUnformatted("}");
            ImGui.EndChild();
            ImGui.Spacing();
            string configuration = "{\"mcpServers\":{\"Engine\":{\"url\":\"http://127.0.0.1:" + _hub.Port + "/mcp\"}}}";
            if (ImGui.Button("Copy configuration"))
                ImGui.SetClipboardText(configuration);
            ImGui.SameLine();
            ImGui.TextUnformatted(configuration);
        }
        ImGui.Separator();
        if (ImGui.CollapsingHeader("Available tools", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (McpTool tool in McpTools.All)
            {
                ImGui.BulletText(tool.Name);
                ImGui.SameLine();
                ImGui.TextDisabled(tool.Description);
            }
        }
        ImGui.End();
    }
}
