using System;
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
        if (outputIndex >= OutputPins.Count)
            return null;

        EngineVec4? aNullable = context.GetInputValue<EngineVec4>(Id, 0);
        EngineVec4? bNullable = context.GetInputValue<EngineVec4>(Id, 1);
        float? aScalar = context.GetInputValue<float>(Id, 0);
        float? bScalar = context.GetInputValue<float>(Id, 1);
        
        EngineVec4 a = aNullable.HasValue ? aNullable.Value : (aScalar.HasValue ? new EngineVec4(aScalar.Value, aScalar.Value, aScalar.Value, 1f) : EngineVec4.One);
        EngineVec4 b = bNullable.HasValue ? bNullable.Value : (bScalar.HasValue ? new EngineVec4(bScalar.Value, bScalar.Value, bScalar.Value, 1f) : EngineVec4.One);
        
        EngineVec4 result = new EngineVec4(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
        
        var pin = OutputPins[outputIndex];
        if (pin.IsSplitPin && pin.ChannelIndex.HasValue)
        {
            return pin.ChannelIndex.Value switch
            {
                0 => result.X,
                1 => result.Y,
                2 => result.Z,
                3 => result.W,
                _ => 0f
            };
        }
        
        return result;
    }

    public override void DrawInspector()
    {
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Multiplies two values component-wise.");
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
        if (outputIndex >= OutputPins.Count)
            return null;

        EngineVec4? aNullable = context.GetInputValue<EngineVec4>(Id, 0);
        EngineVec4? bNullable = context.GetInputValue<EngineVec4>(Id, 1);
        float? aScalar = context.GetInputValue<float>(Id, 0);
        float? bScalar = context.GetInputValue<float>(Id, 1);
        
        EngineVec4 a = aNullable.HasValue ? aNullable.Value : (aScalar.HasValue ? new EngineVec4(aScalar.Value, aScalar.Value, aScalar.Value, 0f) : EngineVec4.Zero);
        EngineVec4 b = bNullable.HasValue ? bNullable.Value : (bScalar.HasValue ? new EngineVec4(bScalar.Value, bScalar.Value, bScalar.Value, 0f) : EngineVec4.Zero);
        
        EngineVec4 result = a + b;
        
        var pin = OutputPins[outputIndex];
        if (pin.IsSplitPin && pin.ChannelIndex.HasValue)
        {
            return pin.ChannelIndex.Value switch
            {
                0 => result.X,
                1 => result.Y,
                2 => result.Z,
                3 => result.W,
                _ => 0f
            };
        }
        
        return result;
    }

    public override void DrawInspector()
    {
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Adds two values component-wise.");
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
        if (outputIndex >= OutputPins.Count)
            return null;

        EngineVec4? aNullable = context.GetInputValue<EngineVec4>(Id, 0);
        EngineVec4? bNullable = context.GetInputValue<EngineVec4>(Id, 1);
        float? alphaNullable = context.GetInputValue<float>(Id, 2);
        EngineVec4 a = aNullable.HasValue ? aNullable.Value : EngineVec4.Zero;
        EngineVec4 b = bNullable.HasValue ? bNullable.Value : EngineVec4.Zero;
        float alpha = System.Math.Clamp(alphaNullable ?? 0.5f, 0f, 1f);
        
        EngineVec4 result = a + (b - a) * alpha;
        
        var pin = OutputPins[outputIndex];
        if (pin.IsSplitPin && pin.ChannelIndex.HasValue)
        {
            return pin.ChannelIndex.Value switch
            {
                0 => result.X,
                1 => result.Y,
                2 => result.Z,
                3 => result.W,
                _ => 0f
            };
        }
        
        return result;
    }

    public override void DrawInspector()
    {
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Linearly interpolates between A and B using Alpha.");
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
        if (outputIndex >= OutputPins.Count)
            return null;

        float? baseValNullable = context.GetInputValue<float>(Id, 0);
        float? expNullable = context.GetInputValue<float>(Id, 1);
        float baseVal = baseValNullable ?? 1.0f;
        float exp = expNullable ?? 1.0f;
        return MathF.Pow(MathF.Max(baseVal, 0f), exp);
    }

    public override void DrawInspector()
    {
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Returns Base raised to the power of Exp.");
    }
}

