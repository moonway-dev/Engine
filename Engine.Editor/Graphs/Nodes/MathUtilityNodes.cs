using System;
using System.Numerics;
using EngineVec4 = Engine.Math.Vector4;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace Engine.Editor.Graphs.Nodes;

public class AbsNode : GraphNode
{
    public AbsNode(int id, NVector2 position) 
        : base(id, "Abs", null, position, new NVector2(180f, 120f), new NVector4(0.24f, 0.48f, 0.23f, 1f),
            inputs: new[] { ("Input", NodeValueKind.Color) },
            outputs: new[] { ("Result", NodeValueKind.Color) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex >= OutputPins.Count)
            return null;

        EngineVec4? inputNullable = context.GetInputValue<EngineVec4>(Id, 0);
        float? inputScalar = context.GetInputValue<float>(Id, 0);
        
        EngineVec4 result;
        if (inputScalar.HasValue)
        {
            result = new EngineVec4(MathF.Abs(inputScalar.Value), MathF.Abs(inputScalar.Value), MathF.Abs(inputScalar.Value), MathF.Abs(inputScalar.Value));
        }
        else
        {
            EngineVec4 input = inputNullable ?? EngineVec4.Zero;
            result = new EngineVec4(MathF.Abs(input.X), MathF.Abs(input.Y), MathF.Abs(input.Z), MathF.Abs(input.W));
        }
        
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
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Returns absolute value of input.");
    }
}

public class FloorNode : GraphNode
{
    public FloorNode(int id, NVector2 position) 
        : base(id, "Floor", null, position, new NVector2(180f, 120f), new NVector4(0.24f, 0.48f, 0.23f, 1f),
            inputs: new[] { ("Input", NodeValueKind.Color) },
            outputs: new[] { ("Result", NodeValueKind.Color) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex >= OutputPins.Count)
            return null;

        EngineVec4? inputNullable = context.GetInputValue<EngineVec4>(Id, 0);
        float? inputScalar = context.GetInputValue<float>(Id, 0);
        
        EngineVec4 result;
        if (inputScalar.HasValue)
        {
            result = new EngineVec4(MathF.Floor(inputScalar.Value), MathF.Floor(inputScalar.Value), MathF.Floor(inputScalar.Value), MathF.Floor(inputScalar.Value));
        }
        else
        {
            EngineVec4 input = inputNullable ?? EngineVec4.Zero;
            result = new EngineVec4(MathF.Floor(input.X), MathF.Floor(input.Y), MathF.Floor(input.Z), MathF.Floor(input.W));
        }
        
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
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Returns floor value of input.");
    }
}

public class CeilNode : GraphNode
{
    public CeilNode(int id, NVector2 position) 
        : base(id, "Ceil", null, position, new NVector2(180f, 120f), new NVector4(0.24f, 0.48f, 0.23f, 1f),
            inputs: new[] { ("Input", NodeValueKind.Color) },
            outputs: new[] { ("Result", NodeValueKind.Color) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex >= OutputPins.Count)
            return null;

        EngineVec4? inputNullable = context.GetInputValue<EngineVec4>(Id, 0);
        float? inputScalar = context.GetInputValue<float>(Id, 0);
        
        EngineVec4 result;
        if (inputScalar.HasValue)
        {
            result = new EngineVec4(MathF.Ceiling(inputScalar.Value), MathF.Ceiling(inputScalar.Value), MathF.Ceiling(inputScalar.Value), MathF.Ceiling(inputScalar.Value));
        }
        else
        {
            EngineVec4 input = inputNullable ?? EngineVec4.Zero;
            result = new EngineVec4(MathF.Ceiling(input.X), MathF.Ceiling(input.Y), MathF.Ceiling(input.Z), MathF.Ceiling(input.W));
        }
        
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
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Returns ceiling value of input.");
    }
}

