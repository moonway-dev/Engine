using System.Numerics;
using EngineVec4 = Engine.Math.Vector4;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace Engine.Editor.Graphs.Nodes;

public class MultiplyNode : GraphNode
{
    public MultiplyNode(int id, NVector2 position) 
        : base(id, "Multiply", null, position, new NVector2(220f, 150f), new NVector4(0.24f, 0.48f, 0.23f, 1f),
            inputs: new[] { ("A", NodeValueKind.Color), ("B", NodeValueKind.Color) },
            outputs: new[] { ("Result", NodeValueKind.Color) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex == 0)
        {
            EngineVec4? aNullable = context.GetInputValue<EngineVec4>(Id, 0);
            EngineVec4? bNullable = context.GetInputValue<EngineVec4>(Id, 1);
            EngineVec4 a = aNullable.HasValue ? aNullable.Value : EngineVec4.One;
            EngineVec4 b = bNullable.HasValue ? bNullable.Value : EngineVec4.One;
            return new EngineVec4(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
        }
        return null;
    }

    public override void DrawInspector()
    {
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No editable properties");
    }
}

public class AddNode : GraphNode
{
    public AddNode(int id, NVector2 position) 
        : base(id, "Add", null, position, new NVector2(200f, 150f), new NVector4(0.24f, 0.48f, 0.23f, 1f),
            inputs: new[] { ("A", NodeValueKind.Color), ("B", NodeValueKind.Color) },
            outputs: new[] { ("Result", NodeValueKind.Color) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex == 0)
        {
            EngineVec4? aNullable = context.GetInputValue<EngineVec4>(Id, 0);
            EngineVec4? bNullable = context.GetInputValue<EngineVec4>(Id, 1);
            EngineVec4 a = aNullable.HasValue ? aNullable.Value : EngineVec4.Zero;
            EngineVec4 b = bNullable.HasValue ? bNullable.Value : EngineVec4.Zero;
            return a + b;
        }
        return null;
    }

    public override void DrawInspector()
    {
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No editable properties");
    }
}

public class LerpNode : GraphNode
{
    public LerpNode(int id, NVector2 position) 
        : base(id, "Lerp", null, position, new NVector2(240f, 170f), new NVector4(0.18f, 0.35f, 0.45f, 1f),
            inputs: new[] { ("A", NodeValueKind.Color), ("B", NodeValueKind.Color), ("Alpha", NodeValueKind.Scalar) },
            outputs: new[] { ("Result", NodeValueKind.Color) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex == 0)
        {
            EngineVec4? aNullable = context.GetInputValue<EngineVec4>(Id, 0);
            EngineVec4? bNullable = context.GetInputValue<EngineVec4>(Id, 1);
            float? alphaNullable = context.GetInputValue<float>(Id, 2);
            EngineVec4 a = aNullable.HasValue ? aNullable.Value : EngineVec4.Zero;
            EngineVec4 b = bNullable.HasValue ? bNullable.Value : EngineVec4.Zero;
            float alpha = alphaNullable.HasValue ? alphaNullable.Value : 0.5f;
            alpha = System.Math.Clamp(alpha, 0f, 1f);
            return a + (b - a) * alpha;
        }
        return null;
    }

    public override void DrawInspector()
    {
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No editable properties");
    }
}

public class PowerNode : GraphNode
{
    public PowerNode(int id, NVector2 position) 
        : base(id, "Power", null, position, new NVector2(200f, 150f), new NVector4(0.24f, 0.48f, 0.23f, 1f),
            inputs: new[] { ("Base", NodeValueKind.Scalar), ("Exp", NodeValueKind.Scalar) },
            outputs: new[] { ("Result", NodeValueKind.Scalar) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex == 0)
        {
            float? baseValNullable = context.GetInputValue<float>(Id, 0);
            float? expNullable = context.GetInputValue<float>(Id, 1);
            float baseVal = baseValNullable.HasValue ? baseValNullable.Value : 1.0f;
            float exp = expNullable.HasValue ? expNullable.Value : 1.0f;
            return System.MathF.Pow(baseVal, exp);
        }
        return null;
    }

    public override void DrawInspector()
    {
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No editable properties");
    }
}

