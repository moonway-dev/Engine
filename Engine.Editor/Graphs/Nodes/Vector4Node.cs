using System.Numerics;
using EngineVec4 = Engine.Math.Vector4;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace Engine.Editor.Graphs.Nodes;

public class Vector4Node : GraphNode
{
    public EngineVec4 Color { get; set; } = EngineVec4.One;

    public Vector4Node(int id, NVector2 position) 
        : base(id, "Vector 4", null, position, new NVector2(210f, 140f), new NVector4(0.94f, 0.54f, 0.2f, 1f),
            inputs: Array.Empty<(string, NodeValueKind)>(),
            outputs: new[] { ("Color", NodeValueKind.Color) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex == 0)
        {
            return Color;
        }
        return null;
    }

    public override void DrawInspector()
    {
        ImGui.Text("Color");
        ImGui.Separator();
        
        Vector4 colorVec = new Vector4(Color.X, Color.Y, Color.Z, Color.W);
        if (ImGui.ColorEdit4("RGBA", ref colorVec, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
        {
            Color = new EngineVec4(colorVec.X, colorVec.Y, colorVec.Z, colorVec.W);
        }

        ImGui.Spacing();
        ImGui.Text("Components:");
        float r = Color.X;
        float g = Color.Y;
        float b = Color.Z;
        float a = Color.W;

        if (ImGui.DragFloat("R", ref r, 0.01f, 0f, 1f))
        {
            Color = new EngineVec4(r, g, b, a);
        }
        if (ImGui.DragFloat("G", ref g, 0.01f, 0f, 1f))
        {
            Color = new EngineVec4(r, g, b, a);
        }
        if (ImGui.DragFloat("B", ref b, 0.01f, 0f, 1f))
        {
            Color = new EngineVec4(r, g, b, a);
        }
        if (ImGui.DragFloat("A", ref a, 0.01f, 0f, 1f))
        {
            Color = new EngineVec4(r, g, b, a);
        }
    }
}

