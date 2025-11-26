using System.Numerics;
using EngineVec2 = Engine.Math.Vector2;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace Engine.Editor.Graphs.Nodes;

public class TexCoordNode : GraphNode
{
    public int UVChannel { get; set; } = 0;

    public TexCoordNode(int id, NVector2 position) 
        : base(id, "TexCoord[0]", "UV Source", position, new NVector2(160f, 140f), new NVector4(0.74f, 0.18f, 0.12f, 1f),
            inputs: Array.Empty<(string, NodeValueKind)>(),
            outputs: new[] { ("UV", NodeValueKind.UV) })
    {
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        Title = $"TexCoord[{UVChannel}]";
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex == 0)
        {
            return new EngineVec2(0.5f, 0.5f);
        }
        return null;
    }

    public override void DrawInspector()
    {
        ImGui.Text("UV Channel");
        ImGui.Separator();
        int channel = UVChannel;
        if (ImGui.DragInt("Channel", ref channel, 1f, 0, 3))
        {
            UVChannel = System.Math.Clamp(channel, 0, 3);
            UpdateTitle();
        }
        ImGui.TextDisabled("Outputs UV coordinates as Vector2");
    }
}