public class SubtractNode : GraphNode
{
    public SubtractNode(int id, NVector2 position) 
        : base(id, "Subtract", null, position, new NVector2(200f, 150f), new NVector4(0.24f, 0.48f, 0.23f, 1f),
            inputs: new[] { ("A", NodeValueKind.Color), ("B", NodeValueKind.Color) },
            outputs: new[] { ("Result", NodeValueKind.Color) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex >= OutputPins.Count)
            return null;

        EngineVec4? aNullable = context.GetInputValue<EngineVec4>(Id, 0);
        EngineVec4? bNullable = context.GetInputValue<EngineVec4>(Id, 1);
        float? aScalar = context.GetInputValue<float>(Id, 0);
        float? bScalar = context.GetInputValue<float>(Id, 1);
        
        EngineVec4 a = aNullable.HasValue ? aNullable.Value : (aScalar.HasValue ? new EngineVec4(aScalar.Value, aScalar.Value, aScalar.Value, 0f) : EngineVec4.Zero);
        EngineVec4 b = bNullable.HasValue ? bNullable.Value : (bScalar.HasValue ? new EngineVec4(bScalar.Value, bScalar.Value, bScalar.Value, 0f) : EngineVec4.Zero);
        
        EngineVec4 result = a - b;
        
        var pin = OutputPins[outputIndex];
        if (pin.IsSplitPin && pin.ChannelIndex.HasValue)
        {
            return pin.ChannelIndex.Value switch
            {
                0 => result.X,
                1 => result.Y,
                2 => result.Z,
                3 => result.W,
                _ => 0f
            };
        }
        
        return result;
    }

    public override void DrawInspector()
    {
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Subtracts B from A component-wise.");
    }
}

public class DivideNode : GraphNode
{
    public DivideNode(int id, NVector2 position) 
        : base(id, "Divide", null, position, new NVector2(200f, 150f), new NVector4(0.24f, 0.48f, 0.23f, 1f),
            inputs: new[] { ("A", NodeValueKind.Color), ("B", NodeValueKind.Color) },
            outputs: new[] { ("Result", NodeValueKind.Color) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex >= OutputPins.Count)
            return null;

        EngineVec4? aNullable = context.GetInputValue<EngineVec4>(Id, 0);
        EngineVec4? bNullable = context.GetInputValue<EngineVec4>(Id, 1);
        float? aScalar = context.GetInputValue<float>(Id, 0);
        float? bScalar = context.GetInputValue<float>(Id, 1);
        
        EngineVec4 a = aNullable.HasValue ? aNullable.Value : (aScalar.HasValue ? new EngineVec4(aScalar.Value, aScalar.Value, aScalar.Value, 1f) : EngineVec4.One);
        EngineVec4 b = bNullable.HasValue ? bNullable.Value : (bScalar.HasValue ? new EngineVec4(bScalar.Value, bScalar.Value, bScalar.Value, 1f) : EngineVec4.One);
        
        const float epsilon = 0.0001f;
        EngineVec4 result = new EngineVec4(
            MathF.Abs(b.X) > epsilon ? a.X / b.X : 0f,
            MathF.Abs(b.Y) > epsilon ? a.Y / b.Y : 0f,
            MathF.Abs(b.Z) > epsilon ? a.Z / b.Z : 0f,
            MathF.Abs(b.W) > epsilon ? a.W / b.W : 0f
        );
        
        var pin = OutputPins[outputIndex];
        if (pin.IsSplitPin && pin.ChannelIndex.HasValue)
        {
            return pin.ChannelIndex.Value switch
            {
                0 => result.X,
                1 => result.Y,
                2 => result.Z,
                3 => result.W,
                _ => 0f
            };
        }
        
        return result;
    }

    public override void DrawInspector()
    {
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Divides A by B component-wise.");
    }
}