public class FracNode : GraphNode
{
    public FracNode(int id, NVector2 position) 
        : base(id, "Frac", null, position, new NVector2(180f, 120f), new NVector4(0.24f, 0.48f, 0.23f, 1f),
            inputs: new[] { ("Input", NodeValueKind.Color) },
            outputs: new[] { ("Result", NodeValueKind.Color) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex >= OutputPins.Count)
            return null;

        EngineVec4? inputNullable = context.GetInputValue<EngineVec4>(Id, 0);
        float? inputScalar = context.GetInputValue<float>(Id, 0);
        
        EngineVec4 result;
        if (inputScalar.HasValue)
        {
            float val = inputScalar.Value;
            float frac = val - MathF.Floor(val);
            result = new EngineVec4(frac, frac, frac, frac);
        }
        else
        {
            EngineVec4 input = inputNullable ?? EngineVec4.Zero;
            result = new EngineVec4(
                input.X - MathF.Floor(input.X),
                input.Y - MathF.Floor(input.Y),
                input.Z - MathF.Floor(input.Z),
                input.W - MathF.Floor(input.W)
            );
        }
        
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
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Returns fractional part of input.");
    }
}

public class SinNode : GraphNode
{
    public SinNode(int id, NVector2 position) 
        : base(id, "Sin", null, position, new NVector2(180f, 120f), new NVector4(0.24f, 0.48f, 0.23f, 1f),
            inputs: new[] { ("Input", NodeValueKind.Scalar) },
            outputs: new[] { ("Result", NodeValueKind.Scalar) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex >= OutputPins.Count)
            return null;

        float? inputNullable = context.GetInputValue<float>(Id, 0);
        float input = inputNullable ?? 0f;
        return MathF.Sin(input);
    }

    public override void DrawInspector()
    {
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Returns sine of input in radians.");
    }
}

public class CosNode : GraphNode
{
    public CosNode(int id, NVector2 position) 
        : base(id, "Cos", null, position, new NVector2(180f, 120f), new NVector4(0.24f, 0.48f, 0.23f, 1f),
            inputs: new[] { ("Input", NodeValueKind.Scalar) },
            outputs: new[] { ("Result", NodeValueKind.Scalar) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex >= OutputPins.Count)
            return null;

        float? inputNullable = context.GetInputValue<float>(Id, 0);
        float input = inputNullable ?? 0f;
        return MathF.Cos(input);
    }

    public override void DrawInspector()
    {
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Returns cosine of input in radians.");
    }
}

public class MaxNode : GraphNode
{
    public MaxNode(int id, NVector2 position) 
        : base(id, "Max", null, position, new NVector2(200f, 150f), new NVector4(0.24f, 0.48f, 0.23f, 1f),
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
        
        EngineVec4 a = aNullable ?? (aScalar.HasValue ? new EngineVec4(aScalar.Value, aScalar.Value, aScalar.Value, aScalar.Value) : EngineVec4.Zero);
        EngineVec4 b = bNullable ?? (bScalar.HasValue ? new EngineVec4(bScalar.Value, bScalar.Value, bScalar.Value, bScalar.Value) : EngineVec4.Zero);
        
        EngineVec4 result = new EngineVec4(
            MathF.Max(a.X, b.X),
            MathF.Max(a.Y, b.Y),
            MathF.Max(a.Z, b.Z),
            MathF.Max(a.W, b.W)
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
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Returns maximum of A and B component-wise.");
    }
}

public class MinNode : GraphNode
{
    public MinNode(int id, NVector2 position) 
        : base(id, "Min", null, position, new NVector2(200f, 150f), new NVector4(0.24f, 0.48f, 0.23f, 1f),
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
        
        EngineVec4 a = aNullable ?? (aScalar.HasValue ? new EngineVec4(aScalar.Value, aScalar.Value, aScalar.Value, aScalar.Value) : EngineVec4.Zero);
        EngineVec4 b = bNullable ?? (bScalar.HasValue ? new EngineVec4(bScalar.Value, bScalar.Value, bScalar.Value, bScalar.Value) : EngineVec4.Zero);
        
        EngineVec4 result = new EngineVec4(
            MathF.Min(a.X, b.X),
            MathF.Min(a.Y, b.Y),
            MathF.Min(a.Z, b.Z),
            MathF.Min(a.W, b.W)
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
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Returns minimum of A and B component-wise.");
    }
}
