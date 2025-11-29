using System.Collections.Generic;
using System.Numerics;
using Engine.Editor.Graphs;
using EngineVec2 = Engine.Math.Vector2;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace Engine.Editor.Graphs.Nodes;

public class TexCoordNode : GraphNode
{
    public EngineVec2 Coordinates { get; set; } = new EngineVec2(0f, 0f);
    public EngineVec2 Scale { get; set; } = new EngineVec2(1f, 1f);
    public EngineVec2 Offset { get; set; } = new EngineVec2(0f, 0f);

    public TexCoordNode(int id, NVector2 position) 
        : base(id, "TexCoord", "UV Source", position, new NVector2(200f, 280f), new NVector4(0.74f, 0.18f, 0.12f, 1f),
            inputs: new[] 
            { 
                ("Coordinates U", NodeValueKind.Scalar),
                ("Coordinates V", NodeValueKind.Scalar),
                ("Scale U", NodeValueKind.Scalar),
                ("Scale V", NodeValueKind.Scalar),
                ("Offset U", NodeValueKind.Scalar),
                ("Offset V", NodeValueKind.Scalar)
            },
            outputs: new[] { ("UV", NodeValueKind.UV) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex == 0)
        {
            float? coordU = context.GetInputValue<float>(Id, 0);
            float? coordV = context.GetInputValue<float>(Id, 1);
            float? scaleU = context.GetInputValue<float>(Id, 2);
            float? scaleV = context.GetInputValue<float>(Id, 3);
            float? offsetU = context.GetInputValue<float>(Id, 4);
            float? offsetV = context.GetInputValue<float>(Id, 5);
            
            float finalCoordU = coordU ?? Coordinates.X;
            float finalCoordV = coordV ?? Coordinates.Y;
            float finalScaleU = scaleU ?? Scale.X;
            float finalScaleV = scaleV ?? Scale.Y;
            float finalOffsetU = offsetU ?? Offset.X;
            float finalOffsetV = offsetV ?? Offset.Y;
            
            if (finalScaleU <= 0f) finalScaleU = 1f;
            if (finalScaleV <= 0f) finalScaleV = 1f;
            
            EngineVec2 uv = new EngineVec2(
                finalCoordU / finalScaleU + finalOffsetU,
                finalCoordV / finalScaleV + finalOffsetV
            );
            return uv;
        }
        return null;
    }

    public override void DrawInspector()
    {
        ImGui.Spacing();
        ImGui.TextDisabled("Outputs UV coordinates as Vector2");
    }
    
    public override void DrawInspector(List<NodeConnection> connections)
    {
        base.DrawInspector(connections);
    }
}

