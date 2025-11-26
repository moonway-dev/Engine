using System.Numerics;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace Engine.Editor.Graphs.Nodes;

public class ScalarParameterNode : GraphNode
{
    public float Value { get; set; } = 1.0f;

    public ScalarParameterNode(int id, NVector2 position) 
        : base(id, "Scalar Parameter", null, position, new NVector2(200f, 120f), new NVector4(0.3f, 0.7f, 0.4f, 0.95f),
            inputs: Array.Empty<(string, NodeValueKind)>(),
            outputs: new[] { ("Value", NodeValueKind.Scalar) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex == 0)
        {
            return Value;
        }
        return null;
    }

    public override void DrawInspector()
    {
        ImGui.Text("Value");
        ImGui.Separator();
        float value = Value;
        if (ImGui.DragFloat("Scalar", ref value, 0.01f))
        {
            Value = value;
        }
    }
}

