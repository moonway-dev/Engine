using System;
using System.Numerics;
using EngineVec2 = Engine.Math.Vector2;
using EngineVec4 = Engine.Math.Vector4;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace Engine.Editor.Graphs.Nodes;

public class MaskNode : GraphNode
{
    public MaskNode(int id, NVector2 position) 
        : base(id, "Mask", null, position, new NVector2(200f, 150f), new NVector4(0.27f, 0.37f, 0.6f, 1f),
            inputs: new[] { ("Input", NodeValueKind.Color), ("Mask", NodeValueKind.Scalar) },
            outputs: new[] { ("Result", NodeValueKind.Color) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex >= OutputPins.Count)
            return null;

        EngineVec4? inputNullable = context.GetInputValue<EngineVec4>(Id, 0);
        float? maskNullable = context.GetInputValue<float>(Id, 1);
        EngineVec4 input = inputNullable ?? EngineVec4.Zero;
        float mask = System.Math.Clamp(maskNullable ?? 1.0f, 0f, 1f);
        
        EngineVec4 result = new EngineVec4(input.X * mask, input.Y * mask, input.Z * mask, input.W * mask);
        
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
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Multiplies input by mask value.");
    }
}

public class ClampNode : GraphNode
{
    public float MinValue { get; set; } = 0.0f;
    public float MaxValue { get; set; } = 1.0f;

    public ClampNode(int id, NVector2 position) 
        : base(id, "Clamp", null, position, new NVector2(200f, 150f), new NVector4(0.27f, 0.37f, 0.6f, 1f),
            inputs: new[] { ("Input", NodeValueKind.Scalar) },
            outputs: new[] { ("Result", NodeValueKind.Scalar) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex >= OutputPins.Count)
            return null;

        float? inputNullable = context.GetInputValue<float>(Id, 0);
        float input = inputNullable ?? 0.5f;
        return System.Math.Clamp(input, MinValue, MaxValue);
    }

    public override void DrawInspector()
    {
        ImGui.Text("Clamp Range");
        ImGui.Separator();
        float min = MinValue;
        float max = MaxValue;
        if (ImGui.DragFloat("Min", ref min, 0.01f))
        {
            MinValue = min;
        }
        if (ImGui.DragFloat("Max", ref max, 0.01f))
        {
            MaxValue = max;
        }
    }
}

public class TimeNode : GraphNode
{
    private static float _time = 0.0f;

    public TimeNode(int id, NVector2 position) 
        : base(id, "Time", null, position, new NVector2(160f, 110f), new NVector4(0.27f, 0.37f, 0.6f, 1f),
            inputs: Array.Empty<(string, NodeValueKind)>(),
            outputs: new[] { ("Time", NodeValueKind.Scalar) })
    {
    }

    public static void UpdateTime(float deltaTime)
    {
        _time += deltaTime;
    }

    public static float GetCurrentTime()
    {
        return _time;
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex >= OutputPins.Count)
            return null;
        return _time;
    }

    public override void DrawInspector()
    {
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Outputs elapsed time in seconds.");
        ImGui.Text($"Current Time: {_time:F2}");
    }
}

public class DeltaTimeNode : GraphNode
{
    private static float _deltaTime = 0.0f;

    public DeltaTimeNode(int id, NVector2 position) 
        : base(id, "Delta Time", null, position, new NVector2(160f, 110f), new NVector4(0.27f, 0.37f, 0.6f, 1f),
            inputs: Array.Empty<(string, NodeValueKind)>(),
            outputs: new[] { ("Delta Time", NodeValueKind.Scalar) })
    {
    }

    public static void UpdateDeltaTime(float deltaTime)
    {
        _deltaTime = deltaTime;
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex >= OutputPins.Count)
            return null;
        return _deltaTime;
    }

    public override void DrawInspector()
    {
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Outputs frame delta time in seconds.");
        ImGui.Text($"Current Delta: {_deltaTime:F6}");
    }
}

public class PannerNode : GraphNode
{
    public EngineVec2 Speed { get; set; } = new EngineVec2(0.1f, 0.1f);

    public PannerNode(int id, NVector2 position) 
        : base(id, "Panner", null, position, new NVector2(220f, 170f), new NVector4(0.27f, 0.37f, 0.6f, 1f),
            inputs: new[] { ("Speed X", NodeValueKind.Scalar), ("Speed Y", NodeValueKind.Scalar), ("Time", NodeValueKind.Scalar) },
            outputs: new[] { ("Offset X", NodeValueKind.Scalar), ("Offset Y", NodeValueKind.Scalar) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex >= OutputPins.Count)
            return null;

        float? speedXNullable = context.GetInputValue<float>(Id, 0);
        float? speedYNullable = context.GetInputValue<float>(Id, 1);
        float? timeNullable = context.GetInputValue<float>(Id, 2);
        
        float speedX = speedXNullable ?? Speed.X;
        float speedY = speedYNullable ?? Speed.Y;
        float time = timeNullable ?? TimeNode.GetCurrentTime();
        
        if (MathF.Abs(time) < 0.0001f)
        {
            return 0f;
        }
        
        float offsetX = (MathF.Sin(speedX * time * 2f * MathF.PI) + 1f) * 0.5f;
        float offsetY = (MathF.Sin(speedY * time * 2f * MathF.PI) + 1f) * 0.5f;
        
        if (outputIndex == 0)
        {
            return offsetX;
        }
        else if (outputIndex == 1)
        {
            return offsetY;
        }
        
        return 0f;
    }

