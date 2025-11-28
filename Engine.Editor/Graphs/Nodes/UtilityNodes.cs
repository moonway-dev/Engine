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
        if (outputIndex == 0)
        {
            EngineVec4? inputNullable = context.GetInputValue<EngineVec4>(Id, 0);
            float? maskNullable = context.GetInputValue<float>(Id, 1);
            EngineVec4 input = inputNullable.HasValue ? inputNullable.Value : EngineVec4.Zero;
            float mask = maskNullable.HasValue ? maskNullable.Value : 1.0f;
            mask = System.Math.Clamp(mask, 0f, 1f);
            return new EngineVec4(input.X * mask, input.Y * mask, input.Z * mask, input.W * mask);
        }
        return null;
    }

    public override void DrawInspector()
    {
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "No editable properties");
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
        if (outputIndex == 0)
        {
            float? inputNullable = context.GetInputValue<float>(Id, 0);
            float input = inputNullable.HasValue ? inputNullable.Value : 0.5f;
            return System.Math.Clamp(input, MinValue, MaxValue);
        }
        return null;
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

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex == 0)
        {
            return _time;
        }
        return null;
    }

    public override void DrawInspector()
    {
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Outputs elapsed time");
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
        if (outputIndex == 0)
        {
            return _deltaTime;
        }
        return null;
    }

    public override void DrawInspector()
    {
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Outputs frame delta time");
        ImGui.Text($"Current Delta: {_deltaTime:F6}");
    }
}

public class PannerNode : GraphNode
{
    public EngineVec2 Speed { get; set; } = new EngineVec2(0.1f, 0.1f);

    public PannerNode(int id, NVector2 position) 
        : base(id, "Panner", null, position, new NVector2(220f, 170f), new NVector4(0.27f, 0.37f, 0.6f, 1f),
            inputs: new[] { ("UV", NodeValueKind.UV), ("Time", NodeValueKind.Scalar) },
            outputs: new[] { ("UV", NodeValueKind.UV) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex == 0)
        {
            EngineVec2? uvNullable = context.GetInputValue<EngineVec2>(Id, 0);
            float? timeNullable = context.GetInputValue<float>(Id, 1);
            EngineVec2 uv = uvNullable.HasValue ? uvNullable.Value : new EngineVec2(0.5f, 0.5f);
            float time = timeNullable.HasValue ? timeNullable.Value : 0.0f;
            return uv + new EngineVec2(Speed.X * time, Speed.Y * time);
        }
        return null;
    }

    public override void DrawInspector()
    {
        ImGui.Text("Speed");
        ImGui.Separator();
        float speedX = Speed.X;
        float speedY = Speed.Y;
        if (ImGui.DragFloat("Speed X", ref speedX, 0.01f))
        {
            Speed = new EngineVec2(speedX, speedY);
        }
        if (ImGui.DragFloat("Speed Y", ref speedY, 0.01f))
        {
            Speed = new EngineVec2(speedX, speedY);
        }
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
        if (outputIndex == 0)
        {
            EngineVec4? colorNullable = context.GetInputValue<EngineVec4>(Id, 0);
            float? hueShiftNullable = context.GetInputValue<float>(Id, 1);
            EngineVec4 color = colorNullable.HasValue ? colorNullable.Value : EngineVec4.One;
            float hueShift = hueShiftNullable.HasValue ? hueShiftNullable.Value : HueShift;

            float r = System.Math.Clamp(color.X, 0f, 1f);
            float g = System.Math.Clamp(color.Y, 0f, 1f);
            float b = System.Math.Clamp(color.Z, 0f, 1f);
            
            float max = System.MathF.Max(System.MathF.Max(r, g), b);
            float min = System.MathF.Min(System.MathF.Min(r, g), b);
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
            float x = c * (1f - System.MathF.Abs((h * 6f) % 2f - 1f));
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
            
            return new EngineVec4(rNew + m, gNew + m, bNew + m, color.W);
        }
        return null;
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
    public float Speed { get; set; } = 1.0f;

    public RotatorNode(int id, NVector2 position) 
        : base(id, "Rotator", null, position, new NVector2(220f, 170f), new NVector4(0.27f, 0.37f, 0.6f, 1f),
            inputs: new[] { ("Coordinate", NodeValueKind.UV), ("Speed", NodeValueKind.Scalar), ("Time", NodeValueKind.Scalar) },
            outputs: new[] { ("Result", NodeValueKind.UV) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex == 0)
        {
            EngineVec2? coordNullable = context.GetInputValue<EngineVec2>(Id, 0);
            float? speedNullable = context.GetInputValue<float>(Id, 1);
            float? timeNullable = context.GetInputValue<float>(Id, 2);
            
            EngineVec2 coord = coordNullable.HasValue ? coordNullable.Value : new EngineVec2(0.5f, 0.5f);
            float speed = speedNullable.HasValue ? speedNullable.Value : Speed;
            float time = timeNullable.HasValue ? timeNullable.Value : 0.0f;

            EngineVec2 center = new EngineVec2(0.5f, 0.5f);
            EngineVec2 offset = coord - center;
            
            float angle = speed * time * 2f * System.MathF.PI;
            float cos = System.MathF.Cos(angle);
            float sin = System.MathF.Sin(angle);
            
            float x = offset.X * cos - offset.Y * sin;
            float y = offset.X * sin + offset.Y * cos;
            
            return new EngineVec2(x, y) + center;
        }
        return null;
    }

    public override void DrawInspector()
    {
        ImGui.Text("Speed");
        ImGui.Separator();
        float speed = Speed;
        if (ImGui.DragFloat("Speed", ref speed, 0.01f))
        {
            Speed = speed;
        }
        ImGui.TextColored(new NVector4(0.7f, 0.7f, 0.7f, 1f), "Rotations per second");
    }
}

