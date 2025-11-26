using System.Numerics;
using EngineVec2 = Engine.Math.Vector2;
using EngineVec4 = Engine.Math.Vector4;
using ImGuiNET;
using Engine.Math;
using NVector2 = System.Numerics.Vector2;

namespace Engine.Editor.Graphs.Nodes;

public class TextureSampleNode : GraphNode
{
    public string? TexturePath { get; set; }
    public EngineVec2 UVOffset { get; set; } = EngineVec2.Zero;
    public EngineVec2 UVScale { get; set; } = EngineVec2.One;

    public TextureSampleNode(int id, NVector2 position) 
        : base(id, "Texture Sample", null, position, new NVector2(230f, 190f), new System.Numerics.Vector4(0.1f, 0.42f, 0.66f, 1f),
            inputs: new[] { ("UVs", NodeValueKind.UV) },
            outputs: new[] { ("RGB", NodeValueKind.Color), ("Alpha", NodeValueKind.Scalar) })
    {
    }

    public override object? EvaluateOutput(int outputIndex, NodeEvaluationContext context)
    {
        if (outputIndex == 0)
        {
            EngineVec2? uvNullable = context.GetInputValue<EngineVec2>(Id, 0);
            EngineVec2 uv = uvNullable.HasValue ? uvNullable.Value : new EngineVec2(0.5f, 0.5f);
            uv = new EngineVec2(uv.X * UVScale.X, uv.Y * UVScale.Y) + UVOffset;
            return SampleTexture(uv);
        }
        else if (outputIndex == 1)
        {
            EngineVec2? uvNullable = context.GetInputValue<EngineVec2>(Id, 0);
            EngineVec2 uv = uvNullable.HasValue ? uvNullable.Value : new EngineVec2(0.5f, 0.5f);
            uv = new EngineVec2(uv.X * UVScale.X, uv.Y * UVScale.Y) + UVOffset;
            EngineVec4 color = SampleTexture(uv);
            return color.W;
        }
        return null;
    }

    private EngineVec4 SampleTexture(EngineVec2 uv)
    {
        if (string.IsNullOrEmpty(TexturePath))
        {
            return new EngineVec4(uv.X, uv.Y, 0.5f, 1.0f);
        }

        float u = System.MathF.Abs(uv.X % 1.0f);
        float v = System.MathF.Abs(uv.Y % 1.0f);

        float r = (System.MathF.Sin(u * 10f) + 1f) * 0.5f;
        float g = (System.MathF.Sin(v * 10f) + 1f) * 0.5f;
        float b = (System.MathF.Sin((u + v) * 5f) + 1f) * 0.5f;

        return new EngineVec4(r, g, b, 1.0f);
    }

    public override void DrawInspector()
    {
        ImGui.Text("Texture");
        ImGui.Separator();
        string? texturePath = TexturePath ?? "";
        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(texturePath + "\0");
        if (ImGui.InputText("Path", buffer, 256))
        {
            TexturePath = System.Text.Encoding.UTF8.GetString(buffer).TrimEnd('\0');
            if (string.IsNullOrEmpty(TexturePath))
                TexturePath = null;
        }

        ImGui.Spacing();
        ImGui.Text("UV Settings");
        ImGui.Separator();
        float offsetX = UVOffset.X;
        float offsetY = UVOffset.Y;
        if (ImGui.DragFloat("Offset X", ref offsetX, 0.01f))
        {
            UVOffset = new EngineVec2(offsetX, offsetY);
        }
        if (ImGui.DragFloat("Offset Y", ref offsetY, 0.01f))
        {
            UVOffset = new EngineVec2(offsetX, offsetY);
        }

        float scaleX = UVScale.X;
        float scaleY = UVScale.Y;
        if (ImGui.DragFloat("Scale X", ref scaleX, 0.01f, 0.01f, 10f))
        {
            UVScale = new EngineVec2(scaleX, scaleY);
        }
        if (ImGui.DragFloat("Scale Y", ref scaleY, 0.01f, 0.01f, 10f))
        {
            UVScale = new EngineVec2(scaleX, scaleY);
        }
    }
}