    public override void DrawInspector()
    {
        ImGui.Spacing();
        ImGui.TextDisabled("Outputs panning offset as two scalars (Offset X, Offset Y)");
    }
}

public class HueShiftNode : GraphNode
{
    public float HueShift { get; set; } = 0.0f;

    public HueShiftNode(int id, NVector2 position) 
        : base(id, "Hue Shift", null, position, new NVector2(220f, 150f), new NVector4(0.27f, 0.37f, 0.6f, 1f),
            inputs: new[] { ("Color", NodeValueKind.Color), ("Hue Shift", NodeValueKind.Scalar) },
            outputs: new[] { ("Result", NodeValueKind.Color) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex >= OutputPins.Count)
            return null;

        EngineVec4? colorNullable = context.GetInputValue<EngineVec4>(Id, 0);
        float? hueShiftNullable = context.GetInputValue<float>(Id, 1);
        EngineVec4 color = colorNullable ?? EngineVec4.One;
        float hueShift = hueShiftNullable ?? HueShift;

        float r = System.Math.Clamp(color.X, 0f, 1f);
        float g = System.Math.Clamp(color.Y, 0f, 1f);
        float b = System.Math.Clamp(color.Z, 0f, 1f);
        
        float max = MathF.Max(MathF.Max(r, g), b);
        float min = MathF.Min(MathF.Min(r, g), b);
        float delta = max - min;
        
        float h = 0f;
        if (delta > 0.0001f)
        {
            if (max == r)
                h = ((g - b) / delta) % 6f;
            else if (max == g)
                h = (b - r) / delta + 2f;
            else
                h = (r - g) / delta + 4f;
            h /= 6f;
        }
        
        float s = max > 0.0001f ? delta / max : 0f;
        float v = max;

        h = (h + hueShift) % 1f;
        if (h < 0f) h += 1f;

        float c = v * s;
        float x = c * (1f - MathF.Abs((h * 6f) % 2f - 1f));
        float m = v - c;
        
        float rNew, gNew, bNew;
        if (h < 1f / 6f)
        {
            rNew = c; gNew = x; bNew = 0f;
        }
        else if (h < 2f / 6f)
        {
            rNew = x; gNew = c; bNew = 0f;
        }
        else if (h < 3f / 6f)
        {
            rNew = 0f; gNew = c; bNew = x;
        }
        else if (h < 4f / 6f)
        {
            rNew = 0f; gNew = x; bNew = c;
        }
        else if (h < 5f / 6f)
        {
            rNew = x; gNew = 0f; bNew = c;
        }
        else
        {
            rNew = c; gNew = 0f; bNew = x;
        }
        
        EngineVec4 result = new EngineVec4(rNew + m, gNew + m, bNew + m, color.W);
        
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
        ImGui.Text("Hue Shift");
        ImGui.Separator();
        float hueShift = HueShift;
        if (ImGui.DragFloat("Hue Shift", ref hueShift, 0.01f, -1f, 1f))
        {
            HueShift = hueShift;
        }
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Range: -1 to 1 (full rotation)");
    }
}

public class RotatorNode : GraphNode
{
    public EngineVec2 Speed { get; set; } = new EngineVec2(0.0f, 0.0f);

    public RotatorNode(int id, NVector2 position) 
        : base(id, "Rotator", null, position, new NVector2(220f, 170f), new NVector4(0.27f, 0.37f, 0.6f, 1f),
            inputs: new[] { ("Speed X", NodeValueKind.Scalar), ("Speed Y", NodeValueKind.Scalar), ("Time", NodeValueKind.Scalar) },
            outputs: new[] { ("Offset X", NodeValueKind.Scalar), ("Offset Y", NodeValueKind.Scalar) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex >= OutputPins.Count)
            return null;

        float? speedXNullable = context.GetInputValue<float>(Id, 0);
        float? speedYNullable = context.GetInputValue<float>(Id, 1);
        float? timeNullable = context.GetInputValue<float>(Id, 2);
        
        float speedX = speedXNullable ?? Speed.X;
        float speedY = speedYNullable ?? Speed.Y;
        float time = timeNullable ?? TimeNode.GetCurrentTime();

        if (MathF.Abs(time) < 0.0001f)
        {
            return 0f;
        }
        
        if (outputIndex == 0)
        {
            float result = speedX * time;
            if (MathF.Abs(result) < 0.0001f)
                return 0f;
            return result;
        }
        else if (outputIndex == 1)
        {
            float result = speedY * time;
            if (MathF.Abs(result) < 0.0001f)
                return 0f;
            return result;
        }
        
        return 0f;
    }

    public override void DrawInspector()
    {
        ImGui.Spacing();
        ImGui.TextDisabled("Outputs rotation offset as two scalars (Offset X, Offset Y)");
    }
}
